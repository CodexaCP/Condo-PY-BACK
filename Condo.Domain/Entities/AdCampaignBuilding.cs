using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class AdCampaignBuilding : BaseEntity
{
    public Guid AdCampaignId { get; set; }
    public Guid BuildingId { get; set; }

    public AdCampaign? AdCampaign { get; set; }
    public Building? Building { get; set; }
}
