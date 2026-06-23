using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class ExpensePeriodUpsertRequest
{
    public Guid BuildingId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly DueDate { get; set; }
    public ExpensePeriodStatus Status { get; set; } = ExpensePeriodStatus.Draft;
    public string Notes { get; set; } = string.Empty;
}

public class ExpensePeriodDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly DueDate { get; set; }
    public ExpensePeriodStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class GenerateExpenseChargesRequest
{
    public string Mode { get; set; } = "FixedAmount";
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class GenerateExpenseChargesResultDto
{
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public int UnitsAffected { get; set; }
    public decimal TotalGeneratedAmount { get; set; }
    public string Mode { get; set; } = string.Empty;
}
