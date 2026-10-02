using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class RecurringBuildingExpenseUpsertRequest
{
    // Null = plantilla general para todos los edificios de la empresa.
    public Guid? BuildingId { get; set; }
    public BuildingExpenseCategory Category { get; set; } = BuildingExpenseCategory.Other;
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; } = BuildingExpenseDistributionType.ByCoefficient;
    public Guid? TargetUnitId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    // Rubro del plan de cuentas de Finanzas del edificio (opcional; solo con un edificio puntual). Con rubro, la categoria sale del rubro.
    public Guid? LedgerCategoryId { get; set; }
}

public class RecurringBuildingExpenseDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BuildingId { get; set; }
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
    public Guid? LedgerCategoryId { get; set; }
    public string? LedgerCategoryCode { get; set; }
    public string? LedgerCategoryName { get; set; }
}

public class ApplyRecurringExpensesRequest
{
    public Guid ExpensePeriodId { get; set; }
}

public class ApplyRecurringExpensesResultDto
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    // Gastos creados sin el rubro de su plantilla porque el rubro ya no esta disponible (desactivado, eliminado o modulo apagado):
    // quedan solo con su categoria, como siempre.
    public int WithoutRubro { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public List<string> AppliedDescriptions { get; set; } = [];
}
