using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Rubro del plan de cuentas del edificio. Arbol de dos niveles: un rubro (ParentId nulo) agrupa subrubros, y los
// movimientos se asientan en los subrubros (hojas). El codigo es editable (para exportar al contador).
public class LedgerCategory : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LedgerCategoryType Type { get; set; } = LedgerCategoryType.Expense;

    // Codigo de la cuenta equivalente en el plan del contador (mapeo manual).
    public string? ExternalCode { get; set; }

    // Clave estable de los rubros de la plantilla (por ejemplo "Expense.Ande"): el libro derivado la usa para mapear
    // las categorias actuales de gastos e ingresos de los movimientos que no eligieron rubro. Nula en los rubros creados a mano.
    public string? SystemKey { get; set; }

    // Rubros creados a mano: con que categoria cuentan en la liquidacion los gastos o ingresos que se cargan en el rubro
    // (la liquidacion sigue agrupando por categoria). En los rubros de la plantilla se deduce de la SystemKey.
    public BuildingExpenseCategory? ExpenseCategory { get; set; }
    public BuildingIncomeCategory? IncomeCategory { get; set; }

    public bool IsActive { get; set; } = true;

    // Clave con la que el libro identifica al rubro: la de la plantilla o, en los rubros propios, una derivada de su id.
    public string RubroKey => SystemKey ?? $"Rubro.{Id:N}";

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public LedgerCategory? Parent { get; set; }
    public ICollection<LedgerCategory> Children { get; set; } = new List<LedgerCategory>();
}
