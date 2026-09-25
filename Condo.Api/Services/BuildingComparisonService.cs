using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

public interface IBuildingComparisonService
{
    Task<BuildingComparisonReportDto> BuildReportAsync(IReadOnlyCollection<Guid> buildingIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}

public class BuildingComparisonService(ICondoDbContext dbContext) : IBuildingComparisonService
{
    public async Task<BuildingComparisonReportDto> BuildReportAsync(IReadOnlyCollection<Guid> buildingIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        if (buildingIds.Count == 0)
        {
            return new BuildingComparisonReportDto { FromDate = fromDate, ToDate = toDate, Items = [] };
        }

        var buildings = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var collectedByBuilding = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsReversed && buildingIds.Contains(x.Unit!.BuildingId)
                     && x.PaymentDate >= fromDate && x.PaymentDate <= toDate)
            .GroupBy(x => x.Unit!.BuildingId)
            .Select(g => new { BuildingId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var expensesByBuilding = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.BuildingId)
                     && x.ExpenseDate >= fromDate && x.ExpenseDate <= toDate)
            .GroupBy(x => x.BuildingId)
            .Select(g => new { BuildingId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        // Morosidad: saldo pendiente vencido a hoy, independiente del rango de fechas elegido
        // (es un estado actual, no algo que ocurrio "dentro" del periodo).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var chargeSnapshots = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.Unit!.BuildingId))
            .Select(x => new
            {
                x.UnitId,
                x.ExpensePeriodId,
                x.Amount,
                BuildingId = x.Unit!.BuildingId,
                DueDate = x.ExpensePeriod != null ? x.ExpensePeriod.DueDate : (DateOnly?)null
            })
            .ToListAsync(cancellationToken);

        var paymentSnapshots = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsReversed && buildingIds.Contains(x.Unit!.BuildingId))
            .Select(x => new { x.UnitId, x.ExpensePeriodId, x.Amount })
            .ToListAsync(cancellationToken);

        var paymentTotals = paymentSnapshots
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId })
            .ToDictionary(g => (g.Key.UnitId, g.Key.ExpensePeriodId), g => g.Sum(x => x.Amount));

        var overdueByBuilding = chargeSnapshots
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId, x.BuildingId, x.DueDate })
            .Select(g =>
            {
                var charged = g.Sum(x => x.Amount);
                var paid = paymentTotals.GetValueOrDefault((g.Key.UnitId, g.Key.ExpensePeriodId), 0m);
                return new { g.Key.BuildingId, g.Key.UnitId, g.Key.DueDate, Balance = charged - paid };
            })
            .Where(x => x.Balance > 0m && x.DueDate.HasValue && x.DueDate.Value < today)
            .GroupBy(x => x.BuildingId)
            .ToDictionary(g => g.Key, g => new { Amount = g.Sum(x => x.Balance), Units = g.Select(x => x.UnitId).Distinct().Count() });

        var collectedMap = collectedByBuilding.ToDictionary(x => x.BuildingId, x => x.Amount);
        var expenseMap = expensesByBuilding.ToDictionary(x => x.BuildingId, x => x.Amount);

        var items = buildings
            .Select(b =>
            {
                var collected = collectedMap.GetValueOrDefault(b.Id, 0m);
                var expenses = expenseMap.GetValueOrDefault(b.Id, 0m);
                var overdue = overdueByBuilding.GetValueOrDefault(b.Id);
                return new BuildingComparisonItemDto
                {
                    BuildingId = b.Id,
                    BuildingName = b.Name,
                    TotalCollected = collected,
                    TotalExpenses = expenses,
                    NetResult = collected - expenses,
                    OverdueAmount = overdue?.Amount ?? 0m,
                    UnitsWithOverdueBalance = overdue?.Units ?? 0
                };
            })
            .OrderByDescending(x => x.OverdueAmount)
            .ThenBy(x => x.BuildingName)
            .ToList();

        return new BuildingComparisonReportDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            Items = items
        };
    }
}
