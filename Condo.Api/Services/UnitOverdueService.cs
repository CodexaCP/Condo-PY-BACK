using Condo.Application.Abstractions;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Calcula si una unidad tiene saldo vencido (mora), usando el mismo criterio que
/// MorosityController: cargos de un periodo cuyo DueDate ya pasó y el periodo no está
/// en Draft, netos de los pagos registrados contra ese mismo periodo.
/// </summary>
public interface IUnitOverdueService
{
    Task<bool> IsUnitOverdueAsync(Guid unitId, CancellationToken ct);
    Task<HashSet<Guid>> GetOverdueUnitIdsByBuildingAsync(IReadOnlyCollection<Guid> buildingIds, CancellationToken ct);
}

public class UnitOverdueService(ICondoDbContext dbContext) : IUnitOverdueService
{
    public async Task<bool> IsUnitOverdueAsync(Guid unitId, CancellationToken ct)
    {
        var balances = await GetOverdueBalancesAsync(x => x.UnitId == unitId, ct);
        return balances.Values.Any(v => v > 0m);
    }

    public async Task<HashSet<Guid>> GetOverdueUnitIdsByBuildingAsync(IReadOnlyCollection<Guid> buildingIds, CancellationToken ct)
    {
        if (buildingIds.Count == 0) return [];

        var balances = await GetOverdueBalancesAsync(x => x.Unit != null && buildingIds.Contains(x.Unit.BuildingId), ct);
        return balances.Where(kv => kv.Value > 0m).Select(kv => kv.Key).ToHashSet();
    }

    private async Task<Dictionary<Guid, decimal>> GetOverdueBalancesAsync(
        System.Linq.Expressions.Expression<Func<Condo.Domain.Entities.ExpenseCharge, bool>> unitFilter,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var charges = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriod != null
                     && x.ExpensePeriod.Status != ExpensePeriodStatus.Draft
                     && x.ExpensePeriod.DueDate < today)
            .Where(unitFilter)
            .Select(x => new { x.UnitId, x.ExpensePeriodId, x.Amount })
            .ToListAsync(ct);

        if (charges.Count == 0) return new Dictionary<Guid, decimal>();

        var periodIds = charges.Select(x => x.ExpensePeriodId).Distinct().ToList();
        var unitIds = charges.Select(x => x.UnitId).Distinct().ToList();

        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && periodIds.Contains(x.ExpensePeriodId) && unitIds.Contains(x.UnitId))
            .Select(x => new { x.UnitId, x.ExpensePeriodId, x.Amount })
            .ToListAsync(ct);

        var paymentTotals = payments
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId })
            .ToDictionary(g => (g.Key.UnitId, g.Key.ExpensePeriodId), g => g.Sum(x => x.Amount));

        var balanceByUnit = new Dictionary<Guid, decimal>();
        foreach (var group in charges.GroupBy(x => new { x.UnitId, x.ExpensePeriodId }))
        {
            var totalCharged = group.Sum(x => x.Amount);
            var totalPaid = paymentTotals.GetValueOrDefault((group.Key.UnitId, group.Key.ExpensePeriodId), 0m);
            var pending = Math.Max(0m, totalCharged - totalPaid);
            if (pending <= 0m) continue;

            balanceByUnit[group.Key.UnitId] = balanceByUnit.GetValueOrDefault(group.Key.UnitId, 0m) + pending;
        }

        return balanceByUnit;
    }
}
