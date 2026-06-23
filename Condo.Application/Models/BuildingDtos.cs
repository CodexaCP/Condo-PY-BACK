namespace Condo.Application.Models;

public class BuildingUpsertRequest
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
}

public class BuildingDto
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string CondominiumName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
}
