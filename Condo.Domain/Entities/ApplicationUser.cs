using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class ApplicationUser : BaseEntity
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }

    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }

    public bool IsResident { get; set; } = false;
    public UserRole Role { get; set; } = UserRole.Resident;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }

    public Company? Company { get; set; }
    public Condominium? Condominium { get; set; }
    public ICollection<UserBuildingAccess> BuildingAccesses { get; set; } = new List<UserBuildingAccess>();
}
