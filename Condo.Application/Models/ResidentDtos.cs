namespace Condo.Application.Models;

public class ResidentUpsertRequest
{
    public Guid? CompanyId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ResidentDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; }
}
