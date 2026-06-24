using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class ExpenseChargeUpsertRequest
{
    public Guid ExpensePeriodId { get; set; }
    public Guid UnitId { get; set; }
    public ExpenseChargeType ChargeType { get; set; } = ExpenseChargeType.Ordinary;
    public bool IsLateFee { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class ExpenseChargeDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public ExpenseChargeType ChargeType { get; set; }
    public Guid? SourceBuildingExpenseId { get; set; }
    public string SourceBuildingExpenseDescription { get; set; } = string.Empty;
    public Guid? SourceSettlementId { get; set; }
    public string SourceSettlementName { get; set; } = string.Empty;
    public bool IsLateFee { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsReversal { get; set; }
    public Guid? ReversalOfChargeId { get; set; }
    public bool IsReversed { get; set; }
    public decimal TotalAllocated { get; set; }
    public decimal PendingAmount { get; set; }
}
