using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class BuildingPlan : BaseEntity
{
    public Guid PlanId { get; set; }
    public Guid BuildingId { get; set; }

    // Scope of the assignment that created this record
    public PlanAssignmentScope AssignmentScope { get; set; } = PlanAssignmentScope.Building;

    // Id of the entity (Building, Condominium or Company) that originated the assignment
    public Guid ScopeEntityId { get; set; }

    // Active period
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // Pending renewal — both null when no renewal is scheduled
    public DateTime? RenewalStartDate { get; set; }
    public DateTime? RenewalEndDate { get; set; }

    // Set to true when the SuperAdmin approves the payment for this period
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? PaidById { get; set; }

    public bool IsActive { get; set; } = true;

    // Archived records are kept for history; they are no longer the current plan
    public bool IsArchived { get; set; }

    // Tracks which expiry alert was last sent (1=-5d, 2=-1d, 3=0d, 4=+1d, 5=suspended)
    public byte? AlertLevel { get; set; }

    public Guid AssignedById { get; set; }

    // Navigation
    public Plan? Plan { get; set; }
    public Building? Building { get; set; }
    public ApplicationUser? AssignedBy { get; set; }
    public ApplicationUser? PaidBy { get; set; }
    public ICollection<BuildingPlanPayment> Payments { get; set; } = new List<BuildingPlanPayment>();
}
