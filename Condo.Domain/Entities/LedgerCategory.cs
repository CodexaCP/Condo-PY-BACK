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
    // las categorias actuales de gastos e ingresos sin tocar esas entidades. Nula en los rubros creados a mano.
    public string? SystemKey { get; set; }

    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public LedgerCategory? Parent { get; set; }
    public ICollection<LedgerCategory> Children { get; set; } = new List<LedgerCategory>();
}
