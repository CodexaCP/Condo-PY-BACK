namespace Condo.Application.Models;

// ── Requests ──────────────────────────────────────────────────────────────────

public class BuildingPlanPaymentCreateRequest
{
    public Guid BuildingPlanId { get; set; }
    public decimal DeclaredAmount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string? ComprobanteUrl { get; set; }
    public string? Reference { get; set; }
}

public class BuildingPlanPaymentRejectRequest
{
    public string RejectionReason { get; set; } = string.Empty;
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class BuildingPlanPaymentDto
{
    public Guid Id { get; set; }
    public Guid BuildingPlanId { get; set; }

    public string AssignmentScope { get; set; } = string.Empty;
    public Guid ScopeEntityId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public string BuildingName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public decimal DeclaredAmount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string ComprobanteUrl { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;

    public string SubmittedByFullName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public string? ReviewedByFullName { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
