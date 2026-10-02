using Condo.Domain.Common;

namespace Condo.Domain.Entities;

/// <summary>
/// Deuda por gestion del propietario que cancela (o que pierde un reclamo): la comision que asume y que su saldo a favor no
/// alcanzo a cubrir. No se suma a las expensas ni toca su calculo: se descuenta automaticamente de su proxima acreditacion del
/// marketplace en el mismo edificio, y el Encargado la ve mientras este pendiente. Una por reserva.
/// </summary>
public class MarketplaceOwnerDebt : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid OwnerId { get; set; }
    // Reserva que la origino.
    public Guid ReservationId { get; set; }
    public decimal Amount { get; set; }
    // Cuanto ya se desconto de acreditaciones posteriores. Pendiente = Amount - PaidAmount.
    public decimal PaidAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? SettledAtUtc { get; set; }

    public decimal Remaining => Amount - PaidAmount;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ApplicationUser? Owner { get; set; }
    public MarketplaceReservation? Reservation { get; set; }
}
