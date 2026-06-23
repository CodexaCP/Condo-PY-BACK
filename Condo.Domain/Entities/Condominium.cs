using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Condominium : BaseEntity
{
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    public Company? Company { get; set; }
    public ICollection<Building> Buildings { get; set; } = new List<Building>();
}
