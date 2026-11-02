using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    // Pais de la empresa: de aqui salen la moneda de todos sus documentos, el idioma regional y los tipos de documento. Las empresas
    // existentes quedan en Paraguay. La moneda no se cambia una vez que la empresa tiene movimientos.
    public string CountryCode { get; set; } = "PY";
    public string CurrencyCode { get; set; } = "PYG";
    public string LocaleCode { get; set; } = "es-PY";
    // Impuesto de la empresa (IVA u otro) tal como se fija al crearla. Tasa general, reducida (opcional) y exento (0 siempre).
    public string TaxName { get; set; } = "IVA";
    public decimal? TaxGeneralRate { get; set; } = 10m;
    public decimal? TaxReducedRate { get; set; } = 5m;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<Condominium> Condominiums { get; set; } = new List<Condominium>();
    public ICollection<Building> Buildings { get; set; } = new List<Building>();
    public ICollection<BuildingExpense> BuildingExpenses { get; set; } = new List<BuildingExpense>();
    public ICollection<BuildingIncome> BuildingIncomes { get; set; } = new List<BuildingIncome>();
    public ICollection<ExpenseCharge> ExpenseCharges { get; set; } = new List<ExpenseCharge>();
    public ICollection<ExpenseSettlement> ExpenseSettlements { get; set; } = new List<ExpenseSettlement>();
    public ICollection<ExpensePeriod> ExpensePeriods { get; set; } = new List<ExpensePeriod>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Resident> Residents { get; set; } = new List<Resident>();
}
