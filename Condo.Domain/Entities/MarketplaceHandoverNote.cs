using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// Documento interno que se genera cuando cambia el propietario principal de una unidad que tiene operaciones del marketplace
/// abiertas: explica que paso y cual era la situacion en ese momento. Lo leen y lo resuelven quienes administran el edificio. Las
/// reservas ya hechas conservan al propietario con el que se crearon (a el se le acredita); las publicaciones sin reservar se
/// suspenden solas. El texto queda fijo; la situacion ACTUAL de cada operacion se consulta en vivo al abrirlo.
/// </summary>
public class MarketplaceHandoverNote : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    // Propietario principal que dejo de serlo (el de las reservas afectadas).
    public Guid PreviousOwnerId { get; set; }
    // Nuevo propietario principal, si ya hay uno (puede asignarse despues: se completa solo).
    public Guid? NewOwnerId { get; set; }
    public MarketplaceHandoverTrigger Trigger { get; set; }
    // Texto de lo que paso y de la situacion al momento del cambio.
    public string Content { get; set; } = string.Empty;
    // Reservas afectadas (ids separados por coma): sirven para mostrar su estado actual.
    public string ReservationIds { get; set; } = string.Empty;
    public int ReservationCount { get; set; }
    // La primera vez que alguien del personal la abre o la descarga.
    public DateTime? ReadAtUtc { get; set; }
    public Guid? ReadByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public Unit? Unit { get; set; }
    public ApplicationUser? PreviousOwner { get; set; }
    public ApplicationUser? NewOwner { get; set; }
    public ApplicationUser? ReadByUser { get; set; }
}
