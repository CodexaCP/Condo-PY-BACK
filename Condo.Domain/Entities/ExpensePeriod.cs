using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class ExpensePeriod : CompanyScopedEntity
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

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ICollection<BuildingExpense> BuildingExpenses { get; set; } = new List<BuildingExpense>();
    public ICollection<BuildingIncome> BuildingIncomes { get; set; } = new List<BuildingIncome>();
    public ICollection<ExpenseSettlement> ExpenseSettlements { get; set; } = new List<ExpenseSettlement>();
    public ICollection<ExpenseCharge> Charges { get; set; } = new List<ExpenseCharge>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
