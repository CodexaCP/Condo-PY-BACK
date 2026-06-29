namespace Condo.Domain.Entities;

public class VoteCast
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoteId { get; set; }
    public Guid VoteOptionId { get; set; }
    public Guid UnitId { get; set; }
    public decimal CoefficientWeight { get; set; }
    public DateTime CastAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CastByUserId { get; set; }

    public Vote Vote { get; set; } = null!;
    public VoteOption Option { get; set; } = null!;
    public Unit Unit { get; set; } = null!;
    public ApplicationUser? CastBy { get; set; }
}
