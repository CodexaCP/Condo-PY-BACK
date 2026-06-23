using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Infrastructure.Services;

public class AccessScopeService(ICondoDbContext dbContext, ITenantContext tenantContext) : IAccessScopeService
{
    public bool IsSuperAdmin => tenantContext.IsSuperAdmin;
    public bool IsCompanyAdmin => tenantContext.IsCompanyAdmin;
    public Guid? CompanyId => tenantContext.CompanyId;
    public Guid? CondominiumId => tenantContext.CondominiumId;

    public async Task<HashSet<Guid>> GetAccessibleBuildingIdsAsync(CancellationToken cancellationToken)
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
