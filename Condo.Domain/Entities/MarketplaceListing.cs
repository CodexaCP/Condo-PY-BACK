using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// Publicacion de una unidad por horas ("Cochera 12"). La hace el propietario principal de la unidad; el precio por hora
/// es lo que el propietario recibe (la comision de gestion se suma aparte en cada reserva). La ventana es de horas
/// enteras, con inicio y fin en punto o y media.
/// </summary>
public class MarketplaceListing : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    // Propietario principal que publica (al momento de publicar).
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public decimal HourlyPrice { get; set; }
    public MarketplaceListingStatus Status { get; set; } = MarketplaceListingStatus.Active;
    // Motivo de la suspension o del cierre, para mostrarlo y auditarlo.
    public string? StatusReason { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public Unit? Unit { get; set; }
    public ApplicationUser? Owner { get; set; }
    public ICollection<MarketplaceReservation> Reservations { get; set; } = new List<MarketplaceReservation>();
}
