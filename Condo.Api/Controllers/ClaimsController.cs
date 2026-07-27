using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
[Route("api/claims")]
public class ClaimsController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClaimDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        if (buildingId.HasValue && !accessScope.IsSuperAdmin && !accessibleBuildingIds.Contains(buildingId.Value))
            return Forbid();

        var query = dbContext.Claims
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (buildingId.HasValue)
        {
            query = query.Where(x => x.BuildingId == buildingId.Value);
        }
        else if (!accessScope.IsSuperAdmin)
        {
            query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!IsValidStatus(status))
                return BadRequest("Estado de reclamo inválido.");

            query = query.Where(x => x.Status == status.Trim());
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(ToDto())
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ClaimDto>> UpdateStatus(
        Guid id,
        [FromBody] ClaimStatusUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidStatus(request.Status))
            return BadRequest("Estado de reclamo inválido.");

        var claim = await dbContext.Claims
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (claim is null)
            return NotFound();

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(claim.BuildingId, cancellationToken))
            return Forbid();

        claim.Status = request.Status.Trim();
        if (claim.Status == "Resuelto")
        {
            claim.ResolvedAtUtc = DateTime.UtcNow;
            claim.ResolvedByUserId = tenantContext.UserId;
        }
        else
        {
            claim.ResolvedAtUtc = null;
            claim.ResolvedByUserId = null;
        }

        var statusLabel = claim.Status == "EnProceso" ? "En proceso" : claim.Status;
        dbContext.Notifications.Add(new Notification
        {
            CompanyId = claim.CompanyId,
            RecipientId = claim.CreatedByUserId,
            Type = NotificationType.ClaimStatusUpdated,
            Title = "Estado de reclamo actualizado",
            Body = $"Tu reclamo fue marcado como \"{statusLabel}\".",
            EntityType = "Claim",
            EntityId = claim.Id
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = await dbContext.Claims
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto())
            .FirstAsync(cancellationToken);

        return Ok(dto);
    }

    private static System.Linq.Expressions.Expression<Func<Claim, ClaimDto>> ToDto() =>
        x => new ClaimDto
        {
            Id = x.Id,
            CondominiumId = x.CondominiumId,
            CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty,
            BuildingId = x.BuildingId,
            BuildingName = x.Building.Name,
            UnitId = x.UnitId,
            UnitCode = x.Unit.Code,
            Category = x.Category,
            Description = x.Description,
            Status = x.Status,
            CreatedByUserId = x.CreatedByUserId,
            CreatedByName = x.CreatedByUser.FullName,
            CreatedAtUtc = x.CreatedAtUtc,
            UpdatedAtUtc = x.UpdatedAtUtc,
            ResolvedAtUtc = x.ResolvedAtUtc,
            ResolvedByUserId = x.ResolvedByUserId,
            ResolvedByUserName = x.ResolvedByUser != null ? x.ResolvedByUser.FullName : string.Empty
        };

    private static bool IsValidStatus(string? status) =>
        status is "Pendiente" or "EnProceso" or "Resuelto";
}
