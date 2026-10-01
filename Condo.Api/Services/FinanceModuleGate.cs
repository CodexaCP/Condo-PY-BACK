using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Estado del modulo "Finanzas del edificio" en un edificio. Esta disponible solo si el SuperAdmin lo habilito
/// (Building.FinanceModuleEnabled) y el plan vigente del edificio lo incluye (Plan.IncludesFinanceModule). Si el
/// edificio pasa a un plan que no lo incluye, el modulo queda apagado pero conserva sus datos y su interruptor.
/// La restriccion por plan vencido (solo lectura / bloqueo) la aplica aparte PlanRestrictionMiddleware.
/// </summary>
public sealed record FinanceModuleState(bool BuildingFound, bool Enabled, bool PlanIncludesModule)
{
    public bool IsAvailable => BuildingFound && Enabled && PlanIncludesModule;
}

public class FinanceModuleGate(ICondoDbContext dbContext)
{
    public const string DisabledCode = "finance_module_disabled";
    public const string PlanNotIncludedCode = "finance_plan_not_included";
    public const string ForbiddenCode = "finance_forbidden";

    public const string DisabledMessage =
        "El módulo Finanzas del edificio no está habilitado para este edificio. Pedí su activación al administrador de CondoPY.";
    public const string PlanNotIncludedMessage =
        "El plan actual del edificio no incluye el módulo Finanzas del edificio.";
    public const string RoleNotAllowedMessage =
        "Tu rol no tiene acceso al módulo Finanzas del edificio.";
    public const string ReadOnlyRoleMessage =
        "Solo el Administrador de empresa o el SuperAdmin pueden modificar la configuración financiera.";

    public async Task<FinanceModuleState> GetStateAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var enabled = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => (bool?)x.FinanceModuleEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        if (enabled is null)
        {
            return new FinanceModuleState(false, false, false);
        }

        var includes = (await PlansIncludingModuleAsync([buildingId], cancellationToken)).Contains(buildingId);
        return new FinanceModuleState(true, enabled.Value, includes);
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
                        && x.Plan != null && !x.Plan.IsDeleted && x.Plan.IncludesFinanceModule)
            .Select(x => x.BuildingId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
