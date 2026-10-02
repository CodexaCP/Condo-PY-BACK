using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class RecurringBuildingExpense : CompanyScopedEntity
{
    // Null = plantilla general, aplicable a todos los edificios de la empresa (se usa en cualquier
    // periodo que se le "aplique" sin importar el edificio); con valor, solo a ese edificio puntual.
    public Guid? BuildingId { get; set; }
    public BuildingExpenseCategory Category { get; set; } = BuildingExpenseCategory.Other;
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; } = BuildingExpenseDistributionType.ByCoefficient;
    public Guid? TargetUnitId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    // Rubro del plan de cuentas de Finanzas del edificio (opcional). Solo en las plantillas de un edificio puntual: el plan es de
    // cada edificio. Los gastos que genera la plantilla nacen con este rubro, si sigue disponible al aplicarla.
    public Guid? LedgerCategoryId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public Unit? TargetUnit { get; set; }
    public LedgerCategory? LedgerCategory { get; set; }
}
