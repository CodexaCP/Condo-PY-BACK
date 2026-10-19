using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Libros contables del edificio (Centro de configuracion): titular del libro (consorcio o empresa administradora), sus datos, el contador
/// y la rubrica. La ven el SuperAdmin, el Administrador y el Operador; la editan solo SuperAdmin y Administrador. Exige Finanzas del
/// edificio habilitado y en el plan. Un edificio ajeno responde 404 y un rol sin permiso, 403. Cada cambio queda en el historial del Centro.
/// </summary>
[ApiController]
[Authorize]
[Route("api/building-config/{buildingId:guid}/books")]
public class BuildingConfigBooksController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    AccountingBooksService books) : ControllerBase
{
    private static ConfigSectionDefinition Section => ConfigSections.Find(ConfigSectionKeys.Books)!;

    private bool CanEdit => ConfigSections.CanEdit(Section, tenantContext.Role);

    [HttpGet]
    public async Task<ActionResult<AccountingBooksDto>> Get(Guid buildingId, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section.ViewRoles, cancellationToken);
        if (denied is not null) return denied;

        var building = await LoadBuildingAsync(buildingId, cancellationToken);
        if (building is null) return NotFound();

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.IsAvailable)
        {
            return Ok(new AccountingBooksDto { BuildingId = buildingId, FinanceAvailable = false, CanEdit = CanEdit });
        }

        return Ok(await books.GetAsync(building, CanEdit, cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<AccountingBooksDto>> Update(
        Guid buildingId, [FromBody] UpdateAccountingBooksRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireAccessAsync(buildingId, Section.EditRoles, cancellationToken);
        if (denied is not null) return denied;

        var building = await LoadBuildingAsync(buildingId, cancellationToken);
        if (building is null) return NotFound();
        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue) return BadRequest("El edificio no tiene empresa asignada.");

        var state = await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken);
        if (!state.Enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.DisabledCode, message = FinanceModuleGate.DisabledMessage });
        }

        if (!state.PlanIncludesModule)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = FinanceModuleGate.PlanNotIncludedCode, message = FinanceModuleGate.PlanNotIncludedMessage });
        }

        var (result, error) = await books.UpdateAsync(building, request, companyId.Value, cancellationToken);
        if (error is null) return Ok(result);

        return error.StartsWith("Otro cambio", StringComparison.Ordinal) ? Conflict(error) : BadRequest(error);
    }

    private async Task<Building?> LoadBuildingAsync(Guid buildingId, CancellationToken cancellationToken) =>
        await dbContext.Buildings.AsNoTracking().Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);

    private async Task<ActionResult?> RequireAccessAsync(Guid buildingId, string[] roles, CancellationToken cancellationToken)
    {
        if (!roles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "building_config_forbidden",
                message = "Tu rol no tiene acceso a esta parte del Centro de configuración."
            });
        }

        if (buildingId == Guid.Empty || !await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken))
        {
            return NotFound();
        }

        return null;
    }
}
