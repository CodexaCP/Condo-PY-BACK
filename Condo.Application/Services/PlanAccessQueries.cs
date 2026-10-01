using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Application.Services;

public static class PlanAccessQueries
{
    /// <summary>
    /// Etapa de acceso de cada edificio segun su plan vigente (el no archivado). Un edificio sin plan
    /// no aparece en el resultado y debe tratarse como <see cref="PlanAccessPhase.Normal"/>.
    /// </summary>
    public static async Task<Dictionary<Guid, PlanAccessPhase>> LoadPhasesAsync(
        ICondoDbContext dbContext, IReadOnlyCollection<Guid> buildingIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, PlanAccessPhase>();
        if (buildingIds.Count == 0) return result;

        var today = DateTime.UtcNow.Date;

        var plans = await dbContext.BuildingPlans
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived && buildingIds.Contains(x.BuildingId))
            .Select(x => new { x.BuildingId, x.EndDate, Grace = x.Plan != null ? x.Plan.GracePeriodDays : 5 })
            .ToListAsync(cancellationToken);

        foreach (var group in plans.GroupBy(x => x.BuildingId))
        {
            // Si por algun motivo hubiera mas de un plan vigente, manda el menos restrictivo.
            result[group.Key] = group
                .Select(x => PlanAccessPolicy.GetPhase(x.EndDate, x.Grace, today))
                .Min();
        }

        return result;
    }
}
