namespace Condo.Application.Models;

public class UnitUpsertRequest
{
    public Guid BuildingId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UnitDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public bool IsActive { get; set; }
}
