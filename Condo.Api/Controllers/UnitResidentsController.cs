using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/unit-residents")]
public class UnitResidentsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UnitResidentDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                query = query.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
            }
        }

        var links = await query
            .OrderBy(x => x.Unit!.Code)
            .ThenBy(x => x.Resident!.FullName)
            .Select(x => new UnitResidentDto
            {
                Id = x.Id,
                UnitId = x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                ResidentId = x.ResidentId,
                ResidentName = x.Resident != null ? x.Resident.FullName : string.Empty,
                IsPrimary = x.IsPrimary,
                StartDate = x.StartDate,
                EndDate = x.EndDate
            })
            .ToListAsync(cancellationToken);

        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<UnitResidentDto>> Create([FromBody] AssignResidentToUnitRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);
        var resident = await dbContext.Residents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ResidentId, cancellationToken);

        if (unit is null || resident is null || unit.CompanyId != resident.CompanyId)
        {
            return BadRequest("Unit or resident not found for the same company.");
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var duplicatedAssignment = await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x =>
                !x.IsDeleted &&
                x.UnitId == request.UnitId &&
                x.ResidentId == request.ResidentId &&
                x.StartDate == request.StartDate,
                cancellationToken);

        if (duplicatedAssignment)
        {
            return Conflict("Ya existe una asignacion con la misma unidad, residente y fecha de inicio.");
        }

        var overlapsUnit = await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x =>
                !x.IsDeleted &&
                x.UnitId == request.UnitId &&
                RangesOverlap(x.StartDate, x.EndDate, request.StartDate, request.EndDate),
                cancellationToken);

        if (overlapsUnit)
        {
            return BadRequest("La unidad ya tiene una asignacion activa o solapada en ese rango de fechas.");
        }

        if (request.IsPrimary)
        {
            var hasPrimaryOverlap = await dbContext.UnitResidents
                .AsNoTracking()
                .AnyAsync(x =>
                    !x.IsDeleted &&
                    x.UnitId == request.UnitId &&
                    x.IsPrimary &&
                    RangesOverlap(x.StartDate, x.EndDate, request.StartDate, request.EndDate),
                    cancellationToken);

            if (hasPrimaryOverlap)
            {
                return BadRequest("La unidad ya tiene una asignacion principal activa en ese rango de fechas.");
            }
        }

        var entity = new UnitResident
        {
            CompanyId = unit.CompanyId,
            UnitId = request.UnitId,
            ResidentId = request.ResidentId,
            IsPrimary = request.IsPrimary,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };

        dbContext.UnitResidents.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueAssignmentViolation(exception))
        {
            return Conflict("Ya existe una asignacion con la misma unidad, residente y fecha de inicio.");
        }

        return Ok(ToDto(entity, unit.Code, resident.FullName));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UnitResidents
            .Include(x => x.Unit)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (entity.Unit is null || !await accessScope.CanAccessBuildingAsync(entity.Unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static UnitResidentDto ToDto(UnitResident entity, string unitCode, string residentName) =>
        new()
        {
            Id = entity.Id,
            UnitId = entity.UnitId,
            UnitCode = unitCode,
            ResidentId = entity.ResidentId,
            ResidentName = residentName,
            IsPrimary = entity.IsPrimary,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate
        };

    private static bool IsValidRequest(AssignResidentToUnitRequest request, out string error)
    {
        if (request.UnitId == Guid.Empty)
        {
            error = "La unidad es obligatoria.";
            return false;
        }

        if (request.ResidentId == Guid.Empty)
        {
            error = "El residente es obligatorio.";
            return false;
        }

        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
        {
            error = "La fecha de fin no puede ser anterior a la fecha de inicio.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsUniqueAssignmentViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains("IX_UnitResidents_UnitId_ResidentId_StartDate", StringComparison.OrdinalIgnoreCase);

    private static bool RangesOverlap(DateOnly existingStart, DateOnly? existingEnd, DateOnly candidateStart, DateOnly? candidateEnd)
    {
        var resolvedExistingEnd = existingEnd ?? DateOnly.MaxValue;
        var resolvedCandidateEnd = candidateEnd ?? DateOnly.MaxValue;
        return existingStart <= resolvedCandidateEnd && candidateStart <= resolvedExistingEnd;
    }
}
