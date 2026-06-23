using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class RecurringBuildingExpenseUpsertRequest
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
}

public class RecurringBuildingExpenseDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public BuildingExpenseCategory Category { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; }
    public Guid? TargetUnitId { get; set; }
    public string TargetUnitCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class ApplyRecurringExpensesRequest
{
    public Guid ExpensePeriodId { get; set; }
}

public class ApplyRecurringExpensesResultDto
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public List<string> AppliedDescriptions { get; set; } = [];
}
