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
[Route("api/buildings")]
public partial class BuildingsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                if (accessScope.CondominiumId.HasValue)
                {
                    var condId = accessScope.CondominiumId.Value;
                    query = query.Where(x => x.CondominiumId == condId);
                }
                else
                {
                    var cid = accessScope.CompanyId.Value;
                    query = query.Where(x => x.CompanyId == cid || (x.Condominium != null && x.Condominium.CompanyId == cid));
                }
            }
            else
            {
                query = query.Where(x => accessibleBuildingIds.Contains(x.Id));
            }
        }

        var buildings = await query
            .OrderBy(x => x.Name)
            .Select(x => new BuildingDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                CondominiumId = x.CondominiumId,
                CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty,
                Name = x.Name,
                Code = x.Code,
                Address = x.Address,
                IsActive = x.IsActive,
                Description = x.Description,
                ContactPhonePrefix = x.ContactPhonePrefix,
                ContactPhone = x.ContactPhone,
                ContactEmail = x.ContactEmail
            })
            .ToListAsync(cancellationToken);

        return Ok(buildings);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildingDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!await accessScope.CanAccessBuildingAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new BuildingDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                CondominiumId = x.CondominiumId,
                CondominiumName = x.Condominium != null ? x.Condominium.Name : string.Empty,
                Name = x.Name,
                Code = x.Code,
                Address = x.Address,
                IsActive = x.IsActive,
                Description = x.Description,
                ContactPhonePrefix = x.ContactPhonePrefix,
                ContactPhone = x.ContactPhone,
                ContactEmail = x.ContactEmail
            })
            .FirstOrDefaultAsync(cancellationToken);

        return building is null ? NotFound() : Ok(building);
    }

    [HttpPost]
    public async Task<ActionResult<BuildingDto>> Create([FromBody] BuildingUpsertRequest request, CancellationToken cancellationToken)
    {
        var companyId = accessScope.IsSuperAdmin ? request.CompanyId : accessScope.CompanyId;

        if (!accessScope.IsSuperAdmin && !companyId.HasValue)
        {
            return Forbid();
        }

        if (companyId.HasValue && !await accessScope.CanManageCompanyAsync(companyId.Value, cancellationToken))
        {
            return Forbid();
        }

        // Admin de condominio solo puede asociar edificios a su propio condominio
        if (accessScope.IsCompanyAdmin && accessScope.CondominiumId.HasValue
            && request.CondominiumId.HasValue
            && request.CondominiumId.Value != accessScope.CondominiumId.Value)
        {
            return Forbid();
        }

        var condominium = await ResolveCondominiumAsync(companyId, request.CondominiumId, cancellationToken);
        if (request.CondominiumId.HasValue && condominium is null)
        {
            return BadRequest("El condominio no existe o no pertenece a la empresa seleccionada.");
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedAddress = request.Address.Trim();

        if (companyId.HasValue)
        {
            var duplicatedCode = await dbContext.Buildings
                .AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.Code == normalizedCode, cancellationToken);

            if (duplicatedCode)
            {
                return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
            }
        }

        var entity = new Building
        {
            CompanyId = companyId,
            CondominiumId = request.CondominiumId,
            Name = normalizedName,
            Code = normalizedCode,
            Address = normalizedAddress,
            IsActive = request.IsActive,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ContactPhonePrefix = string.IsNullOrWhiteSpace(request.ContactPhonePrefix) ? null : request.ContactPhonePrefix.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant()
        };

        dbContext.Buildings.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
        }

        entity.Condominium = condominium;
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BuildingDto>> Update(Guid id, [FromBody] BuildingUpsertRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Buildings
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var effectiveCompanyId = await GetEffectiveCompanyIdAsync(entity, cancellationToken);
        if (effectiveCompanyId.HasValue)
        {
            if (!await accessScope.CanManageCompanyAsync(effectiveCompanyId.Value, cancellationToken))
            {
                return Forbid();
            }
        }
        else if (!accessScope.IsSuperAdmin)
        {
            return Forbid();
        }

        var condominium = await ResolveCondominiumAsync(entity.CompanyId, request.CondominiumId, cancellationToken);
        if (request.CondominiumId.HasValue && condominium is null)
        {
            return BadRequest("El condominio no existe o no pertenece a la empresa seleccionada.");
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedAddress = request.Address.Trim();

        if (entity.CompanyId.HasValue)
        {
            var duplicatedCode = await dbContext.Buildings
                .AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.CompanyId == entity.CompanyId.Value && x.Id != id && x.Code == normalizedCode, cancellationToken);

            if (duplicatedCode)
            {
                return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
            }
        }

        entity.CondominiumId = request.CondominiumId;
        entity.Name = normalizedName;
        entity.Code = normalizedCode;
        entity.Address = normalizedAddress;
        entity.IsActive = request.IsActive;
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.ContactPhonePrefix = string.IsNullOrWhiteSpace(request.ContactPhonePrefix) ? null : request.ContactPhonePrefix.Trim();
        entity.ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim();
        entity.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe un edificio con ese codigo dentro de la empresa.");
        }

        entity.Condominium = condominium;
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Buildings.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var effectiveCompanyId = await GetEffectiveCompanyIdAsync(entity, cancellationToken);
        if (effectiveCompanyId.HasValue)
        {
            if (!await accessScope.CanManageCompanyAsync(effectiveCompanyId.Value, cancellationToken))
            {
                return Forbid();
            }
        }
        else if (!accessScope.IsSuperAdmin)
        {
            return Forbid();
        }

        var hasUnits = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasPeriods = await dbContext.ExpensePeriods
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasAccesses = await dbContext.UserBuildingAccesses
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasExpenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasIncomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        var hasSettlements = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.Id, cancellationToken);

        if (hasUnits || hasPeriods || hasAccesses || hasExpenses || hasIncomes || hasSettlements)
        {
            return BadRequest("No se puede eliminar el edificio porque tiene unidades, periodos, accesos o movimientos asociados.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Guid?> GetEffectiveCompanyIdAsync(Building entity, CancellationToken cancellationToken)
    {
        if (entity.CompanyId.HasValue) return entity.CompanyId;
        if (!entity.CondominiumId.HasValue) return null;

        return await dbContext.Condominiums
            .AsNoTracking()
            .Where(x => x.Id == entity.CondominiumId.Value && !x.IsDeleted)
            .Select(x => (Guid?)x.CompanyId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? ValidateRequest(BuildingUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name.Trim()))
        {
            return "El nombre es obligatorio.";
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return "El codigo es obligatorio.";
        }

        if (!CodeRegex().IsMatch(normalizedCode))
        {
            return "El codigo solo puede contener letras, numeros y guiones medios.";
        }

        if (string.IsNullOrWhiteSpace(request.Address.Trim()))
        {
            return "La direccion es obligatoria.";
        }

        return null;
    }

    private static bool IsUniqueCodeViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains("IX_Buildings_CompanyId_Code", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex CodeRegex();

    private Task<Condominium?> ResolveCondominiumAsync(Guid? companyId, Guid? condominiumId, CancellationToken cancellationToken)
    {
        if (!condominiumId.HasValue) return Task.FromResult<Condominium?>(null);

        var query = dbContext.Condominiums.Where(x => !x.IsDeleted && x.Id == condominiumId.Value);
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    private static BuildingDto ToDto(Building entity) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            CondominiumId = entity.CondominiumId,
            CondominiumName = entity.Condominium != null ? entity.Condominium.Name : string.Empty,
            Name = entity.Name,
            Code = entity.Code,
            Address = entity.Address,
            IsActive = entity.IsActive,
            Description = entity.Description,
            ContactPhonePrefix = entity.ContactPhonePrefix,
            ContactPhone = entity.ContactPhone,
            ContactEmail = entity.ContactEmail
        };
}
