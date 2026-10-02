using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// "Reportar un problema": lo abre el comprador o el propietario desde que empieza la reserva hasta 24 horas despues de su fin.
/// Mientras esta abierto retiene la acreditacion del saldo; lo resuelve el Encargado. Hay uno abierto por reserva como maximo.
/// </summary>
public class MarketplaceClaim : CompanyScopedEntity
{
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid OpenedByUserId { get; set; }
    public MarketplaceClaimParty OpenedBy { get; set; }
    public string Reason { get; set; } = string.Empty;
    public MarketplaceClaimStatus Status { get; set; } = MarketplaceClaimStatus.Open;
    public MarketplaceClaimResolution? Resolution { get; set; }
    // Explicacion del Encargado, visible para las dos partes.
    public string? ResolutionNote { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Company? Company { get; set; }
    public MarketplaceReservation? Reservation { get; set; }
    public Building? Building { get; set; }
    public ApplicationUser? OpenedByUser { get; set; }
    public ApplicationUser? ResolvedByUser { get; set; }
}
