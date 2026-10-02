using Condo.Application.Abstractions;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Valida el rubro (plan de cuentas de Finanzas del edificio) que se elige al cargar un gasto o un ingreso y devuelve la categoria
/// con la que cuenta en la liquidacion. La liquidacion sigue agrupando por categoria: el rubro la fija, asi no hay dos
/// clasificaciones que se contradigan.
/// </summary>
public class MovementRubroResolver(ICondoDbContext dbContext, FinanceModuleGate gate)
{
    public sealed record Result(string? Error, LedgerCategory? Rubro, BuildingExpenseCategory? ExpenseCategory, BuildingIncomeCategory? IncomeCategory);

    private static readonly Result None = new(null, null, null, null);

    /// <param name="currentRubroId">Rubro que el movimiento ya tiene (al editar): conservarlo no exige que siga activo ni el modulo.</param>
    public async Task<Result> ResolveAsync(
        Guid? rubroId, Guid buildingId, LedgerCategoryType type, Guid? currentRubroId, CancellationToken cancellationToken)
    {
        if (!rubroId.HasValue)
        {
            return None;
        }

        var rubro = await dbContext.LedgerCategories.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == rubroId.Value && x.BuildingId == buildingId, cancellationToken);
        if (rubro is null)
        {
            return Fail("El rubro no existe en este edificio.");
        }

        var noun = type == LedgerCategoryType.Expense ? "gastos" : "ingresos";
        if (rubro.Type != type)
        {
            return Fail($"El rubro elegido no es de {noun}.");
        }

        if (!rubro.ParentId.HasValue || await dbContext.LedgerCategories.AnyAsync(x => !x.IsDeleted && x.ParentId == rubro.Id, cancellationToken))
        {
            return Fail("Elegí un subrubro: los rubros principales solo agrupan.");
        }

        if (!FinanceChartTemplate.CanReceiveMovements(rubro))
        {
            return Fail($"El rubro elegido no recibe {noun} cargados a mano.");
        }

        if (currentRubroId != rubro.Id)
        {
            if (!rubro.IsActive)
            {
                return Fail("El rubro elegido está desactivado.");
            }

            if (!(await gate.GetStateAsync(buildingId, cancellationToken)).IsAvailable)
            {
                return Fail("El módulo Finanzas del edificio no está disponible para este edificio.");
            }
        }

        return new Result(
            null,
            rubro,
            FinanceChartTemplate.ExpenseCategoryOf(rubro),
            FinanceChartTemplate.IncomeCategoryOf(rubro));
    }

    /// <summary>Subrubros activos de gastos o ingresos de un edificio y sus rubros principales (para mostrar el grupo).</summary>
    public sealed record AssignableSet(List<LedgerCategory> Rubros, Dictionary<Guid, LedgerCategory> ById)
    {
        public string GroupOf(LedgerCategory rubro) =>
            rubro.ParentId.HasValue && ById.TryGetValue(rubro.ParentId.Value, out var group) ? group.Name : string.Empty;
    }

    /// <summary>
    /// Subrubros activos de gastos o ingresos del edificio, para resolver por codigo o nombre (importacion) o armar la plantilla.
    /// Nulo si Finanzas del edificio no esta disponible en ese edificio: entonces no hay rubros que ofrecer.
    /// </summary>
    public async Task<AssignableSet?> AssignableAsync(Guid buildingId, LedgerCategoryType type, CancellationToken cancellationToken)
    {
        if (!(await gate.GetStateAsync(buildingId, cancellationToken)).IsAvailable)
        {
            return null;
        }

        var all = await dbContext.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);
        return new AssignableSet(FinanceChartTemplate.AssignableCategories(all, type), all.ToDictionary(x => x.Id));
    }

    private static Result Fail(string error) => new(error, null, null, null);
}
