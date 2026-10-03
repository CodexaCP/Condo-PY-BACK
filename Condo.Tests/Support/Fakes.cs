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

// Envio de push de mentira: no manda nada, solo permite armar el PushDispatcher en las pruebas.
internal sealed class NoopPushSender : IPushNotificationSender
{
    public Task SendAsync(string deviceToken, string title, string body, IDictionary<string, string>? data, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

// Mora de mentira: se indica a mano que unidades estan en mora.
internal sealed class StubOverdueService : Condo.Api.Services.IUnitOverdueService
{
    public HashSet<Guid> OverdueUnitIds { get; } = [];

    public Task<bool> IsUnitOverdueAsync(Guid unitId, CancellationToken ct) => Task.FromResult(OverdueUnitIds.Contains(unitId));

    public Task<HashSet<Guid>> GetOverdueUnitIdsByBuildingAsync(IReadOnlyCollection<Guid> buildingIds, CancellationToken ct) =>
        Task.FromResult(OverdueUnitIds.ToHashSet());
}

// Entorno web de mentira: solo importa WebRootPath (de ahi se leen las imagenes subidas, como el comprobante de transferencia).
internal sealed class StubWebHostEnvironment(string webRootPath) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Condo.Tests";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string WebRootPath { get; set; } = webRootPath;
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = webRootPath;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
