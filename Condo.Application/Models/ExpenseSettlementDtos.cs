using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class ExpenseSettlementSummaryDto
{
    public Guid? Id { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public Guid BuildingId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public decimal TotalBuildingExpenses { get; set; }
    public decimal TotalBuildingIncomes { get; set; }
    public decimal ReserveFundAmount { get; set; }
    public decimal ExtraordinaryAmount { get; set; }
    public decimal NetCommonAmount { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public string GeneratedByUserName { get; set; } = string.Empty;
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string ApprovedByUserName { get; set; } = string.Empty;
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public string PublishedByUserName { get; set; } = string.Empty;
    public ExpenseSettlementStatus? Status { get; set; }
    public ExpensePeriodStatus PeriodStatus { get; set; }
    public int GeneratedChargeCount { get; set; }
    public bool IsCalculated { get; set; }
    public List<SettlementCategoryTotalDto> CategoryTotals { get; set; } = new();
}

public class SettlementCategoryTotalDto
{
    public string Category { get; set; } = string.Empty;
    public int ExpenseCount { get; set; }
    public decimal Amount { get; set; }
}

public class ExpenseSettlementChargePreviewDto
{
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid SettlementId { get; set; }
    public int ChargeCount { get; set; }
    public int UnitsAffected { get; set; }
    public decimal TotalGeneratedAmount { get; set; }
    public IReadOnlyList<ExpenseSettlementChargePreviewItemDto> Items { get; set; } = [];
}

public class ExpenseSettlementChargePreviewItemDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public ExpenseChargeType ChargeType { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? SourceBuildingExpenseId { get; set; }
    public Guid SourceSettlementId { get; set; }
}

public class VoidSettlementResultDto
{
    public string ExpensePeriodName { get; set; } = string.Empty;
    public int DeletedChargeCount { get; set; }
}

public class ApplyLateFeesRequest
{
    public decimal RatePercentage { get; set; }
    public DateOnly? ReferenceDate { get; set; }
    public string Concept { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class ApplyLateFeesResultDto
{
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public DateOnly ReferenceDate { get; set; }
    public decimal RatePercentage { get; set; }
    public int ChargesCreated { get; set; }
    public int UnitsAffected { get; set; }
    public decimal TotalLateFeeAmount { get; set; }
}

public class ExpensePeriodOperationalAlertsDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public IReadOnlyList<ExpensePeriodOperationalAlertItemDto> Items { get; set; } = [];
}

public class ExpensePeriodOperationalAlertItemDto
{
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public int DaysUntilDue { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ExpensePeriodStatus PeriodStatus { get; set; }
    public ExpenseSettlementStatus? SettlementStatus { get; set; }
    public decimal PendingAmount { get; set; }
}
