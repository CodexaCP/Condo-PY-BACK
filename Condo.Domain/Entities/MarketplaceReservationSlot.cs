using Condo.Domain.Common;

namespace Condo.Domain.Entities;

/// <summary>
/// Un bloque de 30 minutos ocupado por una reserva. El indice unico (publicacion, inicio del bloque) entre los renglones
/// no borrados es lo que impide en la base de datos que dos reservas ocupen el mismo horario. Al vencer o cancelarse la
/// reserva, sus renglones se marcan IsDeleted y el horario queda libre.
/// </summary>
public class MarketplaceReservationSlot : CompanyScopedEntity
{
    public Guid ReservationId { get; set; }
    public Guid ListingId { get; set; }
    public DateTime SlotStartUtc { get; set; }

    public MarketplaceReservation? Reservation { get; set; }
    public MarketplaceListing? Listing { get; set; }
}
