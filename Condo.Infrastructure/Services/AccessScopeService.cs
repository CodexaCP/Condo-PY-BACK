using Condo.Application.Abstractions;
using Condo.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Condo.Infrastructure.Services;

public class AccessScopeService(ICondoDbContext dbContext, ITenantContext tenantContext) : IAccessScopeService
{
    public bool IsSuperAdmin => tenantContext.IsSuperAdmin;
    public bool IsCompanyAdmin => tenantContext.IsCompanyAdmin;
    public Guid? CompanyId => tenantContext.CompanyId;
    public Guid? CondominiumId => tenantContext.CondominiumId;

    public bool HasFullCompanyScope =>
        tenantContext.IsSuperAdmin ||
        (tenantContext.IsCompanyAdmin && tenantContext.CompanyId.HasValue && !tenantContext.CondominiumId.HasValue);

    private HashSet<Guid>? _allBuildingIds;
    private HashSet<Guid>? _workableBuildingIds;

    // Los unicos roles a los que se les aplica la restriccion por plan vencido.
    private bool IsPlanRestrictedRole =>
        tenantContext.IsCompanyAdmin ||
        string.Equals(tenantContext.Role, "CompanyOperator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    public async Task<HashSet<Guid>> GetAccessibleBuildingIdsAsync(CancellationToken cancellationToken)
    {
        if (_workableBuildingIds is not null) return _workableBuildingIds;

        var all = await GetAllAccessibleBuildingIdsAsync(cancellationToken);
        if (!IsPlanRestrictedRole || all.Count == 0)
            return _workableBuildingIds = all;

        var phases = await PlanAccessQueries.LoadPhasesAsync(dbContext, all, cancellationToken);
        return _workableBuildingIds = all
            .Where(id => !phases.TryGetValue(id, out var phase) || phase != PlanAccessPhase.Blocked)
            .ToHashSet();
    }

    public async Task<HashSet<Guid>> GetAllAccessibleBuildingIdsAsync(CancellationToken cancellationToken) =>
        _allBuildingIds ??= await ResolveAccessibleBuildingIdsAsync(cancellationToken);

    private async Task<HashSet<Guid>> ResolveAccessibleBuildingIdsAsync(CancellationToken cancellationToken)
    {
        if (tenantContext.IsSuperAdmin)
        {
            return (await dbContext.Buildings
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        if (tenantContext.IsCompanyAdmin && tenantContext.CompanyId.HasValue)
        {
            if (tenantContext.CondominiumId.HasValue)
            {
                var condId = tenantContext.CondominiumId.Value;
                return (await dbContext.Buildings
                    .AsNoTracking()
                    .Where(x => !x.IsDeleted && x.CondominiumId == condId)
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();
            }

            var cid = tenantContext.CompanyId.Value;
            return (await dbContext.Buildings
                .AsNoTracking()
                .Where(x => !x.IsDeleted && (x.CompanyId == cid || (x.Condominium != null && x.Condominium.CompanyId == cid)))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        if (!tenantContext.CompanyId.HasValue)
        {
            return [];
        }

        return (await dbContext.UserBuildingAccesses
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                x.CompanyId == tenantContext.CompanyId.Value &&
                x.ApplicationUserId == tenantContext.UserId &&
                x.Building != null &&
                !x.Building.IsDeleted)
            .Select(x => x.BuildingId)
            .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    public async Task<bool> CanAccessBuildingAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        if (tenantContext.IsSuperAdmin)
        {
            return await dbContext.Buildings.AnyAsync(x => x.Id == buildingId && !x.IsDeleted, cancellationToken);
        }

        var accessibleBuildingIds = await GetAccessibleBuildingIdsAsync(cancellationToken);
        return accessibleBuildingIds.Contains(buildingId);
    }

    public async Task<bool> CanManageCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (tenantContext.IsSuperAdmin)
        {
            return await dbContext.Companies.AnyAsync(x => x.Id == companyId && !x.IsDeleted, cancellationToken);
        }

        return tenantContext.IsCompanyAdmin && tenantContext.CompanyId == companyId;
    }
}
