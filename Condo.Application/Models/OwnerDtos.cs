namespace Condo.Application.Models;

public class OwnerUpsertRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsResident { get; set; } = false;
    public bool IsActive { get; set; } = true;
}

public class OwnerDto
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? PhonePrefix { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsResident { get; set; }
    public bool IsActive { get; set; }
}
