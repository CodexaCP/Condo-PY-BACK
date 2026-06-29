using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Vote : BaseEntity
{
    public Guid BuildingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal QuorumPercentage { get; set; } = 50;
    public string WeightType { get; set; } = "ByUnit";
    public string Status { get; set; } = "Draft";
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Building Building { get; set; } = null!;
    public ApplicationUser? CreatedBy { get; set; }
    public ICollection<VoteOption> Options { get; set; } = new List<VoteOption>();
    public ICollection<VoteCast> Casts { get; set; } = new List<VoteCast>();
}
