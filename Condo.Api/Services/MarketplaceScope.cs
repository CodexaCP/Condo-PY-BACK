using Condo.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Edificio ya validado para operar el marketplace: su empresa y desde que lado entra el usuario.</summary>
public sealed record MarketplaceBuildingContext(Guid BuildingId, Guid CompanyId, bool IsStaff, bool IsMember);

public sealed record MarketplaceAccess(MarketplaceBuildingContext? Context, string? ErrorCode, string? Message)
{
    public bool Allowed => Context is not null;

    // Un edificio inexistente, ajeno o de otra empresa responde igual: no se revela que existe.
    public bool IsNotFound => ErrorCode == MarketplaceScope.NotFoundCode;
}

public sealed record MarketplaceOwnedUnit(Guid UnitId, string Code, string Floor);

/// <summary>
/// Aislamiento del marketplace: de que edificios puede ver o publicar el usuario. NUNCA se confia en el edificio que
/// manda el cliente: se valida contra la relacion real del usuario (propietario o residente de una unidad del edificio, o
/// personal con ese edificio en su alcance), contra su empresa y contra que el modulo este habilitado. El filtro por
/// edificio es estricto: se opera con UN edificio a la vez, nunca con "todos los mios".
/// </summary>
public class MarketplaceScope(
    ICondoDbContext dbContext,
    ITenantContext tenantContext,
    IAccessScopeService accessScope,
    MarketplaceModuleGate gate)
{
    public const string NotFoundCode = "marketplace_not_found";
    public const string BuildingRequiredCode = "marketplace_building_required";

    private static readonly string[] StaffRoles = ["SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager"];

    private bool IsStaffRole => StaffRoles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Edificios de los que el usuario es propietario o residente vigente. Se combinan ambos vinculos sin depender del
    /// rol unico de la cuenta: una misma persona puede ser propietaria de una unidad y residente de otra.
    /// </summary>
    public async Task<HashSet<Guid>> GetMemberBuildingIdsAsync(CancellationToken cancellationToken)
    {
        var userId = tenantContext.UserId;
        var result = new HashSet<Guid>();

        result.UnionWith(await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OwnerId == userId
                        && x.Unit != null && !x.Unit.IsDeleted
                        && x.Unit.Building != null && !x.Unit.Building.IsDeleted)
            .Select(x => x.Unit!.BuildingId)
            .ToListAsync(cancellationToken));

        result.UnionWith(await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                        && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId == userId
                        && x.Unit != null && !x.Unit.IsDeleted
                        && x.Unit.Building != null && !x.Unit.Building.IsDeleted)
            .Select(x => x.Unit!.BuildingId)
            .ToListAsync(cancellationToken));

        return result;
    }

    /// <summary>
    /// Valida que el usuario pueda operar el marketplace en ese edificio y devuelve su empresa. Falla si el edificio no
    /// existe o el usuario no tiene relacion con el (mismo mensaje: no se revela), si es de otra empresa, o si el modulo
    /// esta apagado o el plan no lo incluye.
    /// </summary>
    public async Task<MarketplaceAccess> ResolveBuildingAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        if (buildingId == Guid.Empty)
        {
            return new MarketplaceAccess(null, BuildingRequiredCode, "El edificio es obligatorio.");
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => new
            {
                x.Id,
                CompanyId = x.CompanyId ?? (x.Condominium != null ? x.Condominium.CompanyId : null)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (building is null || building.CompanyId is null)
        {
            return NotFound();
        }

        var isStaff = IsStaffRole && await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken);
        var isMember = (await GetMemberBuildingIdsAsync(cancellationToken)).Contains(buildingId);
        if (!isStaff && !isMember)
        {
            return NotFound();
        }

        // Otra empresa: el SuperAdmin no esta atado a una empresa; el resto solo opera dentro de la suya.
        if (!tenantContext.IsSuperAdmin
            && tenantContext.CompanyId.HasValue
            && tenantContext.CompanyId.Value != building.CompanyId.Value)
        {
            return NotFound();
        }

        var state = await gate.GetStateAsync(buildingId, cancellationToken);
        if (!state.Enabled)
        {
            return new MarketplaceAccess(null, MarketplaceModuleGate.DisabledCode, MarketplaceModuleGate.DisabledMessage);
        }

        if (!state.PlanIncludesModule)
        {
            return new MarketplaceAccess(null, MarketplaceModuleGate.PlanNotIncludedCode, MarketplaceModuleGate.PlanNotIncludedMessage);
        }

        return new MarketplaceAccess(
            new MarketplaceBuildingContext(buildingId, building.CompanyId.Value, isStaff, isMember), null, null);
    }

    /// <summary>
    /// Unidades del edificio de las que el usuario es propietario PRINCIPAL (las unicas que puede publicar). La relacion
    /// se consulta en el momento: si deja de ser el principal, o pasa a serlo otra persona, el permiso cambia solo.
    /// </summary>
    public async Task<List<MarketplaceOwnedUnit>> GetPrimaryOwnedUnitsAsync(Guid userId, Guid buildingId, CancellationToken cancellationToken) =>
        await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsPrimary && x.OwnerId == userId
                        && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId)
            .OrderBy(x => x.Unit!.Code)
            .Select(x => new MarketplaceOwnedUnit(x.UnitId, x.Unit!.Code, x.Unit.Floor))
            .ToListAsync(cancellationToken);

    public async Task<bool> IsPrimaryOwnerOfUnitAsync(Guid userId, Guid unitId, CancellationToken cancellationToken) =>
        await dbContext.UnitOwners
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.IsPrimary && x.OwnerId == userId && x.UnitId == unitId
                           && x.Unit != null && !x.Unit.IsDeleted, cancellationToken);

    private static MarketplaceAccess NotFound() =>
        new(null, NotFoundCode, "No se encontró el edificio.");
}
