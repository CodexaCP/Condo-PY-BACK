using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

/// <summary>
/// Historial de solo insercion de los votos de cada unidad: primer voto, cambio de opcion o voto quitado, con quien lo hizo y por que
/// canal. VoteCast es el voto vigente; esto es la constancia de como se llego a el. Nunca se modifica ni se borra.
/// </summary>
public class VoteCastEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoteId { get; set; }
    public Guid UnitId { get; set; }
    public VoteCastAction Action { get; set; }
    // Opcion que tenia la unidad antes (null en el primer voto) y la que queda (null cuando se quita el voto).
    public Guid? PreviousOptionId { get; set; }
    public Guid? NewOptionId { get; set; }
    // Coeficiente de la unidad al momento del evento.
    public decimal CoefficientWeight { get; set; }
    public VoteCastSource Source { get; set; }
    // Usuario que hizo la accion (propietario o personal) y propietario principal de la unidad en ese momento, si habia.
    public Guid? ActingUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public Vote Vote { get; set; } = null!;
    public Unit Unit { get; set; } = null!;
    public VoteOption? PreviousOption { get; set; }
    public VoteOption? NewOption { get; set; }
    public ApplicationUser? ActingUser { get; set; }
    public ApplicationUser? OwnerUser { get; set; }
}
