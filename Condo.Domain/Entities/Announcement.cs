using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Announcement : BaseEntity
{
    public Guid BuildingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public DateTime? PublishedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }

    public Building Building { get; set; } = null!;
    public ApplicationUser? CreatedBy { get; set; }
}
