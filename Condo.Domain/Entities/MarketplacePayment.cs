using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// Comprobante de transferencia de una reserva y su revision por el personal. Es un pago interno del marketplace: no es
/// una factura ni un documento fiscal. Entidad propia (no OwnerPayment) porque aquel exige coincidir con la deuda de expensas.
/// </summary>
public class MarketplacePayment : CompanyScopedEntity
{
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public string ComprobanteUrl { get; set; } = string.Empty;
    // Total que la reserva tenia que pagar (congelado).
    public decimal ExpectedAmount { get; set; }
    // Monto que el revisor vio en el comprobante; solo se aprueba si coincide con el esperado.
    public decimal? ReviewedAmount { get; set; }
    public MarketplacePaymentStatus Status { get; set; } = MarketplacePaymentStatus.Submitted;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    // Alertas al revisor mientras el pago espera revision: cuantas se mandaron y cuando fue la ultima.
    public int AlertCount { get; set; }
    public DateTime? LastAlertAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Company? Company { get; set; }
    public MarketplaceReservation? Reservation { get; set; }
    public Building? Building { get; set; }
    public ApplicationUser? Buyer { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
}
