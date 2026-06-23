using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Building : BaseEntity
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    public Company? Company { get; set; }
    public Condominium? Condominium { get; set; }
    public ICollection<Unit> Units { get; set; } = new List<Unit>();
    public ICollection<BuildingExpense> BuildingExpenses { get; set; } = new List<BuildingExpense>();
    public ICollection<BuildingIncome> BuildingIncomes { get; set; } = new List<BuildingIncome>();
    public ICollection<ExpenseSettlement> ExpenseSettlements { get; set; } = new List<ExpenseSettlement>();
    public ICollection<ExpensePeriod> ExpensePeriods { get; set; } = new List<ExpensePeriod>();
    public ICollection<UserBuildingAccess> UserAccesses { get; set; } = new List<UserBuildingAccess>();
}
