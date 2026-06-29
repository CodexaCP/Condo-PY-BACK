namespace Condo.Application.Models;

public class MyUnitDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string BuildingCode { get; set; } = string.Empty;
    public string BuildingAddress { get; set; } = string.Empty;
    public Guid? CondominiumId { get; set; }
    public string? CondominiumName { get; set; }
    public string RelationRole { get; set; } = string.Empty;  // "Owner" | "Resident"
    public bool IsPrimary { get; set; }
}
