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
[Route("api/condominiums")]
public partial class CondominiumsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CondominiumDto>>> GetAll(CancellationToken cancellationToken)
    {
        IQueryable<Condominium> query = dbContext.Condominiums.AsNoTracking().Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (!accessScope.CompanyId.HasValue)
            {
                return Forbid();
            }

            if (accessScope.CondominiumId.HasValue)
                query = query.Where(x => x.Id == accessScope.CondominiumId.Value);
            else
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
        }

        var items = await query
            .OrderBy(x => x.Name)
            .Select(x => new CondominiumDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
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

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CondominiumDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin)
        {
            if (!accessScope.CompanyId.HasValue)
                return Forbid();

            if (accessScope.CondominiumId.HasValue)
            {
                if (id != accessScope.CondominiumId.Value) return Forbid();
            }
            else
            {
                var belongs = await dbContext.Condominiums.AsNoTracking()
                    .AnyAsync(x => !x.IsDeleted && x.Id == id && x.CompanyId == accessScope.CompanyId.Value, cancellationToken);
                if (!belongs) return Forbid();
            }
        }

        var item = await dbContext.Condominiums
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new CondominiumDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
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

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<CondominiumDto>> Create([FromBody] CondominiumUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin)
            return Forbid();

        var companyId = request.CompanyId;
        if (!companyId.HasValue || !await accessScope.CanManageCompanyAsync(companyId.Value, cancellationToken))
        {
            return Forbid();
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedAddress = request.Address.Trim();

        var duplicatedCode = await dbContext.Condominiums
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.Code == normalizedCode, cancellationToken);

        if (duplicatedCode)
        {
            return Conflict("Ya existe un condominio con ese codigo dentro de la empresa.");
        }

        var entity = new Condominium
        {
            CompanyId = companyId.Value,
            Name = normalizedName,
            Code = normalizedCode,
            Address = normalizedAddress,
            IsActive = request.IsActive,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ContactPhonePrefix = string.IsNullOrWhiteSpace(request.ContactPhonePrefix) ? null : request.ContactPhonePrefix.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant()
        };

        dbContext.Condominiums.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            return Conflict("Ya existe un condominio con ese codigo dentro de la empresa.");
        }

        return Ok(ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CondominiumDto>> Update(Guid id, [FromBody] CondominiumUpsertRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Condominiums.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!accessScope.IsSuperAdmin)
            return Forbid();

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedAddress = request.Address.Trim();

        var duplicatedCode = await dbContext.Condominiums
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.CompanyId == entity.CompanyId && x.Id != id && x.Code == normalizedCode, cancellationToken);

        if (duplicatedCode)
        {
            return Conflict("Ya existe un condominio con ese codigo dentro de la empresa.");
        }

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
            return Conflict("Ya existe un condominio con ese codigo dentro de la empresa.");
        }

        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Condominiums.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!accessScope.IsSuperAdmin)
            return Forbid();

        var buildings = await dbContext.Buildings
            .Where(x => !x.IsDeleted && x.CondominiumId == entity.Id)
            .ToListAsync(cancellationToken);

        foreach (var building in buildings)
        {
            building.CondominiumId = null;
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string? ValidateRequest(CondominiumUpsertRequest request)
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
        sqlException.Message.Contains("IX_Condominiums_CompanyId_Code", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex CodeRegex();

    private static CondominiumDto ToDto(Condominium entity) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
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
