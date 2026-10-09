using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Proveedor de una empresa (compartido entre todos sus edificios). Los gastos del edificio lo eligen por SupplierId; el nombre queda
// copiado en el gasto (BuildingExpense.SupplierName), asi que cambiar el nombre aqui no reescribe gastos ya cargados. El RUC es unico por
// empresa (cuando se informa). No se elimina: se desactiva.
public class Supplier : CompanyScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Ruc { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    // Plazo de pago habitual en dias (opcional): al cargarle un gasto, el vencimiento se propone como fecha del gasto + plazo.
    public int? PaymentTermDays { get; set; }

    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
}
