using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class AmenityReservation : CompanyScopedEntity
{
    public Guid AmenityId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid ReservedByUserId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public decimal Price { get; set; }
    public AmenityReservationStatus Status { get; set; } = AmenityReservationStatus.PendingPayment;
    public string Notes { get; set; } = string.Empty;
    public string? ComprobanteUrl { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    public Amenity? Amenity { get; set; }
    public Building? Building { get; set; }
    public ApplicationUser? ReservedByUser { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
    public Company? Company { get; set; }
}
