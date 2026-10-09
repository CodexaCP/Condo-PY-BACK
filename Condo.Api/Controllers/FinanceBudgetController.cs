using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Presupuesto del modulo "Finanzas del edificio" (fase 3): grilla mensual por rubro de ingresos y gastos, copiar del ejercicio
/// anterior o completar con el promedio real, y presupuesto vs. real con semaforo. Lo edita el SuperAdmin o el Administrador de
/// empresa; los demas roles lo consultan. Consultarlo no exige la configuracion completa; compararlo con lo real si.
/// </summary>
[Route("api/finance")]
public class FinanceBudgetController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    FinanceBudgetService budgets,
    FinanceReportService reports) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    private const int MaxCells = 2000;
    private const decimal MaxAmount = 9_999_999_999_999m;

    [HttpGet("budget")]
    public async Task<ActionResult<FinanceBudgetDto>> GetBudget(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var startMonth = await StartMonthAsync(buildingId, cancellationToken);
        var fy = fiscalYear ?? FinancePeriods.FiscalYearOf(FinancePeriods.Today(), startMonth);
        if (fy is < 2000 or > 2100)
        {
            return BadRequest("El ejercicio no es válido.");
        }

        return Ok(await BuildBudgetAsync(buildingId, fy, startMonth, 0, cancellationToken));
    }

    [HttpPut("budget")]
    public async Task<ActionResult<FinanceBudgetDto>> UpdateBudget(
        [FromQuery] Guid buildingId, [FromQuery] int fiscalYear, [FromBody] FinanceBudgetUpdateRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: true, cancellationToken, budget: true);
        if (denied is not null)
        {
            return denied;
        }

        var startMonth = await StartMonthAsync(buildingId, cancellationToken);
        if (fiscalYear is < 2000 or > 2100)
        {
            return BadRequest("El ejercicio no es válido.");
        }

        if (request.Cells.Count > MaxCells)
        {
            return BadRequest($"Se pueden guardar hasta {MaxCells} celdas por vez.");
        }

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        var categories = await LoadCategoriesAsync(buildingId, cancellationToken);
        var budgetable = FinanceBudgetCalculator.BudgetableCategories(categories).ToDictionary(c => c.Id);
        var months = FinancePeriods.FiscalMonths(fiscalYear, startMonth).ToHashSet();

        var seen = new HashSet<(Guid, int, int)>();
        foreach (var cell in request.Cells)
        {
            if (!budgetable.TryGetValue(cell.CategoryId, out var category))
            {
                return BadRequest("Hay un rubro que no existe en este edificio o que no se puede presupuestar (solo los subrubros de ingresos y gastos).");
            }

            if (!months.Contains((cell.Year, cell.Month)))
            {
                return BadRequest($"El mes {cell.Month:00}/{cell.Year} no pertenece al ejercicio {fiscalYear}.");
            }

            if (cell.Amount < 0)
            {
                return BadRequest("Los importes del presupuesto no pueden ser negativos.");
            }

            if (cell.Amount > MaxAmount)
            {
                return BadRequest("Un importe del presupuesto es demasiado grande.");
            }

            if (!category.IsActive && cell.Amount != 0)
            {
                return BadRequest($"El rubro {category.Code} está desactivado: no se le puede cargar presupuesto nuevo.");
            }

            if (!seen.Add((cell.CategoryId, cell.Year, cell.Month)))
            {
                return BadRequest("Hay una celda repetida (mismo rubro y mes).");
            }
        }

        var existing = await LoadLinesAsync(buildingId, fiscalYear, startMonth, cancellationToken);
        var affected = 0;
        foreach (var cell in request.Cells)
        {
            if (Upsert(existing, buildingId, companyId.Value, cell.CategoryId, cell.Year, cell.Month, decimal.Round(cell.Amount, 2)))
            {
                affected++;
            }
        }

        if (affected > 0)
        {
            Audit.Add(companyId.Value, buildingId, ConfigSectionKeys.Budget, "Updated",
                $"Presupuesto del ejercicio {fiscalYear}: se modificaron {affected} celdas.", "BudgetLine", null);
        }

        var conflict = await SaveOrConflictAsync(cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        return Ok(await BuildBudgetAsync(buildingId, fiscalYear, startMonth, affected, cancellationToken));
    }

    // Copia el presupuesto del ejercicio anterior, mes por mes. Por defecto solo completa las celdas vacias.
    [HttpPost("budget/copy-previous")]
    public async Task<ActionResult<FinanceBudgetDto>> CopyPrevious(
        [FromQuery] Guid buildingId, [FromQuery] int fiscalYear, [FromQuery] bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var denied = await RequireModuleAsync(buildingId, write: true, cancellationToken, budget: true);
        if (denied is not null)
        {
            return denied;
        }

        var startMonth = await StartMonthAsync(buildingId, cancellationToken);
        if (fiscalYear is < 2001 or > 2100)
        {
            return BadRequest("El ejercicio no es válido.");
        }

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        var categories = await LoadCategoriesAsync(buildingId, cancellationToken);
        var budgetable = FinanceBudgetCalculator.BudgetableCategories(categories).Where(c => c.IsActive).Select(c => c.Id).ToHashSet();

        var sourceMonths = FinancePeriods.FiscalMonths(fiscalYear - 1, startMonth);
        var targetMonths = FinancePeriods.FiscalMonths(fiscalYear, startMonth);
        var source = await LoadLinesAsync(buildingId, fiscalYear - 1, startMonth, cancellationToken);
        var existing = await LoadLinesAsync(buildingId, fiscalYear, startMonth, cancellationToken);

        var affected = 0;
        foreach (var line in source.Where(l => l.Amount != 0m && budgetable.Contains(l.CategoryId)).ToList())
        {
            var index = sourceMonths.IndexOf((line.Year, line.Month));
            if (index < 0)
            {
                continue;
            }

            var target = targetMonths[index];
            var current = existing.FirstOrDefault(l => l.CategoryId == line.CategoryId && l.Year == target.Year && l.Month == target.Month);
            if (current is not null && current.Amount != 0m && !overwrite)
            {
                continue;
            }

            if (Upsert(existing, buildingId, companyId.Value, line.CategoryId, target.Year, target.Month, line.Amount))
            {
                affected++;
            }
        }

        if (affected > 0)
        {
            Audit.Add(companyId.Value, buildingId, ConfigSectionKeys.Budget, "Copied",
                $"Presupuesto del ejercicio {fiscalYear}: se copió del ejercicio anterior ({affected} celdas{(overwrite ? ", reemplazando las cargadas" : string.Empty)}).",
                "BudgetLine", null);
        }

        var conflict = await SaveOrConflictAsync(cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        return Ok(await BuildBudgetAsync(buildingId, fiscalYear, startMonth, affected, cancellationToken));
    }

    // Completa el presupuesto con el promedio real de los ultimos meses completos (gastos cargados e ingresos cobrados por rubro).
    [HttpPost("budget/fill-from-average")]
    public async Task<ActionResult<FinanceBudgetDto>> FillFromAverage(
        [FromQuery] Guid buildingId,
        [FromQuery] int fiscalYear,
        [FromQuery] int months = 3,
        [FromQuery] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken, write: true, budget: true);
        if (denied is not null)
        {
            return denied;
        }

        if (fiscalYear is < 2000 or > 2100)
        {
            return BadRequest("El ejercicio no es válido.");
        }

        if (months is < 1 or > 12)
        {
            return BadRequest("La cantidad de meses del promedio debe estar entre 1 y 12.");
        }

        var companyId = await ResolveCompanyIdAsync(buildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        // Ultimos `months` meses completos antes del actual, sin pasar antes del mes de arranque.
        var today = FinancePeriods.Today();
        var current = new DateOnly(today.Year, today.Month, 1);
        var first = new DateOnly(ctx!.StartDate.Year, ctx.StartDate.Month, 1);
        var window = Enumerable.Range(1, months).Select(i => current.AddMonths(-i)).Where(d => d >= first).OrderBy(d => d).ToList();
        if (window.Count == 0)
        {
            return BadRequest("Todavía no hay meses completos desde la fecha de arranque para calcular un promedio.");
        }

        var actuals = await budgets.GetActualsAsync(ctx, window[0], FinancePeriods.EndOfMonth(window[^1].Year, window[^1].Month), cancellationToken);
        var targetMonths = FinancePeriods.FiscalMonths(fiscalYear, ctx.FiscalYearStartMonth);
        var existing = await LoadLinesAsync(buildingId, fiscalYear, ctx.FiscalYearStartMonth, cancellationToken);

        var affected = 0;
        foreach (var category in FinanceBudgetCalculator.BudgetableCategories(ctx.Categories.ToList()).Where(c => c.IsActive))
        {
            var total = window.Sum(d => actuals.GetValueOrDefault((d.Year, d.Month, category.RubroKey)));
            var average = decimal.Round(total / window.Count, 0, MidpointRounding.AwayFromZero);
            if (average <= 0m)
            {
                continue;
            }

            foreach (var m in targetMonths)
            {
                var line = existing.FirstOrDefault(l => l.CategoryId == category.Id && l.Year == m.Year && l.Month == m.Month);
                if (line is not null && line.Amount != 0m && !overwrite)
                {
                    continue;
                }

                if (Upsert(existing, buildingId, companyId.Value, category.Id, m.Year, m.Month, average))
                {
                    affected++;
                }
            }
        }

        if (affected > 0)
        {
            Audit.Add(companyId.Value, buildingId, ConfigSectionKeys.Budget, "Filled",
                $"Presupuesto del ejercicio {fiscalYear}: se completó con el promedio real de los últimos {window.Count} meses ({affected} celdas{(overwrite ? ", reemplazando las cargadas" : string.Empty)}).",
                "BudgetLine", null);
        }

        var conflict = await SaveOrConflictAsync(cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        return Ok(await BuildBudgetAsync(buildingId, fiscalYear, ctx.FiscalYearStartMonth, affected, cancellationToken));
    }

    [HttpGet("budget-vs-actual")]
    public async Task<ActionResult<FinanceBudgetVsActualDto>> GetBudgetVsActual(
        [FromQuery] Guid buildingId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var (result, error) = await reports.BudgetVsActualAsync(ctx!, year, month, cancellationToken);
        return error is null ? Ok(result) : BadRequest(error);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<int> StartMonthAsync(Guid buildingId, CancellationToken cancellationToken) =>
        await Db.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => (int?)x.FiscalYearStartMonth)
            .FirstOrDefaultAsync(cancellationToken) ?? 1;

    private async Task<List<LedgerCategory>> LoadCategoriesAsync(Guid buildingId, CancellationToken cancellationToken) =>
        await Db.LedgerCategories.AsNoTracking().Where(x => !x.IsDeleted && x.BuildingId == buildingId).ToListAsync(cancellationToken);

    // Lineas (rastreadas) del ejercicio.
    private async Task<List<BudgetLine>> LoadLinesAsync(Guid buildingId, int fiscalYear, int startMonth, CancellationToken cancellationToken)
    {
        var months = FinancePeriods.FiscalMonths(fiscalYear, startMonth);
        var fromKey = months[0].Year * 100 + months[0].Month;
        var toKey = months[11].Year * 100 + months[11].Month;

        return await Db.BudgetLines
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Year * 100 + x.Month >= fromKey && x.Year * 100 + x.Month <= toKey)
            .ToListAsync(cancellationToken);
    }

    // Crea o actualiza la celda; una celda en cero se conserva en cero (no se borra). Devuelve si algo cambio.
    private bool Upsert(List<BudgetLine> existing, Guid buildingId, Guid companyId, Guid categoryId, int year, int month, decimal amount)
    {
        var line = existing.FirstOrDefault(l => l.CategoryId == categoryId && l.Year == year && l.Month == month);
        if (line is null)
        {
            if (amount == 0m)
            {
                return false;
            }

            line = new BudgetLine { CompanyId = companyId, BuildingId = buildingId, CategoryId = categoryId, Year = year, Month = month, Amount = amount };
            Db.BudgetLines.Add(line);
            existing.Add(line);
            return true;
        }

        if (line.Amount == amount)
        {
            return false;
        }

        line.Amount = amount;
        return true;
    }

    private Task<FinanceBudgetDto> BuildBudgetAsync(Guid buildingId, int fiscalYear, int startMonth, int affected, CancellationToken cancellationToken) =>
        reports.BudgetAsync(buildingId, fiscalYear, startMonth, affected, cancellationToken);
}
