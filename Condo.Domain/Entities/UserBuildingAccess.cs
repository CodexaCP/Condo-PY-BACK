using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class UserBuildingAccess : CompanyScopedEntity
{
    public Guid ApplicationUserId { get; set; }
    public Guid BuildingId { get; set; }
    public bool IsActive { get; set; } = true;

    public ApplicationUser? ApplicationUser { get; set; }
    public Building? Building { get; set; }
}
