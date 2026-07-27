using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class AccountStatementPeriodDto
{
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly DueDate { get; set; }
    public ExpensePeriodStatus Status { get; set; }
    public decimal TotalCharges { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal Balance { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal RunningBalance { get; set; }
}

public class AccountStatementChargeDto
{
    public Guid Id { get; set; }
    public ExpenseChargeType ChargeType { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsReversal { get; set; }
}

public class AccountStatementPaymentDto
{
    public Guid Id { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsReversed { get; set; }
    public DateTime? ReversedAt { get; set; }
}

public class AccountStatementDetailDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly DueDate { get; set; }
    public ExpensePeriodStatus Status { get; set; }
    public IReadOnlyList<AccountStatementChargeDto> Charges { get; set; } = [];
    public IReadOnlyList<AccountStatementPaymentDto> Payments { get; set; } = [];
    public decimal TotalCharges { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal Balance { get; set; }
}

public class ExpenseReceiptDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly DueDate { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string? OwnerDocumentType { get; set; }
    public string? OwnerDocumentNumber { get; set; }
    public string ResidentName { get; set; } = string.Empty;
    public string? ResidentDocumentType { get; set; }
    public string? ResidentDocumentNumber { get; set; }
    public decimal UnitCoefficient { get; set; }
    public IReadOnlyList<ExpenseReceiptChargeDto> Charges { get; set; } = [];
    public IReadOnlyList<AccountStatementPaymentDto> Payments { get; set; } = [];
    public decimal OrdinaryAmount { get; set; }
    public decimal ReserveFundAmount { get; set; }
    public decimal ExtraordinaryAmount { get; set; }
    public decimal IndividualAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal Balance { get; set; }
}

public class ExpenseReceiptChargeDto
{
    public Guid Id { get; set; }
    public ExpenseChargeType ChargeType { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsReversal { get; set; }
}
