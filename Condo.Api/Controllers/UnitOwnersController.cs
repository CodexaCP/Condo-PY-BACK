using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/unit-owners")]
public class UnitOwnersController(
    ICondoDbContext dbContext, IAccessScopeService accessScope, IOwnerResidencySyncService residencySync) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UnitOwnerDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            else
                query = query.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
        }

        if (buildingId.HasValue)
            query = query.Where(x => x.Unit!.BuildingId == buildingId.Value);

        var items = await query
            .OrderBy(x => x.Unit!.Building!.Name)
            .ThenBy(x => x.Unit!.Code)
            .ThenByDescending(x => x.IsPrimary)
            .Select(x => new UnitOwnerDto
            {
                Id = x.Id,
                UnitId = x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
                BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
                OwnerId = x.OwnerId,
                OwnerName = x.Owner != null ? x.Owner.FullName : string.Empty,
                IsPrimary = x.IsPrimary,
                StartDate = x.StartDate
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<UnitOwnerDto>> Create(
        [FromBody] CreateUnitOwnerRequest request,
        CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);

        if (unit is null) return NotFound("Unidad no encontrada.");

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
            return Forbid();

        // Se permite vincular como propietario a cualquier cuenta Owner o Resident: una misma
        // persona puede ser propietaria de una unidad y residente de otra (o de la misma), sin
        // necesidad de cambiarle el rol base de la cuenta.
        var owner = await dbContext.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.OwnerId
                     && (x.Role == UserRole.Owner || x.Role == UserRole.Resident), cancellationToken);

        if (owner is null) return NotFound("Propietario no encontrado.");

        var duplicate = await dbContext.UnitOwners
            .AnyAsync(x => !x.IsDeleted && x.UnitId == request.UnitId && x.OwnerId == request.OwnerId, cancellationToken);

        if (duplicate) return Conflict("Este propietario ya esta asignado a esta unidad.");

        var companyId = unit.Building?.CompanyId ?? accessScope.CompanyId ?? Guid.Empty;

        var entity = new UnitOwner
        {
            UnitId = request.UnitId,
            OwnerId = request.OwnerId,
            IsPrimary = request.IsPrimary,
            StartDate = request.StartDate,
            CompanyId = companyId
        };

        dbContext.UnitOwners.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (owner.IsResident)
            await residencySync.SyncAsync(owner, companyId, cancellationToken);

        return Ok(new UnitOwnerDto
        {
            Id = entity.Id,
            UnitId = entity.UnitId,
            UnitCode = unit.Code,
            BuildingId = unit.BuildingId,
            BuildingName = unit.Building?.Name ?? string.Empty,
            OwnerId = entity.OwnerId,
            OwnerName = owner.FullName,
            IsPrimary = entity.IsPrimary,
            StartDate = entity.StartDate
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UnitOwners
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        var unit = await dbContext.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == entity.UnitId, cancellationToken);

        if (unit is null || !await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
            return Forbid();

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
