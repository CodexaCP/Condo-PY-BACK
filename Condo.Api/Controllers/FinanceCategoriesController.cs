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
/// Plan de cuentas del edificio: arbol de dos niveles (rubro y subrubro) con codigos editables. Nace de la plantilla
/// estandar al habilitar el modulo; los rubros de la plantilla se pueden renombrar, recodificar y desactivar, pero no
/// mover ni eliminar. Los gastos e ingresos pueden elegir un subrubro; el libro derivado usa ese rubro y, si no eligieron
/// ninguno, mapea su categoria con la clave de la plantilla. Solo el SuperAdmin modifica el plan (servicio de configuracion).
/// </summary>
[Route("api/finance/categories")]
public class FinanceCategoriesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinancePlanCopier copier) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    private const int MaxCodeLength = 30;
    private const int MaxNameLength = 200;
    private const int MaxExternalCodeLength = 50;

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
            // Rubro de la plantilla: su lugar en el arbol y su tipo no cambian.
            if (request.ParentId != entity.ParentId || (parent is null && request.Type != entity.Type))
            {
                return BadRequest("Los rubros de la plantilla no se pueden mover ni cambiar de tipo. Podés renombrarlos, cambiarles el código o desactivarlos.");
            }
        }
        else
        {
            if (hasChildren && (request.ParentId.HasValue || request.Type != entity.Type))
            {
                return BadRequest("Este rubro tiene subrubros: no se le puede asignar un rubro padre ni cambiar su tipo.");
            }

            var newType = parent?.Type ?? request.Type;
            if (newType != entity.Type && await HasBudgetAsync(entity.Id, cancellationToken))
            {
                return BadRequest("Este rubro tiene presupuesto cargado: no se le puede cambiar el tipo.");
            }

            if (hasMovements && (newType != entity.Type || parent is null))
            {
                return BadRequest("Este rubro ya tiene gastos o ingresos cargados: no se le puede cambiar el tipo ni pasarlo a rubro principal. Si ya no lo usás, desactivalo.");
            }

            // Sin dato en el pedido se conserva la categoria que ya tenia.
            var currentExpense = FinanceChartTemplate.ExpenseCategoryOf(entity);
            var currentIncome = FinanceChartTemplate.IncomeCategoryOf(entity);
            var (categoryError, expenseCategory, incomeCategory) = ResolveSettlementCategory(request, newType, parent is not null, currentExpense, currentIncome);
            if (categoryError is not null)
            {
                return BadRequest(categoryError);
            }

            if (hasMovements && (expenseCategory != currentExpense || incomeCategory != currentIncome))
            {
                return BadRequest("Este rubro ya tiene gastos o ingresos cargados: no se le puede cambiar la categoría de la liquidación, porque esos movimientos ya cuentan con la actual.");
            }

            entity.ParentId = parent?.Id;
            entity.Type = newType;
            entity.ExpenseCategory = expenseCategory;
            entity.IncomeCategory = incomeCategory;
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
            return BadRequest("Los rubros de la plantilla no se eliminan: si no los usás, desactivalos.");
        }

        if (await Db.LedgerCategories.AnyAsync(x => !x.IsDeleted && x.ParentId == entity.Id, cancellationToken))
        {
            return BadRequest("El rubro tiene subrubros. Eliminá o movés primero sus subrubros.");
        }

        // Un rubro con gastos o ingresos cargados, o con presupuesto, solo se puede desactivar.
        if (await HasMovementsAsync(entity.Id, cancellationToken))
        {
            return BadRequest("El rubro tiene gastos o ingresos cargados: no se puede eliminar. Si ya no lo usás, desactivalo.");
        }

        if (await HasBudgetAsync(entity.Id, cancellationToken))
        {
            return BadRequest("El rubro tiene presupuesto cargado: pasalo a cero o desactivalo.");
        }

        entity.IsDeleted = true;
        await Db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Copia el plan de cuentas de otro edificio (nombres, codigos del contador, rubros activos y rubros propios) sin tocar
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

    // Valida el pedido y resuelve el rubro padre. `current` es el rubro que se edita (null al crear).
    private async Task<(ActionResult? Error, LedgerCategory? Parent)> ValidateAsync(
        LedgerCategoryUpsertRequest request, Guid buildingId, LedgerCategory? current, CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            return (BadRequest("El código del rubro es obligatorio."), null);
        }

        if (code.Length > MaxCodeLength)
        {
            return (BadRequest($"El código no puede superar los {MaxCodeLength} caracteres."), null);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return (BadRequest("El nombre del rubro es obligatorio."), null);
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
            return (BadRequest("El tipo de rubro no es válido."), null);
        }

        LedgerCategory? parent = null;
        if (request.ParentId.HasValue)
        {
            if (current is not null && request.ParentId.Value == current.Id)
            {
                return (BadRequest("Un rubro no puede ser su propio padre."), null);
            }

            parent = await Db.LedgerCategories.AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ParentId.Value && x.BuildingId == buildingId, cancellationToken);
            if (parent is null)
            {
                return (BadRequest("El rubro padre no existe en este edificio."), null);
            }

            if (parent.ParentId.HasValue)
            {
                return (BadRequest("El plan de cuentas tiene dos niveles: el rubro padre no puede ser a su vez un subrubro."), null);
            }

            if (!parent.IsActive && (current is null || current.ParentId != parent.Id))
            {
                return (BadRequest("El rubro padre está desactivado."), null);
            }

            // Un rubro con presupuesto no puede pasar a ser un grupo: el presupuesto se carga en los subrubros.
            if ((current is null || current.ParentId != parent.Id) && await HasBudgetAsync(parent.Id, cancellationToken))
            {
                return (BadRequest("El rubro padre tiene presupuesto cargado: pasalo a cero antes de agregarle subrubros."), null);
            }
        }

        var upperCode = code.ToUpper();
        var duplicated = await Db.LedgerCategories.AsNoTracking().AnyAsync(
            x => !x.IsDeleted && x.BuildingId == buildingId && x.Code.ToUpper() == upperCode
                 && (current == null || x.Id != current.Id),
            cancellationToken);
        if (duplicated)
        {
            return (Conflict("Ya existe un rubro con ese código en este edificio."), null);
        }

        return (null, parent);
    }

    // Categoria de la liquidacion de un subrubro propio de gastos o ingresos. Sin dato en el pedido: la que ya tenia o "Otro".
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
                return ("El aporte al fondo de reserva ya tiene su propio rubro en la plantilla: elegí otra categoría para este rubro.", null, null);
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
                return ("El saldo acumulado y el fondo operativo no son ingresos nuevos: elegí otra categoría para este rubro.", null, null);
            }

            return (null, null, category);
        }

        return (null, null, null);
    }

    private Task<bool> HasBudgetAsync(Guid categoryId, CancellationToken cancellationToken) =>
        Db.BudgetLines.AnyAsync(x => !x.IsDeleted && x.CategoryId == categoryId && x.Amount != 0m, cancellationToken);

    private async Task<bool> HasMovementsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await Db.BuildingExpenses.AnyAsync(x => !x.IsDeleted && x.LedgerCategoryId == categoryId, cancellationToken)
        || await Db.BuildingIncomes.AnyAsync(x => !x.IsDeleted && x.LedgerCategoryId == categoryId, cancellationToken);

    // Rubros del edificio que ya tienen gastos o ingresos cargados.
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
        return fromExpenses.Concat(fromIncomes).ToHashSet();
    }

    private static string? NormalizeExternalCode(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

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
