using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class UnitOwner : CompanyScopedEntity
{
    public Guid UnitId { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    // Porcentaje de titularidad cuando hay copropietarios (vacio = no se informa; los porcentajes vigentes de la unidad no suman mas de 100).
    public decimal? OwnershipPercentage { get; set; }
    // Al darlo de baja (cambio de propietario) queda la fecha y el motivo; la fila se conserva como historial.
    public DateOnly? EndDate { get; set; }
    public string? TransferReason { get; set; }

    public Company? Company { get; set; }
    public Unit? Unit { get; set; }
    public ApplicationUser? Owner { get; set; }
}
