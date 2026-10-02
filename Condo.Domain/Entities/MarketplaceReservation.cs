using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// La operacion comercial completa: que se reservo, quien la reservo, de quien era, cuanto pago el comprador, cuanto
/// corresponde al propietario y cuanto a la gestion. Los importes se congelan al crearla y nunca se recalculan con la
/// publicacion ni con la comision vigente.
/// </summary>
public class MarketplaceReservation : CompanyScopedEntity
{
    public Guid ListingId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    public Guid BuyerUserId { get; set; }
    // Propietario principal al crear la reserva: es a quien se le acredita la ganancia, aunque despues cambie el principal.
    public Guid OwnerId { get; set; }
    // Numero de operacion visible (p. ej. MP-00000125), unico por empresa.
    public string Reference { get; set; } = string.Empty;

    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Hours { get; set; }

    // Importes congelados.
    public decimal HourlyPrice { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal OwnerNetAmount { get; set; }

    public MarketplaceReservationStatus Status { get; set; } = MarketplaceReservationStatus.PendingPayment;
    // Solo mientras espera el pago: pasado este instante la reserva vence y libera el horario.
    public DateTime? ExpiresAtUtc { get; set; }

    // Acreditacion de la ganancia al saldo a favor del propietario (estado aparte del de la reserva).
    public MarketplaceCreditStatus CreditStatus { get; set; } = MarketplaceCreditStatus.None;
    public DateTime? CreditedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public MarketplaceCancellationActor? CancelledBy { get; set; }
    public string? CancelReason { get; set; }

    // Control de concurrencia (solo SQL Server la actualiza sola). Evita que dos cambios simultaneos se pisen.
    public byte[] RowVersion { get; set; } = [];

    public Company? Company { get; set; }
    public MarketplaceListing? Listing { get; set; }
    public Building? Building { get; set; }
    public Unit? Unit { get; set; }
    public ApplicationUser? Buyer { get; set; }
    public ApplicationUser? Owner { get; set; }
    public ICollection<MarketplaceReservationSlot> Slots { get; set; } = new List<MarketplaceReservationSlot>();
    public ICollection<MarketplacePayment> Payments { get; set; } = new List<MarketplacePayment>();
}
