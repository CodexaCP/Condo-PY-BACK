namespace Condo.Application.Abstractions;

public interface IAccessScopeService
{
    bool IsSuperAdmin { get; }
    bool IsCompanyAdmin { get; }
    Guid? CompanyId { get; }
    Guid? CondominiumId { get; }
    Task<HashSet<Guid>> GetAccessibleBuildingIdsAsync(CancellationToken cancellationToken);
    Task<bool> CanAccessBuildingAsync(Guid buildingId, CancellationToken cancellationToken);
    Task<bool> CanManageCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}
