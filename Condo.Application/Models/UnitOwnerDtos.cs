namespace Condo.Application.Models;

public class UnitOwnerDto
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; }
}

public class CreateUnitOwnerRequest
{
    public Guid UnitId { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly StartDate { get; set; }
}
