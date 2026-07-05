using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Amenity : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal ReservationPrice { get; set; }
    public bool IsActive { get; set; } = true;

    public Building? Building { get; set; }
    public Company? Company { get; set; }
    public ICollection<AmenityReservation> Reservations { get; set; } = new List<AmenityReservation>();
}
