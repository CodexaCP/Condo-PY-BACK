using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Unit : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ICollection<BuildingExpense> BuildingExpenses { get; set; } = new List<BuildingExpense>();
    public ICollection<ExpenseCharge> ExpenseCharges { get; set; } = new List<ExpenseCharge>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<UnitResident> UnitResidents { get; set; } = new List<UnitResident>();
}
