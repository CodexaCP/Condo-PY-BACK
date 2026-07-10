using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Plan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public decimal Price { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public int GracePeriodDays { get; set; } = 5;
    public bool IsActive { get; set; } = true;

    // Readonly once at least one BuildingPlan references this plan
    public bool IsAssigned { get; set; }

    public ICollection<BuildingPlan> BuildingPlans { get; set; } = new List<BuildingPlan>();
}
