using System.Security.Claims;
using Condo.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Condo.Infrastructure.Services;

public class JwtTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid? CompanyId => ReadNullableGuidClaim("companyId");
    public Guid? CondominiumId => ReadNullableGuidClaim("condominiumId");
    public Guid UserId => ReadGuidClaim(ClaimTypes.NameIdentifier);
    public string Email => ReadStringClaim(ClaimTypes.Email);
    public string Role => ReadStringClaim(ClaimTypes.Role);
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    public bool IsSuperAdmin => string.Equals(Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
    public bool IsCompanyAdmin => string.Equals(Role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase);

    private Guid ReadGuidClaim(string claimType)
    {
        var raw = ReadStringClaim(claimType);
        return Guid.TryParse(raw, out var value) ? value : Guid.Empty;
    }

    private Guid? ReadNullableGuidClaim(string claimType)
    {
        var raw = ReadStringClaim(claimType);
        return Guid.TryParse(raw, out var value) ? value : null;
    }

    private string ReadStringClaim(string claimType) =>
        httpContextAccessor.HttpContext?.User?.FindFirstValue(claimType) ?? string.Empty;
}
