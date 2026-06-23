using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class BuildingIncome : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public BuildingIncomeCategory Category { get; set; } = BuildingIncomeCategory.Other;
    public string Description { get; set; } = string.Empty;
    public DateOnly IncomeDate { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
}
