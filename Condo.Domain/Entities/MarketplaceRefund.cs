using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// Reembolso pendiente al comprador (version 1: el dinero se devuelve fuera del sistema). Se crea al cancelar una reserva ya
/// pagada o al resolver un reclamo a favor del comprador; el Encargado lo devuelve a mano y lo marca "devuelto". Es unico
/// por reserva: una operacion no se reembolsa dos veces.
/// </summary>
public class MarketplaceRefund : CompanyScopedEntity
{
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    // El comprador: a quien hay que devolverle.
    public Guid RecipientUserId { get; set; }
    public decimal Amount { get; set; }
    public MarketplaceRefundOrigin Origin { get; set; }
    public string Reason { get; set; } = string.Empty;
    public MarketplaceRefundStatus Status { get; set; } = MarketplaceRefundStatus.Pending;
    public DateTime? ReturnedAtUtc { get; set; }
    public Guid? ReturnedByUserId { get; set; }
    // Se avisa una sola vez al Encargado si pasan 72 horas sin devolverlo.
    public DateTime? OverdueAlertSentAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Company? Company { get; set; }
    public MarketplaceReservation? Reservation { get; set; }
    public Building? Building { get; set; }
    public ApplicationUser? Recipient { get; set; }
    public ApplicationUser? ReturnedByUser { get; set; }
}
