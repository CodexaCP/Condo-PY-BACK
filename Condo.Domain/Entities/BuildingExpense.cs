using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class BuildingExpense : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public BuildingExpenseCategory Category { get; set; } = BuildingExpenseCategory.Other;
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; } = BuildingExpenseDistributionType.ByCoefficient;
    public Guid? TargetUnitId { get; set; }
    public string Notes { get; set; } = string.Empty;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
    public Unit? TargetUnit { get; set; }
    public ICollection<ExpenseCharge> ExpenseCharges { get; set; } = new List<ExpenseCharge>();
}
