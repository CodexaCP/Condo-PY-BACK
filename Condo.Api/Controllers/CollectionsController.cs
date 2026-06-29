using Condo.Api.Documents;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/collections")]
public class CollectionsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CollectionReportDto>> GetReport(
        [FromQuery] Guid? buildingId,
        [FromQuery] int? year,
        [FromQuery] int? fromMonth,
        [FromQuery] int? toMonth,
        CancellationToken cancellationToken)
    {
        var report = await BuildReportAsync(buildingId, year, fromMonth, toMonth, cancellationToken);
        if (report is null) return Forbid();
        return Ok(report);
    }

    [HttpGet("report-pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadReportPdf(
        [FromQuery(Name = "access_token")] string? _,
        [FromQuery] Guid? buildingId,
        [FromQuery] int? year,
        [FromQuery] int? fromMonth,
        [FromQuery] int? toMonth,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var report = await BuildReportAsync(buildingId, year, fromMonth, toMonth, cancellationToken);
        if (report is null) return Forbid();

        var filterDescription = BuildFilterDescription(buildingId, year, fromMonth, toMonth);
        var document = new CollectionReportPdfDocument(report, filterDescription);
        var bytes = document.GeneratePdf();

        var filename = $"cobranza_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    private async Task<CollectionReportDto?> BuildReportAsync(
        Guid? buildingId,
        int? year,
        int? fromMonth,
        int? toMonth,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var periodsQuery = dbContext.ExpensePeriods
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
                periodsQuery = periodsQuery.Where(x => x.CompanyId == companyId);
                chargesQuery = chargesQuery.Where(x => x.CompanyId == companyId);
                paymentsQuery = paymentsQuery.Where(x => x.CompanyId == companyId);
            }
            else
            {
                periodsQuery = periodsQuery.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
                chargesQuery = chargesQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
                paymentsQuery = paymentsQuery.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
            }
        }

        if (buildingId.HasValue)
        {
            var canAccess = await periodsQuery.AnyAsync(x => x.BuildingId == buildingId.Value, cancellationToken);
            if (!canAccess)
                return null;

            periodsQuery = periodsQuery.Where(x => x.BuildingId == buildingId.Value);
            chargesQuery = chargesQuery.Where(x => x.Unit!.BuildingId == buildingId.Value);
            paymentsQuery = paymentsQuery.Where(x => x.Unit!.BuildingId == buildingId.Value);
        }

        if (year.HasValue)
            periodsQuery = periodsQuery.Where(x => x.Year == year.Value);

        if (fromMonth.HasValue)
            periodsQuery = periodsQuery.Where(x => x.Month >= fromMonth.Value);

        if (toMonth.HasValue)
            periodsQuery = periodsQuery.Where(x => x.Month <= toMonth.Value);

        var periods = await periodsQuery
            .Select(x => new
            {
                x.Id,
                x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                x.Name,
                x.Year,
                x.Month,
                x.DueDate,
                Status = x.Status.ToString()
            })
            .ToListAsync(cancellationToken);

        var chargeSnapshots = await chargesQuery
            .Select(x => new
            {
                x.ExpensePeriodId,
                x.UnitId,
                x.ChargeType,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        var paymentSnapshots = await paymentsQuery
            .Select(x => new
            {
                x.ExpensePeriodId,
                x.UnitId,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        var unitIds = chargeSnapshots.Select(x => x.UnitId)
            .Concat(paymentSnapshots.Select(x => x.UnitId))
            .Distinct()
            .ToList();

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

        var chargeTotals = chargeSnapshots
            .GroupBy(x => x.ExpensePeriodId)
            .Select(group => new
            {
                ExpensePeriodId = group.Key,
                Amount = group.Sum(x => x.Amount),
                OrdinaryAmount = group.Where(x => x.ChargeType == ExpenseChargeType.Ordinary).Sum(x => x.Amount),
                ReserveFundAmount = group.Where(x => x.ChargeType == ExpenseChargeType.ReserveFund).Sum(x => x.Amount),
                ExtraordinaryAmount = group.Where(x => x.ChargeType == ExpenseChargeType.Extraordinary).Sum(x => x.Amount),
                IndividualAmount = group.Where(x => x.ChargeType == ExpenseChargeType.Individual).Sum(x => x.Amount),
                AdjustmentAmount = group.Where(x => x.ChargeType == ExpenseChargeType.Adjustment).Sum(x => x.Amount)
            })
            .ToList();

        var paymentTotals = paymentSnapshots
            .GroupBy(x => x.ExpensePeriodId)
            .Select(group => new
            {
                ExpensePeriodId = group.Key,
                Amount = group.Sum(x => x.Amount)
            })
            .ToList();

        var responsibilityByUnitPeriod = chargeSnapshots
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId })
            .Select(group =>
            {
                var period = periods.First(x => x.Id == group.Key.ExpensePeriodId);
                var assignedResident = assignments
                    .Where(x =>
                        x.UnitId == group.Key.UnitId &&
                        x.StartDate <= period.DueDate &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= period.DueDate))
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenByDescending(x => x.StartDate)
                    .FirstOrDefault();

                var chargedByType = group
                    .GroupBy(x => x.ChargeType)
                    .ToDictionary(x => x.Key, x => x.Sum(v => v.Amount));
                var totalCollected = paymentSnapshots
                    .Where(x => x.UnitId == group.Key.UnitId && x.ExpensePeriodId == group.Key.ExpensePeriodId)
                    .Sum(x => x.Amount);
                var allocation = AllocatePaymentsByType(chargedByType, totalCollected);

                return new
                {
                    group.Key.ExpensePeriodId,
                    IsOccupied = assignedResident is not null,
                    Charged = chargedByType.Values.Sum(),
                    Collected = decimal.Min(totalCollected, chargedByType.Values.Sum()),
                    Pending = allocation.TotalPendingAmount
                };
            })
            .ToList();

        var chargeMap = chargeTotals.ToDictionary(x => x.ExpensePeriodId);
        var paymentMap = paymentTotals.ToDictionary(x => x.ExpensePeriodId, x => x.Amount);

        var items = periods
            .Select(period =>
            {
                var chargedSnapshot = chargeMap.GetValueOrDefault(period.Id);
                var charged = chargedSnapshot?.Amount ?? 0m;
                var collected = paymentMap.GetValueOrDefault(period.Id, 0m);
                var allocation = AllocatePaymentsByType(new Dictionary<ExpenseChargeType, decimal>
                {
                    [ExpenseChargeType.Ordinary] = chargedSnapshot?.OrdinaryAmount ?? 0m,
                    [ExpenseChargeType.ReserveFund] = chargedSnapshot?.ReserveFundAmount ?? 0m,
                    [ExpenseChargeType.Extraordinary] = chargedSnapshot?.ExtraordinaryAmount ?? 0m,
                    [ExpenseChargeType.Individual] = chargedSnapshot?.IndividualAmount ?? 0m,
                    [ExpenseChargeType.Adjustment] = chargedSnapshot?.AdjustmentAmount ?? 0m
                }, collected);
                var pending = allocation.TotalPendingAmount;
                var rate = charged <= 0m ? 0m : Math.Round((collected / charged) * 100m, 2);
                var periodResponsibility = responsibilityByUnitPeriod.Where(x => x.ExpensePeriodId == period.Id).ToList();

                return new CollectionItemDto
                {
                    BuildingId = period.BuildingId,
                    BuildingName = period.BuildingName,
                    ExpensePeriodId = period.Id,
                    ExpensePeriodName = period.Name,
                    Year = period.Year,
                    Month = period.Month,
                    DueDate = period.DueDate,
                    Status = period.Status,
                    TotalChargedAmount = charged,
                    TotalCollectedAmount = collected,
                    PendingAmount = pending,
                    CollectionRatePercentage = rate,
                    CreditBalanceAmount = allocation.CreditBalanceAmount,
                    OrdinaryChargedAmount = chargedSnapshot?.OrdinaryAmount ?? 0m,
                    ReserveFundChargedAmount = chargedSnapshot?.ReserveFundAmount ?? 0m,
                    ExtraordinaryChargedAmount = chargedSnapshot?.ExtraordinaryAmount ?? 0m,
                    IndividualChargedAmount = chargedSnapshot?.IndividualAmount ?? 0m,
                    AdjustmentChargedAmount = chargedSnapshot?.AdjustmentAmount ?? 0m,
                    ResidentChargedAmount = periodResponsibility.Where(x => x.IsOccupied).Sum(x => x.Charged),
                    ResidentCollectedAmount = periodResponsibility.Where(x => x.IsOccupied).Sum(x => x.Collected),
                    ResidentPendingAmount = periodResponsibility.Where(x => x.IsOccupied).Sum(x => x.Pending),
                    OwnerChargedAmount = periodResponsibility.Where(x => !x.IsOccupied).Sum(x => x.Charged),
                    OwnerCollectedAmount = periodResponsibility.Where(x => !x.IsOccupied).Sum(x => x.Collected),
                    OwnerPendingAmount = periodResponsibility.Where(x => !x.IsOccupied).Sum(x => x.Pending)
                };
            })
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ThenBy(x => x.BuildingName)
            .ToList();

        var rateIndex = items.ToDictionary(x => (x.BuildingId, x.Year, x.Month), x => x.CollectionRatePercentage);
        foreach (var item in items)
        {
            var prevYear = item.Month == 1 ? item.Year - 1 : item.Year;
            var prevMonth = item.Month == 1 ? 12 : item.Month - 1;
            if (rateIndex.TryGetValue((item.BuildingId, prevYear, prevMonth), out var prevRate))
                item.PreviousPeriodCollectionRatePercentage = prevRate;
        }

        var totalCharged = items.Sum(x => x.TotalChargedAmount);
        var totalCollected = items.Sum(x => x.TotalCollectedAmount);
        var totalPending = items.Sum(x => x.PendingAmount);

        return new CollectionReportDto
        {
            Summary = new CollectionSummaryDto
            {
                TotalChargedAmount = totalCharged,
                TotalCollectedAmount = totalCollected,
                TotalPendingAmount = totalPending,
                CollectionRatePercentage = totalCharged <= 0m ? 0m : Math.Round((totalCollected / totalCharged) * 100m, 2),
                TotalCreditBalanceAmount = items.Sum(x => x.CreditBalanceAmount),
                OrdinaryChargedAmount = items.Sum(x => x.OrdinaryChargedAmount),
                ReserveFundChargedAmount = items.Sum(x => x.ReserveFundChargedAmount),
                ExtraordinaryChargedAmount = items.Sum(x => x.ExtraordinaryChargedAmount),
                IndividualChargedAmount = items.Sum(x => x.IndividualChargedAmount),
                AdjustmentChargedAmount = items.Sum(x => x.AdjustmentChargedAmount),
                ResidentChargedAmount = items.Sum(x => x.ResidentChargedAmount),
                ResidentCollectedAmount = items.Sum(x => x.ResidentCollectedAmount),
                ResidentPendingAmount = items.Sum(x => x.ResidentPendingAmount),
                OwnerChargedAmount = items.Sum(x => x.OwnerChargedAmount),
                OwnerCollectedAmount = items.Sum(x => x.OwnerCollectedAmount),
                OwnerPendingAmount = items.Sum(x => x.OwnerPendingAmount)
            },
            Items = items
        };
    }

    private string BuildFilterDescription(Guid? buildingId, int? year, int? fromMonth, int? toMonth)
    {
        var parts = new List<string>();
        if (year.HasValue) parts.Add($"Año {year}");
        if (fromMonth.HasValue || toMonth.HasValue)
        {
            var monthNames = new[] { "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };
            if (fromMonth.HasValue && toMonth.HasValue)
                parts.Add($"{monthNames[fromMonth.Value]} – {monthNames[toMonth.Value]}");
            else if (fromMonth.HasValue)
                parts.Add($"Desde {monthNames[fromMonth.Value]}");
            else
                parts.Add($"Hasta {monthNames[toMonth!.Value]}");
        }
        if (!parts.Any()) parts.Add("Todos los periodos");
        return string.Join("  ·  ", parts);
    }

    private static AllocationResult AllocatePaymentsByType(
        IReadOnlyDictionary<ExpenseChargeType, decimal> chargedByType,
        decimal totalPayments)
    {
        var effectiveBuckets = chargedByType
            .Where(x => x.Value != 0m)
            .ToDictionary(x => x.Key, x => x.Value);
        var totalCharged = effectiveBuckets.Values.Sum();

        if (totalCharged <= 0m)
            return new AllocationResult(decimal.Max(totalPayments, 0m), 0m);

        var appliedPayments = decimal.Min(totalPayments, totalCharged);
        var pending = decimal.Round(totalCharged - appliedPayments, 2, MidpointRounding.AwayFromZero);
        var credit = decimal.Max(totalPayments - totalCharged, 0m);
        return new AllocationResult(credit, pending);
    }

    private sealed record AllocationResult(decimal CreditBalanceAmount, decimal TotalPendingAmount);
}
