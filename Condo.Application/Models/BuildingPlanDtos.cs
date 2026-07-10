namespace Condo.Application.Models;

// ── Requests ──────────────────────────────────────────────────────────────────

public class BuildingPlanAssignRequest
{
    public Guid PlanId { get; set; }
    public Guid BuildingId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class BuildingPlanBulkAssignRequest
{
    public Guid PlanId { get; set; }
    // "Company" | "Condominium"
    public string Scope { get; set; } = string.Empty;
    public Guid ScopeEntityId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class BuildingPlanSetRenewalRequest
{
    public DateTime RenewalStartDate { get; set; }
    public DateTime RenewalEndDate { get; set; }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class BuildingPlanDto
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public bool PlanIsDefault { get; set; }
    public decimal PlanPrice { get; set; }
    public string PlanBillingCycle { get; set; } = string.Empty;
    public int PlanGracePeriodDays { get; set; }

    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    public string AssignmentScope { get; set; } = string.Empty;
    public Guid ScopeEntityId { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public DateTime? RenewalStartDate { get; set; }
    public DateTime? RenewalEndDate { get; set; }

    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaidByFullName { get; set; }

    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }

    public string AssignedByFullName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    // Computed — derived at query time, never stored
    public string Status { get; set; } = string.Empty;
    public int DaysUntilExpiry { get; set; }

    public bool HasPendingPayment { get; set; }
}

public class BuildingPlanSummaryDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool HasRenewal { get; set; }
    public bool IsPaid { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DaysUntilExpiry { get; set; }
}
