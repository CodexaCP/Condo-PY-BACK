namespace Condo.Domain.Entities;

public class VoteOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoteId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public Vote Vote { get; set; } = null!;
    public ICollection<VoteCast> Casts { get; set; } = new List<VoteCast>();
}
