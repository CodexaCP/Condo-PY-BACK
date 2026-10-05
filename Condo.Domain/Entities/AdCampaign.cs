using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class AdCampaign : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string AdvertiserName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CtaText { get; set; } = string.Empty;
    public string? CtaUrl { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public AdCampaignCategory Category { get; set; }
    public int Position { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal? MonthlyAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NotifyBeforeExpiry { get; set; } = true;
    public bool ExpiryNotificationSent { get; set; } = false;

    public Company? Company { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
    public ICollection<AdCampaignBuilding> Buildings { get; set; } = new List<AdCampaignBuilding>();
}
