namespace Condo.Application.Models;

public class AssignResidentToUnitRequest
{
    public Guid UnitId { get; set; }
    public Guid ResidentId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EndDate { get; set; }
}

public class UnitResidentDto
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ResidentId { get; set; }
    public string ResidentName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public class EndResidencyRequest
{
    public DateOnly? EndDate { get; set; }
}
