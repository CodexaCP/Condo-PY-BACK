namespace Condo.Application.Models;

public class UserUpsertRequest
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<Guid> BuildingIds { get; set; } = Array.Empty<Guid>();
}

public class BuildingCapacityItem
{
    public Guid BuildingId { get; set; }
    public int BuildingManagerCount { get; set; }
    public int CompanyOperatorCount { get; set; }
}

public class BuildingCapacityResponse
{
    public IReadOnlyList<BuildingCapacityItem> Items { get; set; } = Array.Empty<BuildingCapacityItem>();
}

public class UserDto
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public IReadOnlyList<Guid> BuildingIds { get; set; } = Array.Empty<Guid>();
}
