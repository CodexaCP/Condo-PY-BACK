using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class BuildingPlanPayment : BaseEntity
{
    public Guid BuildingPlanId { get; set; }

    // Denormalized for fast querying by scope
    public PlanAssignmentScope AssignmentScope { get; set; }
    public Guid ScopeEntityId { get; set; }
    public Guid CompanyId { get; set; }

    public decimal DeclaredAmount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string ComprobanteUrl { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    public BuildingPlanPaymentStatus Status { get; set; } = BuildingPlanPaymentStatus.Pending;

    public string RejectionReason { get; set; } = string.Empty;
    public Guid SubmittedById { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Navigation
    public BuildingPlan? BuildingPlan { get; set; }
    public ApplicationUser? SubmittedBy { get; set; }
    public ApplicationUser? ReviewedBy { get; set; }
    public Company? Company { get; set; }
}
