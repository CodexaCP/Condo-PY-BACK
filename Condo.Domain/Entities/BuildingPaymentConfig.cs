using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Asignacion de una conexion de pago a un edificio. La conexion y el edificio son de la misma empresa. Un edificio puede tener varias
// conexiones (por ejemplo Tpago y vPOS); una conexion puede servir a varios edificios.
public class BuildingPaymentConfig : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid ConnectionId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public PaymentConnection? Connection { get; set; }
}
