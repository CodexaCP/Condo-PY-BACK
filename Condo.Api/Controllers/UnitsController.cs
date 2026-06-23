using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/units")]
public partial class UnitsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UnitDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Building != null && !x.Building.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
            }
        }

        var units = await query
            .OrderBy(x => x.Code)
            .Select(x => new UnitDto
            {
                Id = x.Id,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Code = x.Code,
                Floor = x.Floor,
                Coefficient = x.Coefficient,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(units);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UnitDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var unit = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id && x.Building != null && !x.Building.IsDeleted)
            .Select(x => new UnitDto
            {
                Id = x.Id,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Code = x.Code,
                Floor = x.Floor,
                Coefficient = x.Coefficient,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken) ? Ok(unit) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<UnitDto>> Create([FromBody] UnitUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return BadRequest("No se puede crear una unidad sin un edificio valido.");
        }

        if (!await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
        {
            return Forbid();
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var duplicatedCode = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == request.BuildingId && x.Code == normalizedCode, cancellationToken);

        if (duplicatedCode)
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar unidades.");
        }

        var entity = new Unit
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            Code = normalizedCode,
            Floor = request.Floor.Trim(),
            Coefficient = request.Coefficient,
            IsActive = request.IsActive
        };

        dbContext.Units.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, building.Name));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UnitDto>> Update(Guid id, [FromBody] UnitUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.Units.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null || !await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
        {
            return BadRequest("No se puede guardar una unidad sin un edificio valido y accesible.");
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var duplicatedCode = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == request.BuildingId && x.Id != id && x.Code == normalizedCode, cancellationToken);

        if (duplicatedCode)
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar unidades.");
        }

        entity.CompanyId = effectiveCompanyId.Value;
        entity.BuildingId = request.BuildingId;
        entity.Code = normalizedCode;
        entity.Floor = request.Floor.Trim();
        entity.Coefficient = request.Coefficient;
        entity.IsActive = request.IsActive;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe una unidad con ese codigo dentro del edificio.");
        }

        return Ok(ToDto(entity, building.Name));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Units.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var hasResidents = await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.UnitId == entity.Id, cancellationToken);

        var hasCharges = await dbContext.ExpenseCharges
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.UnitId == entity.Id, cancellationToken);

        var hasPayments = await dbContext.Payments
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.UnitId == entity.Id, cancellationToken);

        var hasIndividualExpenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.TargetUnitId == entity.Id, cancellationToken);

        if (hasResidents || hasCharges || hasPayments || hasIndividualExpenses)
        {
            return BadRequest("No se puede eliminar la unidad porque tiene residentes, cargos, pagos o gastos individuales asociados.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static UnitDto ToDto(Unit entity, string buildingName) =>
        new()
        {
            Id = entity.Id,
            BuildingId = entity.BuildingId,
            BuildingName = buildingName,
            Code = entity.Code,
            Floor = entity.Floor,
            Coefficient = entity.Coefficient,
            IsActive = entity.IsActive
        };

    private static bool IsValidRequest(UnitUpsertRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "No se puede crear una unidad sin seleccionar un edificio.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            error = "El codigo de la unidad es obligatorio.";
            return false;
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (!CodeRegex().IsMatch(normalizedCode))
        {
            error = "El codigo de la unidad solo puede contener letras, numeros y guiones medios.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Floor))
        {
            error = "El piso de la unidad es obligatorio.";
            return false;
        }

        if (request.Coefficient < 0m)
        {
            error = "El coeficiente no puede ser negativo.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsUniqueCodeViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains("IX_Units_BuildingId_Code", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex CodeRegex();
}
