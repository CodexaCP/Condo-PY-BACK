using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class Resident : CompanyScopedEntity
{
    public string FullName { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public ICollection<UnitResident> UnitResidents { get; set; } = new List<UnitResident>();
}
