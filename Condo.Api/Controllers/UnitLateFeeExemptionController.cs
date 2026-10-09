using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Exoneracion de mora de una unidad (Centro de configuracion, politica de mora): mientras este marcada, la mora automatica no corre para
/// esa unidad. No borra la mora que ya tenia. Solo SuperAdmin y Administrador de empresa; el motivo es obligatorio y quedan guardados quien,
/// cuando y por que, ademas de la entrada en el historial del edificio.
/// </summary>
[ApiController]
[Authorize]
[Route("api/units/{id:guid}/late-fee-exemption")]
public class UnitLateFeeExemptionController(
    ICondoDbContext dbContext, IAccessScopeService accessScope, ITenantContext tenantContext) : ControllerBase
{
    private const int ReasonMaxLength = 300;

    [HttpPut]
    public async Task<ActionResult<UnitDto>> Set(Guid id, [FromBody] UnitLateFeeExemptionRequest request, CancellationToken cancellationToken)
    {
        if (!ConfigSections.CanEdit(ConfigSections.Find(ConfigSectionKeys.LateFee)!, tenantContext.Role))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "building_config_forbidden",
                message = "Solo el SuperAdmin o el Administrador de empresa pueden exonerar una unidad de la mora."
            });
        }

        var unit = await dbContext.Units.Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (unit is null || !await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return NotFound();
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (request.Exempt)
        {
            if (reason.Length == 0) return BadRequest("El motivo de la exoneración es obligatorio.");
            if (reason.Length > ReasonMaxLength) return BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres.");
        }

        if (unit.LateFeeExempt != request.Exempt || (request.Exempt && unit.LateFeeExemptReason != reason))
        {
            var changes = new List<ConfigChange>
            {
                new("lateFeeExempt", "Exonerada de mora", unit.LateFeeExempt ? "Sí" : "No", request.Exempt ? "Sí" : "No")
            };
            if (request.Exempt)
            {
                changes.Add(new ConfigChange("lateFeeExemptReason", "Motivo", unit.LateFeeExemptReason, reason));
            }

            new ConfigAuditWriter(dbContext, tenantContext).Add(
                unit.CompanyId, unit.BuildingId, ConfigSectionKeys.LateFee, request.Exempt ? "Exempted" : "ExemptionRemoved",
                request.Exempt
                    ? $"La unidad {unit.Code} quedó exonerada de mora. Motivo: {reason}"
                    : $"La unidad {unit.Code} dejó de estar exonerada de mora.",
                "Unit", unit.Id, changes);

            unit.LateFeeExempt = request.Exempt;
            unit.LateFeeExemptReason = request.Exempt ? reason : null;
            unit.LateFeeExemptByUserId = request.Exempt ? tenantContext.UserId : null;
            unit.LateFeeExemptAtUtc = request.Exempt ? DateTime.UtcNow : null;
            unit.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new UnitDto
        {
            Id = unit.Id,
            BuildingId = unit.BuildingId,
            BuildingName = unit.Building?.Name ?? string.Empty,
            Code = unit.Code,
            Floor = unit.Floor,
            Coefficient = unit.Coefficient,
            IsActive = unit.IsActive,
            LateFeeExempt = unit.LateFeeExempt,
            LateFeeExemptReason = unit.LateFeeExemptReason
        });
    }
}
