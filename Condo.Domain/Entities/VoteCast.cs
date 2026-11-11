using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Voto vigente de una unidad (uno por unidad y votacion). Cada cambio queda ademas en VoteCastEvent.
public class VoteCast
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoteId { get; set; }
    public Guid VoteOptionId { get; set; }
    public Guid UnitId { get; set; }
    public decimal CoefficientWeight { get; set; }
    public DateTime CastAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CastByUserId { get; set; }
    // Canal del voto vigente: el propietario o el personal (los votos anteriores a esto son todos del personal).
    public VoteCastSource Source { get; set; } = VoteCastSource.Staff;

    public Vote Vote { get; set; } = null!;
    public VoteOption Option { get; set; } = null!;
    public Unit Unit { get; set; } = null!;
    public ApplicationUser? CastBy { get; set; }
}
