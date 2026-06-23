using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class ExpenseCharge : CompanyScopedEntity
{
    public Guid ExpensePeriodId { get; set; }
    public Guid UnitId { get; set; }
    public ExpenseChargeType ChargeType { get; set; } = ExpenseChargeType.Ordinary;
    public Guid? SourceBuildingExpenseId { get; set; }
    public Guid? SourceSettlementId { get; set; }
    public bool IsLateFee { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;

    public Company? Company { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
    public Unit? Unit { get; set; }
    public BuildingExpense? SourceBuildingExpense { get; set; }
    public ExpenseSettlement? SourceSettlement { get; set; }
}
