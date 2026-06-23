using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class RolloverIncomeRequest
{
    public Guid BuildingId { get; set; }
    public Guid SourcePeriodId { get; set; }
    public Guid TargetPeriodId { get; set; }
}

public class RolloverIncomeResultDto
{
    public string SourcePeriodName { get; set; } = string.Empty;
    public string TargetPeriodName { get; set; } = string.Empty;
    public decimal TotalIngresos { get; set; }
    public decimal TotalGastos { get; set; }
    public decimal Saldo { get; set; }
    public bool RolloverCreated { get; set; }
    public BuildingIncomeDto? CreatedIncome { get; set; }
}

public class BuildingIncomeUpsertRequest
{
    public Guid BuildingId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public BuildingIncomeCategory Category { get; set; } = BuildingIncomeCategory.Other;
    public string Description { get; set; } = string.Empty;
    public DateOnly IncomeDate { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class BuildingIncomeDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public BuildingIncomeCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateOnly IncomeDate { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}
