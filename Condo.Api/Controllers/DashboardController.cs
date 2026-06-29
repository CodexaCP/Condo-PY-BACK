using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var buildingsQuery = dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        var unitsQuery = dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        var residentsQuery = dbContext.Residents
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        var assignmentsQuery = dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && (x.EndDate == null || x.EndDate >= today));

        var expensePeriodsQuery = dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        var expenseChargesQuery = dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        var paymentsQuery = dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsReversed);


        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                var companyId = accessScope.CompanyId.Value;
                buildingsQuery = buildingsQuery.Where(x => x.CompanyId == companyId);
                unitsQuery = unitsQuery.Where(x => x.CompanyId == companyId);
                residentsQuery = residentsQuery.Where(x => x.CompanyId == companyId);
                assignmentsQuery = assignmentsQuery.Where(x => x.CompanyId == companyId);
                expensePeriodsQuery = expensePeriodsQuery.Where(x => x.CompanyId == companyId);
                expenseChargesQuery = expenseChargesQuery.Where(x => x.CompanyId == companyId);
                paymentsQuery = paymentsQuery.Where(x => x.CompanyId == companyId);
            }
            else
            {
                buildingsQuery = buildingsQuery.Where(x => accessibleBuildingIds.Contains(x.Id));
                unitsQuery = unitsQuery.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
                assignmentsQuery = assignmentsQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
                expensePeriodsQuery = expensePeriodsQuery.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
                expenseChargesQuery = expenseChargesQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
                paymentsQuery = paymentsQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
                residentsQuery = residentsQuery.Where(x =>
                    x.UnitResidents.Any(link =>
                        !link.IsDeleted &&
                        (link.EndDate == null || link.EndDate >= today) &&
                        accessibleBuildingIds.Contains(link.Unit!.BuildingId)));
            }
        }

        var totalBuildings = await buildingsQuery.CountAsync(cancellationToken);
        var activeBuildings = await buildingsQuery.CountAsync(x => x.IsActive, cancellationToken);
        var totalUnits = await unitsQuery.CountAsync(cancellationToken);
        var activeUnits = await unitsQuery.CountAsync(x => x.IsActive, cancellationToken);
        var totalResidents = await residentsQuery.CountAsync(cancellationToken);
        var activeResidents = await residentsQuery.CountAsync(x => x.IsActive, cancellationToken);
        var activeAssignments = await assignmentsQuery.CountAsync(cancellationToken);
        var occupiedUnits = await assignmentsQuery.Select(x => x.UnitId).Distinct().CountAsync(cancellationToken);

        var unitOwnersQuery = dbContext.UnitOwners.AsNoTracking().Where(x => !x.IsDeleted);
        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
                unitOwnersQuery = unitOwnersQuery.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            else
                unitOwnersQuery = unitOwnersQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
        }
        var unitsWithOwners = await unitOwnersQuery.Select(x => x.UnitId).Distinct().CountAsync(cancellationToken);
        var unitsWithoutPrimaryResident = await unitsQuery
            .Where(x => x.IsActive)
            .CountAsync(unit =>
                !dbContext.UnitResidents.Any(link =>
                    !link.IsDeleted &&
                    link.UnitId == unit.Id &&
                    link.IsPrimary &&
                    (link.EndDate == null || link.EndDate >= today)), cancellationToken);
        var totalExpensePeriods = await expensePeriodsQuery.CountAsync(cancellationToken);
        var draftExpensePeriods = await expensePeriodsQuery.CountAsync(x => x.Status == Condo.Domain.Enums.ExpensePeriodStatus.Draft, cancellationToken);
        // Solo cargos originales (lo que salió en la liquidación, sin reversiones)
        var totalChargedAmount = await expenseChargesQuery
            .Where(x => !x.IsReversal)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        // Neto real (incluye los ajustes negativos de reversiones de cargos)
        var netChargedAmount = await expenseChargesQuery.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var totalCollectedAmount = await paymentsQuery.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var pendingBalanceAmount = netChargedAmount - totalCollectedAmount;

        var chargeSnapshots = await expenseChargesQuery
            .Select(x => new
            {
                x.UnitId,
                x.ExpensePeriodId,
                x.Amount,
                DueDate = x.ExpensePeriod != null ? x.ExpensePeriod.DueDate : (DateOnly?)null
            })
            .ToListAsync(cancellationToken);

        var paymentSnapshots = await paymentsQuery
            .Select(x => new
            {
                x.UnitId,
                x.ExpensePeriodId,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        var chargeTotals = chargeSnapshots
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId, x.DueDate })
            .Select(group => new
            {
                group.Key.UnitId,
                group.Key.ExpensePeriodId,
                group.Key.DueDate,
                TotalCharges = group.Sum(x => x.Amount)
            })
            .ToList();

        var paymentTotals = paymentSnapshots
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId })
            .ToDictionary(
                group => (group.Key.UnitId, group.Key.ExpensePeriodId),
                group => group.Sum(x => x.Amount));

        var openBalances = chargeTotals
            .Select(item =>
            {
                var paid = paymentTotals.GetValueOrDefault((item.UnitId, item.ExpensePeriodId), 0m);
                var balance = item.TotalCharges - paid;
                return new
                {
                    item.UnitId,
                    item.DueDate,
                    Balance = balance
                };
            })
            .Where(x => x.Balance > 0m)
            .ToList();

        var unitsWithOutstandingBalance = openBalances
            .Select(x => x.UnitId)
            .Distinct()
            .Count();

        var overdueBalanceAmount = openBalances
            .Where(x => x.DueDate.HasValue && x.DueDate.Value < today)
            .Sum(x => x.Balance);

        var collectionRatePercentage = netChargedAmount <= 0m
            ? 0m
            : Math.Round((totalCollectedAmount / netChargedAmount) * 100m, 2);

        // Reversiones de cargos: Amount es negativo, lo invertimos para mostrar positivo en el dashboard
        var totalReversedAmount = -(await expenseChargesQuery
            .Where(x => x.IsReversal)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m);
        var totalReversedPayments = await expenseChargesQuery
            .Where(x => x.IsReversal)
            .CountAsync(cancellationToken);

        return Ok(new DashboardSummaryDto
        {
            TotalBuildings = totalBuildings,
            ActiveBuildings = activeBuildings,
            TotalUnits = totalUnits,
            ActiveUnits = activeUnits,
            TotalResidents = totalResidents,
            ActiveResidents = activeResidents,
            ActiveAssignments = activeAssignments,
            UnitsWithOwners = unitsWithOwners,
            OccupiedUnits = occupiedUnits,
            UnitsWithoutPrimaryResident = unitsWithoutPrimaryResident,
            TotalExpensePeriods = totalExpensePeriods,
            DraftExpensePeriods = draftExpensePeriods,
            UnitsWithOutstandingBalance = unitsWithOutstandingBalance,
            TotalChargedAmount = totalChargedAmount,
            TotalCollectedAmount = totalCollectedAmount,
            PendingBalanceAmount = pendingBalanceAmount,
            OverdueBalanceAmount = overdueBalanceAmount,
            CollectionRatePercentage = collectionRatePercentage,
            TotalReversedAmount = totalReversedAmount,
            TotalReversedPayments = totalReversedPayments
        });
    }
}
