using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class RecurringBuildingExpense : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public BuildingExpenseCategory Category { get; set; } = BuildingExpenseCategory.Other;
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; } = BuildingExpenseDistributionType.ByCoefficient;
    public Guid? TargetUnitId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public Unit? TargetUnit { get; set; }
}
