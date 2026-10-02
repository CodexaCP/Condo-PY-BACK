using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Estado del "Marketplace de espacios temporales" en un edificio. Esta disponible solo si el SuperAdmin lo habilito
/// (Building.MarketplaceEnabled) y el plan vigente del edificio lo incluye (Plan.IncludesMarketplace). Si el edificio
/// pasa a un plan que no lo incluye, el modulo queda apagado pero conserva sus datos y su interruptor.
/// La restriccion por plan vencido (solo lectura / bloqueo) la aplica aparte PlanRestrictionMiddleware.
/// </summary>
public sealed record MarketplaceModuleState(bool BuildingFound, bool Enabled, bool PlanIncludesModule)
{
    public bool IsAvailable => BuildingFound && Enabled && PlanIncludesModule;
}

public class MarketplaceModuleGate(ICondoDbContext dbContext)
{
    public const string DisabledCode = "marketplace_module_disabled";
    public const string PlanNotIncludedCode = "marketplace_plan_not_included";
    public const string ForbiddenCode = "marketplace_forbidden";

    public const string DisabledMessage =
        "El Marketplace no está habilitado para este edificio. Pedí su activación al administrador de CondoPY.";
    public const string PlanNotIncludedMessage =
        "El plan actual del edificio no incluye el Marketplace.";
    public const string AdminOnlyMessage =
        "Solo el SuperAdmin administra el Marketplace por edificio.";

    public async Task<MarketplaceModuleState> GetStateAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var enabled = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => (bool?)x.MarketplaceEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        if (enabled is null)
        {
            return new MarketplaceModuleState(false, false, false);
        }

        var includes = (await PlansIncludingModuleAsync([buildingId], cancellationToken)).Contains(buildingId);
        return new MarketplaceModuleState(true, enabled.Value, includes);
    }

    /// <summary>Edificios, de los indicados, cuyo plan vigente (el no archivado) incluye el modulo.</summary>
    public async Task<HashSet<Guid>> PlansIncludingModuleAsync(
        IReadOnlyCollection<Guid> buildingIds, CancellationToken cancellationToken)
    {
        if (buildingIds.Count == 0)
        {
            return [];
        }

        return (await dbContext.BuildingPlans
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived
                        && buildingIds.Contains(x.BuildingId)
                        && x.Plan != null && !x.Plan.IsDeleted && x.Plan.IncludesMarketplace)
            .Select(x => x.BuildingId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
