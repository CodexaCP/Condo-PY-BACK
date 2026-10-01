using Condo.Api.Services;
using Condo.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Base de los controladores del modulo "Finanzas del edificio". Cada endpoint llama a
/// <see cref="RequireModuleAsync"/> con el edificio sobre el que opera: valida el rol, el alcance por edificio
/// (CanAccessBuildingAsync) y que el modulo este habilitado y incluido en el plan. Con el modulo apagado responde
/// 403 con { error, message }, el mismo formato que usa la restriccion por plan vencido.
/// </summary>
[ApiController]
[Authorize]
public abstract class FinanceControllerBase(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate) : ControllerBase
{
    private static readonly string[] FinanceRoles = ["SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager"];

    protected ICondoDbContext Db => dbContext;
    protected IAccessScopeService AccessScope => accessScope;
    protected ITenantContext Tenant => tenantContext;
    protected FinanceModuleGate Gate => gate;

    // Roles administrativos con acceso al modulo (el resto de los roles usa la app movil y no ve Finanzas).
    protected bool IsFinanceRole =>
        FinanceRoles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase);

    // Configurar (cuentas, plan de cuentas, fecha de arranque): SuperAdmin y Administrador de empresa.
    protected bool CanConfigure => tenantContext.IsSuperAdmin || tenantContext.IsCompanyAdmin;

    protected ObjectResult FinanceForbidden(string code, string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = code, message });

    /// <summary>
    /// Devuelve null si el usuario puede operar el modulo en el edificio; si no, la respuesta de error.
    /// <paramref name="hideMissingAccess"/>: en los endpoints por id un edificio ajeno responde 404 (igual que un id
    /// inexistente) para no revelar que existe.
    /// </summary>
    protected async Task<ActionResult?> RequireModuleAsync(
        Guid buildingId, bool write, CancellationToken cancellationToken, bool hideMissingAccess = false)
    {
        if (!IsFinanceRole)
        {
            return FinanceForbidden(FinanceModuleGate.ForbiddenCode, FinanceModuleGate.RoleNotAllowedMessage);
        }

        if (buildingId == Guid.Empty)
        {
            return BadRequest("El edificio es obligatorio.");
        }

        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken))
        {
            return hideMissingAccess ? NotFound() : Forbid();
        }

        var state = await gate.GetStateAsync(buildingId, cancellationToken);
        if (!state.BuildingFound)
        {
            return NotFound();
        }

        if (!state.Enabled)
        {
            return FinanceForbidden(FinanceModuleGate.DisabledCode, FinanceModuleGate.DisabledMessage);
        }

        if (!state.PlanIncludesModule)
        {
            return FinanceForbidden(FinanceModuleGate.PlanNotIncludedCode, FinanceModuleGate.PlanNotIncludedMessage);
        }

        if (write && !CanConfigure)
        {
            return FinanceForbidden(FinanceModuleGate.ForbiddenCode, FinanceModuleGate.ReadOnlyRoleMessage);
        }

        return null;
    }

    // Empresa efectiva del edificio (la propia o la de su condominio), como en los gastos del edificio.
    protected async Task<Guid?> ResolveCompanyIdAsync(Guid buildingId, CancellationToken cancellationToken) =>
        await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => x.CompanyId ?? (x.Condominium != null ? x.Condominium.CompanyId : null))
            .FirstOrDefaultAsync(cancellationToken);

    protected const string NoCompanyMessage =
        "El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de configurar las finanzas.";

    // Guarda y traduce la violacion de un indice unico (alta o edicion simultanea) en un 409 claro.
    protected async Task<ActionResult?> SaveOrConflictAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            return Conflict("No se pudo guardar porque otro cambio se hizo al mismo tiempo (por ejemplo, un nombre o código repetido). Actualizá la pantalla y reintentá.");
        }
    }
}
