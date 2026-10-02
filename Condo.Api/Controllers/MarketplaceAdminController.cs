using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Marketplace de espacios temporales, fase 1: interruptor por edificio, comision de gestion y datos para
/// transferir. Todo es del SuperAdmin; el resto de los roles no administra el modulo.
/// </summary>
[ApiController]
[Authorize]
[Route("api/marketplace/admin")]
public class MarketplaceAdminController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext,
    MarketplaceModuleGate gate) : ControllerBase
{
    private const int TransferInfoMaxLength = 1000;

    [HttpGet("buildings")]
    public async Task<ActionResult<IReadOnlyList<MarketplaceAdminBuildingDto>>> GetBuildings(CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return AdminOnly();
        }

        return Ok(await BuildRowsAsync(null, cancellationToken));
    }

    [HttpPut("buildings/{buildingId:guid}")]
    public async Task<ActionResult<MarketplaceAdminBuildingDto>> Update(
        Guid buildingId, [FromBody] MarketplaceAdminUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return AdminOnly();
        }

        var error = Validate(request);
        if (error is not null)
        {
            return BadRequest(error);
        }

        var building = await dbContext.Buildings.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, cancellationToken);
        if (building is null)
        {
            return NotFound();
        }

        // Encender exige un plan que lo incluya; apagar siempre se puede y conserva la configuracion y los datos.
        if (request.Enabled && !building.MarketplaceEnabled)
        {
            var state = await gate.GetStateAsync(buildingId, cancellationToken);
            if (!state.PlanIncludesModule)
            {
                return BadRequest("El plan vigente del edificio no incluye el Marketplace. Asigná un plan que lo incluya antes de habilitarlo.");
            }
        }

        building.MarketplaceEnabled = request.Enabled;
        building.MarketplaceCommissionPercent = request.CommissionPercent;
        building.MarketplaceTransferInfo = string.IsNullOrWhiteSpace(request.TransferInfo) ? null : request.TransferInfo.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok((await BuildRowsAsync(buildingId, cancellationToken)).Single());
    }

    private static string? Validate(MarketplaceAdminUpdateRequest request)
    {
        if (request.CommissionPercent < 0m || request.CommissionPercent > 100m)
        {
            return "La comisión debe estar entre 0 y 100.";
        }

        if (decimal.Round(request.CommissionPercent, 2) != request.CommissionPercent)
        {
            return "La comisión admite como máximo 2 decimales.";
        }

        if (request.TransferInfo is not null && request.TransferInfo.Trim().Length > TransferInfoMaxLength)
        {
            return $"Los datos para transferir no pueden superar los {TransferInfoMaxLength} caracteres.";
        }

        return null;
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = MarketplaceModuleGate.ForbiddenCode, message = MarketplaceModuleGate.AdminOnlyMessage });

    // Filas del listado del SuperAdmin (todos los edificios, o uno solo).
    private async Task<List<MarketplaceAdminBuildingDto>> BuildRowsAsync(Guid? onlyBuildingId, CancellationToken cancellationToken)
    {
        var buildingsQuery = dbContext.Buildings.AsNoTracking().Where(x => !x.IsDeleted);
        if (onlyBuildingId.HasValue)
        {
            buildingsQuery = buildingsQuery.Where(x => x.Id == onlyBuildingId.Value);
        }

        var buildings = await buildingsQuery
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.MarketplaceEnabled,
                x.MarketplaceCommissionPercent,
                x.MarketplaceTransferInfo,
                CompanyName = x.Company != null
                    ? x.Company.Name
                    : (x.Condominium != null && x.Condominium.Company != null ? x.Condominium.Company.Name : string.Empty),
                CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty
            })
            .ToListAsync(cancellationToken);

        var buildingIds = buildings.Select(x => x.Id).ToList();

        var plans = await dbContext.BuildingPlans.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived && x.Plan != null && buildingIds.Contains(x.BuildingId))
            .Select(x => new
            {
                x.BuildingId,
                x.PlanId,
                PlanName = x.Plan!.Name,
                Includes = x.Plan.IncludesMarketplace,
                x.EndDate,
                Grace = x.Plan.GracePeriodDays
            })
            .ToListAsync(cancellationToken);
        var planByBuilding = plans.GroupBy(x => x.BuildingId).ToDictionary(g => g.Key, g => g.ToList());

        var today = DateTime.UtcNow.Date;

        return buildings.Select(b =>
        {
            planByBuilding.TryGetValue(b.Id, out var buildingPlans);
            var current = buildingPlans?.OrderByDescending(x => x.EndDate).FirstOrDefault();
            var includes = buildingPlans?.Any(x => x.Includes) ?? false;

            return new MarketplaceAdminBuildingDto
            {
                BuildingId = b.Id,
                BuildingName = b.Name,
                CompanyName = b.CompanyName,
                CondominiumName = b.CondominiumName,
                PlanId = current?.PlanId,
                PlanName = current?.PlanName ?? string.Empty,
                PlanStatus = current is null ? string.Empty : PlanAccessPolicy.GetStatusName(false, current.EndDate, current.Grace, today),
                PlanIncludesMarketplace = includes,
                ModuleEnabled = b.MarketplaceEnabled,
                ModuleAvailable = b.MarketplaceEnabled && includes,
                CommissionPercent = b.MarketplaceCommissionPercent,
                TransferInfo = b.MarketplaceTransferInfo ?? string.Empty
            };
        }).ToList();
    }
}
