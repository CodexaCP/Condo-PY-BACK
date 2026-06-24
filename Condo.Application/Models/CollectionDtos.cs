using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class CollectionSummaryDto
{
    public decimal TotalChargedAmount { get; set; }
    public decimal TotalCollectedAmount { get; set; }
    public decimal TotalPendingAmount { get; set; }
    public decimal CollectionRatePercentage { get; set; }
    public decimal TotalCreditBalanceAmount { get; set; }
    public decimal OrdinaryChargedAmount { get; set; }
    public decimal ReserveFundChargedAmount { get; set; }
    public decimal ExtraordinaryChargedAmount { get; set; }
    public decimal IndividualChargedAmount { get; set; }
    public decimal AdjustmentChargedAmount { get; set; }
    public decimal ResidentChargedAmount { get; set; }
    public decimal ResidentCollectedAmount { get; set; }
    public decimal ResidentPendingAmount { get; set; }
    public decimal OwnerChargedAmount { get; set; }
    public decimal OwnerCollectedAmount { get; set; }
    public decimal OwnerPendingAmount { get; set; }
}

public class CollectionItemDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalChargedAmount { get; set; }
    public decimal TotalCollectedAmount { get; set; }
    public decimal PendingAmount { get; set; }
    public decimal CollectionRatePercentage { get; set; }
    public decimal CreditBalanceAmount { get; set; }
    public decimal OrdinaryChargedAmount { get; set; }
    public decimal ReserveFundChargedAmount { get; set; }
    public decimal ExtraordinaryChargedAmount { get; set; }
    public decimal IndividualChargedAmount { get; set; }
    public decimal AdjustmentChargedAmount { get; set; }
    public decimal ResidentChargedAmount { get; set; }
    public decimal ResidentCollectedAmount { get; set; }
    public decimal ResidentPendingAmount { get; set; }
    public decimal OwnerChargedAmount { get; set; }
    public decimal OwnerCollectedAmount { get; set; }
    public decimal OwnerPendingAmount { get; set; }
    public decimal? PreviousPeriodCollectionRatePercentage { get; set; }
}

public class CollectionReportDto
{
    public CollectionSummaryDto Summary { get; set; } = new();
    public IReadOnlyList<CollectionItemDto> Items { get; set; } = [];
}
