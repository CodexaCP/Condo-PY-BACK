namespace Condo.Application.Models;

public class ClaimDto
{
    public Guid Id { get; set; }
    public Guid CondominiumId { get; set; }
    public string CondominiumName { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string ResolvedByUserName { get; set; } = string.Empty;
}

public class ClaimCreateRequest
{
    public Guid UnitId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class ClaimStatusUpdateRequest
{
    public string Status { get; set; } = string.Empty;
}
