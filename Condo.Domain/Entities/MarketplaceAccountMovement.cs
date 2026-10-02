using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// Renglon del extracto de la cuenta aparte del marketplace (una por edificio). No es una cuenta bancaria ni toca la
/// contabilidad ni las Finanzas del edificio: solo refleja entradas y salidas por concepto. El importe lleva signo.
/// Los movimientos automaticos de una reserva son unicos por (reserva, tipo): asi una operacion no se asienta dos veces.
/// </summary>
public class MarketplaceAccountMovement : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public MarketplaceAccountMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public Guid? ReservationId { get; set; }
    public string Concept { get; set; } = string.Empty;
    // Nulo = movimiento automatico del sistema.
    public Guid? CreatedByUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public MarketplaceReservation? Reservation { get; set; }
}
