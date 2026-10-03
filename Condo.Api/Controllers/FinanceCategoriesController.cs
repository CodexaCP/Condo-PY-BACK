using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Plan de cuentas del edificio: arbol de hasta <see cref="FinanceChartTemplate.MaxDepth"/> niveles (clase → grupo → cuenta) con codigos
/// editables. Nace del plan generico de CondoPY al habilitar el modulo y se puede reemplazar por el plan del cliente (importado de Excel),
/// copiar de otro edificio o ajustar a mano. Solo las cuentas finales de ingresos y egresos reciben gastos e ingresos; el resto es de
/// referencia y se exporta al contador. Una cuenta puede tener una <i>funcion especial</i> (<c>SystemKey</c>): cobranza de expensas o
/// cuenta por defecto de una categoria. Solo el SuperAdmin modifica el plan (servicio de configuracion).
/// </summary>
[Route("api/finance/categories")]
public class FinanceCategoriesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinancePlanCopier copier,
    FinancePlanService planService) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    private const int MaxCodeLength = 30;
    private const int MaxNameLength = 200;
    private const int MaxExternalCodeLength = 50;
    private const long MaxPlanFileBytes = 5 * 1024 * 1024;
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LedgerCategoryDto>>> GetAll([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var items = await Db.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);

        var parentIds = items.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).ToHashSet();
        var withMovements = await MovementCategoryIdsAsync(buildingId, cancellationToken);

        return Ok(items
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => ToDto(x, parentIds.Contains(x.Id), withMovements.Contains(x.Id)))
            .ToList());
    }

    [HttpPost]
    public async Task<ActionResult<LedgerCategoryDto>> Create([FromBody] LedgerCategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(request.BuildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var companyId = await ResolveCompanyIdAsync(request.BuildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        var (error, parent) = await ValidateAsync(request, request.BuildingId, null, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var type = parent?.Type ?? request.Type;
        var (categoryError, expenseCategory, incomeCategory) = ResolveSettlementCategory(request, type, parent is not null, null, null);
        if (categoryError is not null)
        {
            return BadRequest(categoryError);
        }

        var entity = new LedgerCategory
        {
            CompanyId = companyId.Value,
            BuildingId = request.BuildingId,
            ParentId = parent?.Id,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Type = type,
            ExternalCode = NormalizeExternalCode(request.ExternalCode),
            ExpenseCategory = expenseCategory,
            IncomeCategory = incomeCategory,
            IsActive = request.IsActive
        };

        var roleError = await ApplyRoleAsync(entity, request.SystemKey, isLeaf: parent is not null, hasChildren: false, hasMovements: false, cancellationToken);
        if (roleError is not null)
        {
            return BadRequest(roleError);
        }

        Db.LedgerCategories.Add(entity);
        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(ToDto(entity, false, false));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LedgerCategoryDto>> Update(Guid id, [FromBody] LedgerCategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        var entity = await Db.LedgerCategories.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var denied = await RequireModuleAsync(entity.BuildingId, write: true, cancellationToken, hideMissingAccess: true);
        if (denied is not null)
        {
            return denied;
        }

        var hasChildren = await Db.LedgerCategories.AnyAsync(x => !x.IsDeleted && x.ParentId == entity.Id, cancellationToken);
        var hasMovements = await HasMovementsAsync(entity.Id, cancellationToken);

        var (error, parent) = await ValidateAsync(request, entity.BuildingId, entity, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (entity.SystemKey is not null)
        {
            // Cuenta con funcion especial: su lugar en el arbol y su tipo no cambian.
            if (request.ParentId != entity.ParentId || (parent is null && request.Type != entity.Type))
            {
                return BadRequest("Las cuentas con función especial no se pueden mover ni cambiar de tipo. Podés renombrarlas, cambiarles el código o desactivarlas.");
            }
        }
        else
        {
            var newType = parent?.Type ?? request.Type;
            if (hasChildren && newType != entity.Type)
            {
                return BadRequest("Este grupo tiene subcuentas: no se le puede cambiar el tipo.");
            }

            if (newType != entity.Type && await HasBudgetAsync(entity.Id, cancellationToken))
            {
                return BadRequest("Esta cuenta tiene presupuesto cargado: no se le puede cambiar el tipo.");
            }

            if (hasMovements && (newType != entity.Type || parent is null))
            {
                return BadRequest("Esta cuenta ya tiene gastos o ingresos cargados: no se le puede cambiar el tipo ni pasarla a primer nivel. Si ya no la usás, desactivala.");
            }

            // Sin dato en el pedido se conserva la categoria que ya tenia.
            var currentExpense = FinanceChartTemplate.ExpenseCategoryOf(entity);
            var currentIncome = FinanceChartTemplate.IncomeCategoryOf(entity);
            var (categoryError, expenseCategory, incomeCategory) = ResolveSettlementCategory(request, newType, parent is not null && !hasChildren, currentExpense, currentIncome);
            if (categoryError is not null)
            {
                return BadRequest(categoryError);
            }

            if (hasMovements && (expenseCategory != currentExpense || incomeCategory != currentIncome))
            {
                return BadRequest("Esta cuenta ya tiene gastos o ingresos cargados: no se le puede cambiar la categoría de la liquidación, porque esos movimientos ya cuentan con la actual.");
            }

            entity.ParentId = parent?.Id;
            entity.Type = newType;
            entity.ExpenseCategory = expenseCategory;
            entity.IncomeCategory = incomeCategory;
        }

        var roleError = await ApplyRoleAsync(entity, request.SystemKey, isLeaf: entity.ParentId.HasValue && !hasChildren, hasChildren, hasMovements, cancellationToken);
        if (roleError is not null)
        {
            return BadRequest(roleError);
        }

        entity.Code = request.Code.Trim();
        entity.Name = request.Name.Trim();
        entity.ExternalCode = NormalizeExternalCode(request.ExternalCode);
        entity.IsActive = request.IsActive;

        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(ToDto(entity, hasChildren, hasMovements));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await Db.LedgerCategories.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var denied = await RequireModuleAsync(entity.BuildingId, write: true, cancellationToken, hideMissingAccess: true);
        if (denied is not null)
        {
            return denied;
        }

        if (entity.SystemKey is not null)
        {
            return BadRequest("Las cuentas con función especial no se eliminan: si no las usás, desactivalas (o pasá la función a otra cuenta).");
        }

        if (await Db.LedgerCategories.AnyAsync(x => !x.IsDeleted && x.ParentId == entity.Id, cancellationToken))
        {
            return BadRequest("El grupo tiene subcuentas. Eliminá o movés primero sus subcuentas.");
        }

        // Una cuenta con gastos o ingresos cargados, o con presupuesto, solo se puede desactivar.
        if (await HasMovementsAsync(entity.Id, cancellationToken))
        {
            return BadRequest("La cuenta tiene gastos o ingresos cargados: no se puede eliminar. Si ya no la usás, desactivala.");
        }

        if (await HasBudgetAsync(entity.Id, cancellationToken))
        {
            return BadRequest("La cuenta tiene presupuesto cargado: pasalo a cero o desactivala.");
        }

        entity.IsDeleted = true;
        await Db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Activa o desactiva varias cuentas a la vez (un grupo con todas sus cuentas). Al activar se activan tambien sus grupos; al desactivar un grupo, todo lo que contiene.</summary>
    [HttpPost("bulk-active")]
    public async Task<ActionResult<object>> BulkActive([FromBody] LedgerCategoryBulkActiveRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(request.BuildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        if (request.Ids is null || request.Ids.Count == 0)
        {
            return BadRequest("No se indicó ninguna cuenta.");
        }

        var all = await Db.LedgerCategories.Where(x => !x.IsDeleted && x.BuildingId == request.BuildingId).ToListAsync(cancellationToken);
        var byId = all.ToDictionary(x => x.Id);
        if (request.Ids.Any(i => !byId.ContainsKey(i)))
        {
            return BadRequest("Alguna de las cuentas no existe en este edificio.");
        }

        var childrenOf = all.Where(x => x.ParentId.HasValue).ToLookup(x => x.ParentId!.Value);
        var targets = new HashSet<Guid>();

        void AddWithDescendants(Guid id)
        {
            if (!targets.Add(id)) return;
            foreach (var child in childrenOf[id]) AddWithDescendants(child.Id);
        }

        foreach (var id in request.Ids.Distinct())
        {
            AddWithDescendants(id);
        }

        if (request.IsActive)
        {
            // Una cuenta activa necesita sus grupos activos.
            foreach (var id in targets.ToList())
            {
                var current = byId[id];
                while (current.ParentId.HasValue && byId.TryGetValue(current.ParentId.Value, out var parent))
                {
                    targets.Add(parent.Id);
                    current = parent;
                }
            }
        }

        var changed = 0;
        foreach (var id in targets)
        {
            if (byId[id].IsActive != request.IsActive)
            {
                byId[id].IsActive = request.IsActive;
                changed++;
            }
        }

        await Db.SaveChangesAsync(cancellationToken);
        return Ok(new { changed });
    }

    /// <summary>
    /// Copia el plan de cuentas de otro edificio (nombres, codigos del contador, cuentas activas y funciones) sin tocar
    /// los movimientos ni el presupuesto del edificio destino. Hay que poder operar en los dos edificios.
    /// </summary>
    [HttpPost("copy-from")]
    public async Task<ActionResult<LedgerCategoryCopyResultDto>> CopyFrom([FromBody] LedgerCategoryCopyRequest request, CancellationToken cancellationToken)
    {
        if (request.SourceBuildingId == request.TargetBuildingId)
        {
            return BadRequest("Elegí un edificio de origen distinto del edificio que estás configurando.");
        }

        var denied = await RequireModuleAsync(request.TargetBuildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var deniedSource = await RequireModuleAsync(request.SourceBuildingId, write: false, cancellationToken, hideMissingAccess: true);
        if (deniedSource is not null)
        {
            return deniedSource;
        }

        var companyId = await ResolveCompanyIdAsync(request.TargetBuildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        var result = await copier.CopyAsync(request.SourceBuildingId, request.TargetBuildingId, companyId.Value, cancellationToken);
        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(result);
    }

    // ── Plan generico y plan del cliente ──────────────────────────────────────

    /// <summary>Lo que se perderia (se desvincula o se borra) al reemplazar el plan del edificio.</summary>
    [HttpGet("replace-impact")]
    public async Task<ActionResult<LedgerPlanImpactDto>> ReplaceImpact([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: true, cancellationToken);
        return denied ?? Ok(await planService.GetImpactAsync(buildingId, cancellationToken));
    }

    /// <summary>Aplica el plan generico de CondoPY: reemplaza el plan entero o solo agrega las cuentas que faltan.</summary>
    [HttpPost("apply-template")]
    public async Task<ActionResult<LedgerPlanApplyResultDto>> ApplyTemplate([FromBody] LedgerPlanApplyRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(request.BuildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var companyId = await ResolveCompanyIdAsync(request.BuildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        return await ApplyAsync(request.BuildingId, companyId.Value, FinanceChartTemplate.Specs, request.Mode, request.ConfirmReplace, cancellationToken);
    }

    [HttpGet("import-template")]
    public async Task<IActionResult> DownloadImportTemplate([FromQuery] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        return File(FinancePlanExcel.BuildTemplate(), XlsxContentType, "plan-de-cuentas-plantilla.xlsx");
    }

    /// <summary>Lee el Excel del plan del cliente y devuelve el arbol resuelto (padres, tipos, categorias sugeridas, errores y avisos). No guarda nada.</summary>
    [HttpPost("import/preview")]
    [RequestSizeLimit(MaxPlanFileBytes)]
    public async Task<ActionResult<LedgerPlanImportPreviewDto>> ImportPreview(IFormFile file, [FromForm] Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        if (file is null || file.Length == 0) return BadRequest("No se recibió ningún archivo.");
        if (file.Length > MaxPlanFileBytes) return BadRequest("El archivo no puede superar los 5 MB.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Solo se permiten archivos Excel (.xlsx).");
        }

        List<PlanRowInput>? rows;
        string? parseError;
        await using (var stream = file.OpenReadStream())
        {
            (rows, parseError) = FinancePlanExcel.Parse(stream);
        }

        if (rows is null)
        {
            return BadRequest(parseError ?? "No se pudo leer el archivo.");
        }

        var resolution = FinancePlanValidator.Resolve(rows, inferParents: true, inferTypes: true, suggestCategories: true, childTypeFollowsParent: false);
        return Ok(new LedgerPlanImportPreviewDto
        {
            Rows = resolution.Rows.Select(ToRowDto).ToList(),
            ErrorCount = resolution.ErrorCount,
            WarningCount = resolution.Rows.Sum(r => r.Warnings.Count),
            HasErrors = resolution.HasErrors,
            Impact = await planService.GetImpactAsync(buildingId, cancellationToken)
        });
    }

    /// <summary>Confirma la importacion con las filas de la vista previa (corregidas por el usuario). Se vuelve a validar todo en el servidor.</summary>
    [HttpPost("import/commit")]
    public async Task<ActionResult<LedgerPlanApplyResultDto>> ImportCommit([FromBody] LedgerPlanImportCommitRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(request.BuildingId, write: true, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var companyId = await ResolveCompanyIdAsync(request.BuildingId, cancellationToken);
        if (!companyId.HasValue)
        {
            return BadRequest(NoCompanyMessage);
        }

        if (request.Rows is null || request.Rows.Count == 0)
        {
            return BadRequest("No hay cuentas para importar.");
        }

        var rows = request.Rows.Select(r => new PlanRowInput
        {
            RowNumber = r.RowNumber,
            Code = r.Code,
            Name = r.Name,
            ParentCode = r.ParentCode,
            Type = Enum.IsDefined(r.Type) ? r.Type : null,
            ExternalCode = r.ExternalCode,
            IsActive = r.IsActive,
            SystemKey = r.SystemKey,
            ExpenseCategory = r.ExpenseCategory,
            IncomeCategory = r.IncomeCategory
        }).ToList();

        var resolution = FinancePlanValidator.Resolve(rows, inferParents: false, inferTypes: false, suggestCategories: false, childTypeFollowsParent: true);
        if (resolution.HasErrors)
        {
            var first = resolution.Rows.Where(r => r.Errors.Count > 0).Take(5)
                .Select(r => $"Fila {r.RowNumber} ({r.Code}): {r.Errors[0]}");
            return BadRequest($"El plan tiene {resolution.ErrorCount} errores. {string.Join(" ", first)}");
        }

        return await ApplyAsync(request.BuildingId, companyId.Value, resolution.Rows.Select(r => r.ToSpec()).ToList(), request.Mode, request.ConfirmReplace, cancellationToken);
    }

    private async Task<ActionResult<LedgerPlanApplyResultDto>> ApplyAsync(
        Guid buildingId, Guid companyId, IReadOnlyList<PlanRowSpec> specs, LedgerPlanApplyMode mode, bool confirmReplace, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            return BadRequest("El modo no es válido.");
        }

        if (mode == LedgerPlanApplyMode.Replace)
        {
            var impact = await planService.GetImpactAsync(buildingId, cancellationToken);
            if (impact.HasImpact && !confirmReplace)
            {
                return BadRequest(
                    $"Reemplazar el plan desvincula el rubro de {impact.Expenses} gastos, {impact.Incomes} ingresos y {impact.RecurringExpenses} plantillas recurrentes, " +
                    $"y borra {impact.BudgetLines} renglones de presupuesto. Confirmá para continuar.");
            }

            return Ok(await planService.ReplaceAsync(buildingId, companyId, specs, cancellationToken));
        }

        var result = await planService.MergeAsync(buildingId, companyId, specs, updateExisting: mode == LedgerPlanApplyMode.Update, cancellationToken);
        var conflict = await SaveOrConflictAsync(cancellationToken);
        return conflict ?? Ok(result);
    }

    // ── Validaciones ──────────────────────────────────────────────────────────

    // Valida el pedido y resuelve el grupo padre. `current` es la cuenta que se edita (null al crear).
    private async Task<(ActionResult? Error, LedgerCategory? Parent)> ValidateAsync(
        LedgerCategoryUpsertRequest request, Guid buildingId, LedgerCategory? current, CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            return (BadRequest("El código es obligatorio."), null);
        }

        if (code.Length > MaxCodeLength)
        {
            return (BadRequest($"El código no puede superar los {MaxCodeLength} caracteres."), null);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return (BadRequest("El nombre es obligatorio."), null);
        }

        if (name.Length > MaxNameLength)
        {
            return (BadRequest($"El nombre no puede superar los {MaxNameLength} caracteres."), null);
        }

        if ((request.ExternalCode?.Trim().Length ?? 0) > MaxExternalCodeLength)
        {
            return (BadRequest($"El código del contador no puede superar los {MaxExternalCodeLength} caracteres."), null);
        }

        if (!Enum.IsDefined(request.Type))
        {
            return (BadRequest("El tipo no es válido."), null);
        }

        var all = await Db.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);
        var byId = all.ToDictionary(x => x.Id);

        LedgerCategory? parent = null;
        if (request.ParentId.HasValue)
        {
            if (current is not null && request.ParentId.Value == current.Id)
            {
                return (BadRequest("Una cuenta no puede ser su propio grupo."), null);
            }

            if (!byId.TryGetValue(request.ParentId.Value, out parent))
            {
                return (BadRequest("El grupo no existe en este edificio."), null);
            }

            // No se puede mover un grupo dentro de uno de sus propios descendientes.
            if (current is not null)
            {
                var walker = parent;
                while (walker is not null)
                {
                    if (walker.Id == current.Id)
                    {
                        return (BadRequest("No se puede mover un grupo dentro de uno de sus propios subgrupos."), null);
                    }

                    walker = walker.ParentId.HasValue && byId.TryGetValue(walker.ParentId.Value, out var up) ? up : null;
                }
            }

            // Profundidad: lo que cuelga del grupo + la propia cuenta (y lo que ya cuelga de ella, si se esta moviendo).
            var parentDepth = 1;
            var cursor = parent;
            while (cursor.ParentId.HasValue && byId.TryGetValue(cursor.ParentId.Value, out var next))
            {
                parentDepth++;
                cursor = next;
            }

            var height = current is null ? 1 : SubtreeHeight(all, current.Id);
            if (parentDepth + height > FinanceChartTemplate.MaxDepth)
            {
                return (BadRequest($"El plan admite hasta {FinanceChartTemplate.MaxDepth} niveles."), null);
            }

            if (!parent.IsActive && (current is null || current.ParentId != parent.Id))
            {
                return (BadRequest("El grupo está desactivado."), null);
            }

            // Una cuenta final con presupuesto, movimientos o funcion especial no puede pasar a ser un grupo.
            if (current is null || current.ParentId != parent.Id)
            {
                var parentHasOtherChildren = all.Any(x => x.ParentId == parent.Id && (current is null || x.Id != current.Id));
                if (!parentHasOtherChildren)
                {
                    if (parent.SystemKey is not null)
                    {
                        return (BadRequest("El grupo elegido es una cuenta con función especial: no puede tener subcuentas."), null);
                    }

                    if (await HasBudgetAsync(parent.Id, cancellationToken))
                    {
                        return (BadRequest("El grupo elegido tiene presupuesto cargado: pasalo a cero antes de agregarle subcuentas."), null);
                    }

                    if (await HasMovementsAsync(parent.Id, cancellationToken))
                    {
                        return (BadRequest("El grupo elegido ya tiene gastos o ingresos cargados: no puede pasar a ser un grupo. Elegí otro."), null);
                    }
                }
            }
        }

        var upperCode = code.ToUpperInvariant();
        if (all.Any(x => x.Code.ToUpperInvariant() == upperCode && (current is null || x.Id != current.Id)))
        {
            return (Conflict("Ya existe una cuenta con ese código en este edificio."), null);
        }

        return (null, parent);
    }

    private static int SubtreeHeight(IReadOnlyList<LedgerCategory> all, Guid id)
    {
        var children = all.Where(x => x.ParentId == id).ToList();
        return 1 + (children.Count == 0 ? 0 : children.Max(c => SubtreeHeight(all, c.Id)));
    }

    // Categoria de la liquidacion de una cuenta final de gastos o ingresos. Sin dato en el pedido: la que ya tenia o "Otro".
    // El aporte al fondo de reserva y el saldo acumulado / fondo operativo tienen un trato especial en el libro y no se eligen.
    private static (string? Error, BuildingExpenseCategory? Expense, BuildingIncomeCategory? Income) ResolveSettlementCategory(
        LedgerCategoryUpsertRequest request, LedgerCategoryType type, bool isLeaf,
        BuildingExpenseCategory? currentExpense, BuildingIncomeCategory? currentIncome)
    {
        if (!isLeaf)
        {
            return (null, null, null);
        }

        if (type == LedgerCategoryType.Expense)
        {
            var category = request.ExpenseCategory ?? currentExpense ?? BuildingExpenseCategory.Other;
            if (!Enum.IsDefined(category))
            {
                return ("La categoría de la liquidación no es válida.", null, null);
            }

            if (category == BuildingExpenseCategory.ReserveFund)
            {
                return ("El aporte al fondo de reserva ya tiene su propia cuenta de cobranza: elegí otra categoría para esta cuenta.", null, null);
            }

            return (null, category, null);
        }

        if (type == LedgerCategoryType.Income)
        {
            var category = request.IncomeCategory ?? currentIncome ?? BuildingIncomeCategory.Other;
            if (!Enum.IsDefined(category))
            {
                return ("La categoría de la liquidación no es válida.", null, null);
            }

            if (category is BuildingIncomeCategory.AccumulatedBalance or BuildingIncomeCategory.OperationalFund)
            {
                return ("El saldo acumulado y el fondo operativo no son ingresos nuevos: elegí otra categoría para esta cuenta.", null, null);
            }

            return (null, null, category);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Asigna, conserva o quita la funcion especial de una cuenta. Cada funcion va en una sola cuenta: si otra la tenia, se la quita (y se
    /// fija antes su categoria de liquidacion, para que no cambie). Las cuentas de cobranza no se pueden dejar sin funcion: se pasa la
    /// funcion a otra cuenta. Una cuenta con funcion de categoria cuenta siempre en esa categoria.
    /// </summary>
    private async Task<string?> ApplyRoleAsync(
        LedgerCategory entity, string? requested, bool isLeaf, bool hasChildren, bool hasMovements, CancellationToken cancellationToken)
    {
        if (requested is null)
        {
            return null;
        }

        var wanted = requested.Trim();
        if (wanted == (entity.SystemKey ?? string.Empty))
        {
            return null;
        }

        // Una cuenta de cobranza no se puede dejar sin esa funcion ni cambiarla por otra: se pasa la funcion a otra cuenta.
        if (entity.SystemKey?.StartsWith("Collection.", StringComparison.Ordinal) == true)
        {
            return "Las cuentas de cobranza de expensas no se pueden dejar sin función ni cambiar de función: asigná esa función a otra cuenta.";
        }

        if (wanted.Length == 0)
        {
            Freeze(entity);
            entity.SystemKey = null;
            return null;
        }

        var role = FinanceChartTemplate.FindRole(wanted);
        if (role is null)
        {
            return "La función elegida no existe.";
        }

        if (hasChildren || !isLeaf)
        {
            return "Solo una cuenta final (sin subcuentas y dentro de un grupo) puede tener función especial.";
        }

        if (role.Type != entity.Type)
        {
            return $"La función «{role.Label}» solo se puede asignar a una cuenta de {FinancePlanValidator.TypeLabel(role.Type).ToLowerInvariant()}.";
        }

        var newExpense = role.ExpenseCategory ?? entity.ExpenseCategory;
        var newIncome = role.IncomeCategory ?? entity.IncomeCategory;
        var isCollection = role.Key.StartsWith("Collection.", StringComparison.Ordinal);
        if (isCollection)
        {
            newExpense = null;
            newIncome = null;
        }

        if (hasMovements && (newExpense != FinanceChartTemplate.ExpenseCategoryOf(entity) || newIncome != FinanceChartTemplate.IncomeCategoryOf(entity)))
        {
            return "Esta cuenta ya tiene gastos o ingresos cargados: solo se le puede asignar una función de su misma categoría de liquidación.";
        }

        var holder = await Db.LedgerCategories.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.BuildingId == entity.BuildingId && x.SystemKey == role.Key && x.Id != entity.Id, cancellationToken);
        if (holder is not null)
        {
            Freeze(holder);
            holder.SystemKey = null;
            // El indice unico de la funcion exige liberarla antes de asignarla a la otra cuenta.
            await Db.SaveChangesAsync(cancellationToken);
        }

        Freeze(entity);
        entity.SystemKey = role.Key;
        entity.ExpenseCategory = newExpense;
        entity.IncomeCategory = newIncome;
        return null;
    }

    // Guarda la categoria de liquidacion deducida de la funcion, para que no cambie al quitarla.
    private static void Freeze(LedgerCategory category)
    {
        category.ExpenseCategory ??= FinanceChartTemplate.ExpenseCategoryOf(category);
        category.IncomeCategory ??= FinanceChartTemplate.IncomeCategoryOf(category);
    }

    private Task<bool> HasBudgetAsync(Guid categoryId, CancellationToken cancellationToken) =>
        Db.BudgetLines.AnyAsync(x => !x.IsDeleted && x.CategoryId == categoryId && x.Amount != 0m, cancellationToken);

    // "Con movimientos": ya tiene gastos o ingresos cargados o lo usa una plantilla de gasto recurrente.
    private async Task<bool> HasMovementsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await Db.BuildingExpenses.AnyAsync(x => !x.IsDeleted && x.LedgerCategoryId == categoryId, cancellationToken)
        || await Db.BuildingIncomes.AnyAsync(x => !x.IsDeleted && x.LedgerCategoryId == categoryId, cancellationToken)
        || await Db.RecurringBuildingExpenses.AnyAsync(x => !x.IsDeleted && x.LedgerCategoryId == categoryId, cancellationToken);

    // Cuentas del edificio que ya tienen gastos o ingresos cargados.
    private async Task<HashSet<Guid>> MovementCategoryIdsAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var fromExpenses = await Db.BuildingExpenses.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.LedgerCategoryId != null)
            .Select(x => x.LedgerCategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var fromIncomes = await Db.BuildingIncomes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.LedgerCategoryId != null)
            .Select(x => x.LedgerCategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var fromRecurring = await Db.RecurringBuildingExpenses.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.LedgerCategoryId != null)
            .Select(x => x.LedgerCategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        return fromExpenses.Concat(fromIncomes).Concat(fromRecurring).ToHashSet();
    }

    private static string? NormalizeExternalCode(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static LedgerPlanImportRowDto ToRowDto(PlanRowResult r) => new()
    {
        RowNumber = r.RowNumber,
        Code = r.Code,
        Name = r.Name,
        ParentCode = r.ParentCode,
        Type = r.Type,
        Level = r.Level,
        IsLeaf = r.IsLeaf,
        ExternalCode = r.ExternalCode,
        IsActive = r.IsActive,
        SystemKey = r.SystemKey,
        ExpenseCategory = r.ExpenseCategory,
        IncomeCategory = r.IncomeCategory,
        CategorySuggested = r.CategorySuggested,
        Errors = r.Errors,
        Warnings = r.Warnings
    };

    private static LedgerCategoryDto ToDto(LedgerCategory x, bool hasChildren, bool hasMovements)
    {
        var isLeaf = x.ParentId.HasValue && !hasChildren;
        return new LedgerCategoryDto
        {
            Id = x.Id,
            BuildingId = x.BuildingId,
            ParentId = x.ParentId,
            Code = x.Code,
            Name = x.Name,
            Type = x.Type,
            ExternalCode = x.ExternalCode,
            SystemKey = x.SystemKey,
            IsActive = x.IsActive,
            IsTemplate = x.SystemKey is not null,
            HasChildren = hasChildren,
            ExpenseCategory = isLeaf ? FinanceChartTemplate.ExpenseCategoryOf(x) : null,
            IncomeCategory = isLeaf ? FinanceChartTemplate.IncomeCategoryOf(x) : null,
            HasMovements = hasMovements
        };
    }
}
