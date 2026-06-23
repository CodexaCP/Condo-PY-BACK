using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/morosity")]
public class MorosityController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MorosityReportDto>> GetReport(
        [FromQuery] Guid? buildingId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var buildingsQuery = dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        var chargesQuery = dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriod != null && x.ExpensePeriod.Status != ExpensePeriodStatus.Draft);

        var paymentsQuery = dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriod != null && x.ExpensePeriod.Status != ExpensePeriodStatus.Draft);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                var companyId = accessScope.CompanyId.Value;
                buildingsQuery = buildingsQuery.Where(x => x.CompanyId == companyId);
                chargesQuery = chargesQuery.Where(x => x.CompanyId == companyId);
                paymentsQuery = paymentsQuery.Where(x => x.CompanyId == companyId);
            }
            else
            {
                buildingsQuery = buildingsQuery.Where(x => accessibleBuildingIds.Contains(x.Id));
                chargesQuery = chargesQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
                paymentsQuery = paymentsQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
            }
        }

        if (buildingId.HasValue)
        {
            var canAccess = await buildingsQuery.AnyAsync(x => x.Id == buildingId.Value, cancellationToken);
            if (!canAccess)
            {
                return Forbid();
            }

            chargesQuery = chargesQuery.Where(x => x.Unit!.BuildingId == buildingId.Value);
            paymentsQuery = paymentsQuery.Where(x => x.Unit!.BuildingId == buildingId.Value);
        }

        var chargeSnapshots = await chargesQuery
            .Where(x => x.ExpensePeriod != null && x.ExpensePeriod.DueDate < today)
            .Select(x => new
            {
                x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
                BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
                x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                DueDate = x.ExpensePeriod != null ? x.ExpensePeriod.DueDate : today,
                x.ChargeType,
                x.Amount
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

        var paymentTotals = paymentSnapshots
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId })
            .ToDictionary(
                group => (group.Key.UnitId, group.Key.ExpensePeriodId),
                group => group.Sum(x => x.Amount));

        var unitIds = chargeSnapshots.Select(x => x.UnitId).Distinct().ToList();
        var assignments = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && unitIds.Contains(x.UnitId))
            .Select(x => new
            {
                x.UnitId,
                x.IsPrimary,
                x.StartDate,
                x.EndDate,
                ResidentName = x.Resident != null ? x.Resident.FullName : string.Empty
            })
            .ToListAsync(cancellationToken);

        var allItems = chargeSnapshots
            .GroupBy(x => new
            {
                x.UnitId,
                x.UnitCode,
                x.BuildingId,
                x.BuildingName,
                x.ExpensePeriodId,
                x.ExpensePeriodName,
                x.DueDate
            })
            .Select(group =>
            {
                var totalCharges = group.Sum(x => x.Amount);
                var totalPayments = paymentTotals.GetValueOrDefault((group.Key.UnitId, group.Key.ExpensePeriodId), 0m);
                var breakdown = AllocatePaymentsByType(
                    group.GroupBy(x => x.ChargeType).ToDictionary(x => x.Key, x => x.Sum(v => v.Amount)),
                    totalPayments);
                var balance = breakdown.TotalPendingAmount;
                var assignedResident = assignments
                    .Where(x =>
                        x.UnitId == group.Key.UnitId &&
                        x.StartDate <= group.Key.DueDate &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= group.Key.DueDate))
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenByDescending(x => x.StartDate)
                    .FirstOrDefault();

                return new MorosityItemDto
                {
                    UnitId = group.Key.UnitId,
                    UnitCode = group.Key.UnitCode,
                    BuildingId = group.Key.BuildingId,
                    BuildingName = group.Key.BuildingName,
                    ExpensePeriodId = group.Key.ExpensePeriodId,
                    ExpensePeriodName = group.Key.ExpensePeriodName,
                    DueDate = group.Key.DueDate,
                    DaysOverdue = today.DayNumber - group.Key.DueDate.DayNumber,
                    TotalCharges = totalCharges,
                    TotalPayments = totalPayments,
                    Balance = balance,
                    OrdinaryBalance = breakdown.GetPending(ExpenseChargeType.Ordinary),
                    ReserveFundBalance = breakdown.GetPending(ExpenseChargeType.ReserveFund),
                    ExtraordinaryBalance = breakdown.GetPending(ExpenseChargeType.Extraordinary),
                    IndividualBalance = breakdown.GetPending(ExpenseChargeType.Individual),
                    AdjustmentBalance = breakdown.GetPending(ExpenseChargeType.Adjustment),
                    CreditBalanceAmount = breakdown.CreditBalanceAmount,
                    IsOccupied = assignedResident is not null,
                    ResponsibleType = assignedResident is not null ? "ResidentAssigned" : "OwnerAdministration",
                    ResponsibleName = assignedResident?.ResidentName ?? "Propietario / administracion"
                };
            })
            .ToList();

        var items = allItems
            .Where(x => x.Balance > 0m)
            .OrderByDescending(x => x.Balance)
            .ThenByDescending(x => x.DaysOverdue)
            .ThenBy(x => x.BuildingName)
            .ThenBy(x => x.UnitCode)
            .ToList();

        return Ok(new MorosityReportDto
        {
            Summary = new MorositySummaryDto
            {
                TotalUnitsInArrears = items.Select(x => x.UnitId).Distinct().Count(),
                TotalOverduePeriods = items.Count,
                TotalOverdueAmount = items.Sum(x => x.Balance),
                OrdinaryOverdueAmount = items.Sum(x => x.OrdinaryBalance),
                ReserveFundOverdueAmount = items.Sum(x => x.ReserveFundBalance),
                ExtraordinaryOverdueAmount = items.Sum(x => x.ExtraordinaryBalance),
                IndividualOverdueAmount = items.Sum(x => x.IndividualBalance),
                AdjustmentOverdueAmount = items.Sum(x => x.AdjustmentBalance),
                TotalCreditBalanceAmount = allItems.Sum(x => x.CreditBalanceAmount),
                OccupiedUnitsInArrears = items.Where(x => x.IsOccupied).Select(x => x.UnitId).Distinct().Count(),
                VacantUnitsInArrears = items.Where(x => !x.IsOccupied).Select(x => x.UnitId).Distinct().Count(),
                OccupiedOverdueAmount = items.Where(x => x.IsOccupied).Sum(x => x.Balance),
                VacantOverdueAmount = items.Where(x => !x.IsOccupied).Sum(x => x.Balance)
            },
            Items = items
        });
    }

    private static AllocationResult AllocatePaymentsByType(
        IReadOnlyDictionary<ExpenseChargeType, decimal> chargedByType,
        decimal totalPayments)
    {
        var totalCharged = chargedByType.Values.Sum();
        var pending = new Dictionary<ExpenseChargeType, decimal>();

        if (totalCharged <= 0m)
        {
            return new AllocationResult(pending, decimal.Max(totalPayments, 0m), 0m);
        }

        var appliedPayments = decimal.Min(totalPayments, totalCharged);
        decimal remainingPending = decimal.Round(totalCharged - appliedPayments, 2, MidpointRounding.AwayFromZero);
        var orderedTypes = chargedByType.Keys.OrderBy(x => x.ToString()).ToList();

        for (var index = 0; index < orderedTypes.Count; index++)
        {
            var chargeType = orderedTypes[index];
            var chargedAmount = chargedByType[chargeType];
            var pendingAmount = index == orderedTypes.Count - 1
                ? remainingPending
                : decimal.Round(chargedAmount - (appliedPayments * (chargedAmount / totalCharged)), 2, MidpointRounding.AwayFromZero);

            remainingPending -= pendingAmount;
            pending[chargeType] = pendingAmount;
        }

        return new AllocationResult(
            pending,
            decimal.Max(totalPayments - totalCharged, 0m),
            pending.Values.Sum());
    }

    private sealed record AllocationResult(
        IReadOnlyDictionary<ExpenseChargeType, decimal> PendingByType,
        decimal CreditBalanceAmount,
        decimal TotalPendingAmount)
    {
        public decimal GetPending(ExpenseChargeType chargeType) => PendingByType.GetValueOrDefault(chargeType, 0m);
    }
}
