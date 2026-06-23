namespace Condo.Application.Abstractions;

public interface ITenantContext
{
    Guid? CompanyId { get; }
    Guid? CondominiumId { get; }
    Guid UserId { get; }
    string Email { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
    bool IsCompanyAdmin { get; }
}
