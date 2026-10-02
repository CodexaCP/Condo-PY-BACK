using Condo.Application.Abstractions;

namespace Condo.Tests.Support;

internal sealed class FakeTenantContext : ITenantContext
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public Guid UserId { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "test@test.local";
    public string Role { get; set; } = "Owner";
    public bool IsAuthenticated { get; set; } = true;
    public bool IsSuperAdmin => string.Equals(Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
    public bool IsCompanyAdmin => string.Equals(Role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase);
}

// Alcance del personal: los edificios que se le asignan a mano.
internal sealed class FakeAccessScope(FakeTenantContext tenant) : IAccessScopeService
{
    public HashSet<Guid> Buildings { get; } = [];

    public bool IsSuperAdmin => tenant.IsSuperAdmin;
    public bool IsCompanyAdmin => tenant.IsCompanyAdmin;
    public Guid? CompanyId => tenant.CompanyId;
    public Guid? CondominiumId => tenant.CondominiumId;
    public bool HasFullCompanyScope => tenant.IsSuperAdmin || tenant.IsCompanyAdmin;

    public Task<HashSet<Guid>> GetAccessibleBuildingIdsAsync(CancellationToken cancellationToken) => Task.FromResult(Buildings);
    public Task<HashSet<Guid>> GetAllAccessibleBuildingIdsAsync(CancellationToken cancellationToken) => Task.FromResult(Buildings);

    public Task<bool> CanAccessBuildingAsync(Guid buildingId, CancellationToken cancellationToken) =>
        Task.FromResult(tenant.IsSuperAdmin || Buildings.Contains(buildingId));

    public Task<bool> CanManageCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(tenant.IsSuperAdmin || tenant.CompanyId == companyId);
}
