using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Presupuesto mensual de un rubro (subrubro de ingresos o de gastos) del edificio. Se identifica por el mes calendario
// (Year/Month); el ejercicio es solo la forma de agrupar 12 meses seguidos desde FinanceSettings.FiscalYearStartMonth.
public class BudgetLine : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid CategoryId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public LedgerCategory? Category { get; set; }
}
