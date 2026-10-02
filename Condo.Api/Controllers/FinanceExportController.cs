using ClosedXML.Excel;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Exportacion a Excel del modulo "Finanzas del edificio" para el contador: una hoja por reporte (movimientos, flujo de caja,
/// presupuesto, presupuesto vs. real, fondo de reserva y plan de cuentas) y un paquete con todo. Es de consulta (la ven los cuatro
/// roles administrativos con acceso al edificio), usa los mismos numeros y reglas que las pantallas (<see cref="FinanceReportService"/>)
/// y exige la configuracion inicial completa.
/// </summary>
[Route("api/finance/export")]
public class FinanceExportController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    FinanceReportService reports) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    // Hora local de Paraguay (UTC-3), la misma que usa el libro para decidir "hoy".
    private static DateTime NowLocal() => DateTime.UtcNow.AddHours(-3);

    [HttpGet("movements")]
    public async Task<IActionResult> Movements(
        [FromQuery] Guid buildingId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? accountId,
        [FromQuery] bool unassigned = false,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] LedgerDirection? direction = null,
        [FromQuery] bool newestFirst = false,
        CancellationToken cancellationToken = default)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var (page, error) = await reports.MovementsAsync(
            ctx!, new FinanceReportService.MovementsQuery(from, to, accountId, unassigned, categoryId, direction, newestFirst, Unpaged: true), cancellationToken);
        if (error is not null)
        {
            return BadRequest(error);
        }

        var scope = new List<string>();
        if (accountId.HasValue) scope.Add($"Cuenta: {ctx!.Accounts.First(a => a.Id == accountId.Value).Name}.");
        else if (unassigned) scope.Add("Solo lo que no tiene cuenta asignada.");
        if (categoryId.HasValue) scope.Add($"Rubro: {ctx!.ById[categoryId.Value].Code} · {ctx.ById[categoryId.Value].Name}.");
        if (direction.HasValue) scope.Add(direction == LedgerDirection.In ? "Solo entradas." : "Solo salidas.");

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddMovements(workbook, ctx!, page!, string.Join(" ", scope));
        return Excel(workbook, FinanceExcelExporter.FileName("movimientos", ctx!.BuildingName, $"{page!.From:yyyyMMdd}-{page.To:yyyyMMdd}"));
    }

    [HttpGet("cash-flow")]
    public async Task<IActionResult> CashFlow([FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var (flow, error) = await reports.CashFlowAsync(ctx!, fiscalYear, cancellationToken);
        if (error is not null)
        {
            return BadRequest(error);
        }

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddCashFlow(workbook, ctx!, flow!);
        return Excel(workbook, FinanceExcelExporter.FileName("flujo-de-caja", ctx!.BuildingName, flow!.FiscalYear.ToString()));
    }

    [HttpGet("budget")]
    public async Task<IActionResult> Budget([FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var fy = fiscalYear ?? FinancePeriods.FiscalYearOf(FinancePeriods.Today(), ctx!.FiscalYearStartMonth);
        if (fy is < 2000 or > 2100)
        {
            return BadRequest("El ejercicio no es válido.");
        }

        var budget = await reports.BudgetAsync(buildingId, fy, ctx!.FiscalYearStartMonth, 0, cancellationToken);
        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddBudget(workbook, ctx, budget);
        return Excel(workbook, FinanceExcelExporter.FileName("presupuesto", ctx.BuildingName, fy.ToString()));
    }

    [HttpGet("budget-vs-actual")]
    public async Task<IActionResult> BudgetVsActual(
        [FromQuery] Guid buildingId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var (vs, error) = await reports.BudgetVsActualAsync(ctx!, year, month, cancellationToken);
        if (error is not null)
        {
            return BadRequest(error);
        }

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddBudgetVsActual(workbook, ctx!, vs!);
        return Excel(workbook, FinanceExcelExporter.FileName("presupuesto-vs-real", ctx!.BuildingName, $"{vs!.Year}{vs.Month:00}"));
    }

    [HttpGet("reserve-fund")]
    public async Task<IActionResult> ReserveFund(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var (fund, error) = await reports.ReserveFundAsync(ctx!, from, to, cancellationToken, unpagedMovements: true);
        if (error is not null)
        {
            return BadRequest(error);
        }

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddReserveFund(workbook, ctx!, fund!);
        return Excel(workbook, FinanceExcelExporter.FileName("fondo-de-reserva", ctx!.BuildingName, $"{fund!.AsOf:yyyyMMdd}"));
    }

    [HttpGet("chart")]
    public async Task<IActionResult> Chart([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddChart(workbook, ctx!);
        return Excel(workbook, FinanceExcelExporter.FileName("plan-de-cuentas", ctx!.BuildingName, FinancePeriods.Today().ToString("yyyyMMdd")));
    }

    /// <summary>Paquete para el contador: resumen, plan de cuentas, saldos, movimientos, flujo de caja, presupuesto, presupuesto vs. real y fondo, del ejercicio.</summary>
    [HttpGet("accountant-pack")]
    public async Task<IActionResult> AccountantPack([FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var today = FinancePeriods.Today();
        var fy = fiscalYear ?? FinancePeriods.FiscalYearOf(today, ctx!.FiscalYearStartMonth);
        if (fy is < 2000 or > 2100)
        {
            return BadRequest("El ejercicio no es válido.");
        }

        var (fiscalStart, fiscalEnd) = FinancePeriods.FiscalYearRange(fy, ctx!.FiscalYearStartMonth);
        if (fiscalStart > today)
        {
            return BadRequest("El ejercicio elegido todavía no empezó.");
        }

        var asOf = fiscalEnd > today ? today : fiscalEnd;
        if (asOf < ctx.StartDate)
        {
            return BadRequest("El ejercicio elegido es anterior a la fecha de arranque del módulo: todavía no tiene movimientos.");
        }

        var (movements, movementsError) = await reports.MovementsAsync(
            ctx, new FinanceReportService.MovementsQuery(fiscalStart, asOf, NewestFirst: false, Unpaged: true), cancellationToken);
        if (movementsError is not null)
        {
            return BadRequest(movementsError);
        }

        var (flow, flowError) = await reports.CashFlowAsync(ctx, fy, cancellationToken);
        if (flowError is not null)
        {
            return BadRequest(flowError);
        }

        var budget = await reports.BudgetAsync(buildingId, fy, ctx.FiscalYearStartMonth, 0, cancellationToken);
        var balances = await reports.BalancesAsync(ctx, asOf, cancellationToken);
        var (vs, vsError) = await reports.BudgetVsActualAsync(ctx, asOf.Year, asOf.Month, cancellationToken);
        if (vsError is not null)
        {
            return BadRequest(vsError);
        }

        var (fund, _) = await reports.ReserveFundAsync(ctx, fiscalStart, asOf, cancellationToken, unpagedMovements: true);

        using var workbook = new XLWorkbook();
        var sheets = new List<string> { "Plan de cuentas", "Saldos", "Movimientos", "Flujo de caja", "Presupuesto", "Presupuesto vs real" };
        if (fund is { HasFundAccount: true }) sheets.Add("Fondo de reserva");

        FinanceExcelExporter.AddSummary(
            workbook, ctx, NowLocal(), $"Ejercicio {fy}: {fiscalStart:dd/MM/yyyy} al {fiscalEnd:dd/MM/yyyy} (datos hasta el {asOf:dd/MM/yyyy})", sheets);
        FinanceExcelExporter.AddChart(workbook, ctx);
        FinanceExcelExporter.AddBalances(workbook, balances);
        FinanceExcelExporter.AddMovements(workbook, ctx, movements!);
        FinanceExcelExporter.AddCashFlow(workbook, ctx, flow!);
        FinanceExcelExporter.AddBudget(workbook, ctx, budget);
        FinanceExcelExporter.AddBudgetVsActual(workbook, ctx, vs!);
        if (fund is { HasFundAccount: true })
        {
            FinanceExcelExporter.AddReserveFund(workbook, ctx, fund);
        }

        return Excel(workbook, FinanceExcelExporter.FileName("paquete-contador", ctx.BuildingName, fy.ToString()));
    }

    private FileContentResult Excel(XLWorkbook workbook, string fileName) =>
        File(FinanceExcelExporter.Save(workbook), FinanceExcelExporter.ContentType, fileName);
}
