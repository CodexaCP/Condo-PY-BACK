namespace Condo.Application.Models;

// ── Requests ──────────────────────────────────────────────────────────────────

public class PlanCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
    public int GracePeriodDays { get; set; } = 5;
}

public class PlanUpdateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
    public int GracePeriodDays { get; set; }
    public bool IsActive { get; set; }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class PlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
    public int GracePeriodDays { get; set; }
    public bool IsActive { get; set; }
    public bool IsAssigned { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int AssignedBuildingsCount { get; set; }
}

public class PlanCloneResult
{
    public Guid NewPlanId { get; set; }
    public string NewPlanName { get; set; } = string.Empty;
}
