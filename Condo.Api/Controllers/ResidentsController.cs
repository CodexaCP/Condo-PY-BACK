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
[Route("api/residents")]
public partial class ResidentsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResidentDto>>> GetAll(CancellationToken cancellationToken)
    {
        IQueryable<Resident> query = dbContext.Residents.AsNoTracking().Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
                query = query.Where(x =>
                    x.UnitResidents.Any(link =>
                        !link.IsDeleted &&
                        accessibleBuildingIds.Contains(link.Unit!.BuildingId)));
            }
        }

        var residents = await query
            .OrderBy(x => x.FullName)
            .Select(x => new ResidentDto
            {
                Id = x.Id,
                FullName = x.FullName,
                DocumentNumber = x.DocumentNumber,
                Email = x.Email,
                PhoneNumber = x.PhoneNumber,
                IsOwner = x.IsOwner,
                IsActive = x.IsActive,
                HasLinkedAccount = x.ApplicationUserId != null
            })
            .ToListAsync(cancellationToken);

        return Ok(residents);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResidentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var resident = await dbContext.Residents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new ResidentDto
            {
                Id = x.Id,
                FullName = x.FullName,
                DocumentNumber = x.DocumentNumber,
                Email = x.Email,
                PhoneNumber = x.PhoneNumber,
                IsOwner = x.IsOwner,
                IsActive = x.IsActive,
                HasLinkedAccount = x.ApplicationUserId != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (resident is null)
        {
            return NotFound();
        }

        if (accessScope.IsSuperAdmin || (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue))
        {
            return Ok(resident);
        }

        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
        var hasAccess = await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.ResidentId == id && accessibleBuildingIds.Contains(x.Unit!.BuildingId), cancellationToken);

        return hasAccess ? Ok(resident) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<ResidentDto>> Create([FromBody] ResidentUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin && !accessScope.IsCompanyAdmin
            && !User.IsInRole("BuildingManager") && !User.IsInRole("CompanyOperator"))
        {
            return Forbid();
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var companyId = accessScope.IsSuperAdmin ? request.CompanyId : accessScope.CompanyId;
        if (!companyId.HasValue)
        {
            return BadRequest("La empresa es obligatoria para crear un residente.");
        }

        // No usar CanManageCompanyAsync aquí: solo autoriza SuperAdmin/CompanyAdmin y dejaría sin
        // efecto el permiso de BuildingManager/CompanyOperator recién validado arriba. Como
        // companyId ya es siempre accessScope.CompanyId para quien no es SuperAdmin, alcanza con
        // la misma verificación de alcance que usan Update y Delete más abajo; para SuperAdmin se
        // conserva la validación de que la empresa indicada exista.
        if (accessScope.IsSuperAdmin)
        {
            var companyExists = await dbContext.Companies.AnyAsync(x => x.Id == companyId.Value && !x.IsDeleted, cancellationToken);
            if (!companyExists)
            {
                return BadRequest("La empresa indicada no existe.");
            }
        }
        else if (accessScope.CompanyId != companyId)
        {
            return Forbid();
        }

        var normalizedDocumentNumber = request.DocumentNumber.Trim();
        var duplicatedDocument = await dbContext.Residents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.CompanyId == companyId.Value && x.DocumentNumber == normalizedDocumentNumber, cancellationToken);

        if (duplicatedDocument)
        {
            return Conflict("Ya existe un residente con ese documento dentro de la empresa.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var linkedUserId = await FindMatchingUserIdAsync(companyId.Value, normalizedEmail, cancellationToken);

        var entity = new Resident
        {
            CompanyId = companyId.Value,
            FullName = request.FullName.Trim(),
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? null : request.DocumentType.Trim(),
            DocumentNumber = normalizedDocumentNumber,
            Email = normalizedEmail,
            PhoneNumber = request.PhoneNumber.Trim(),
            IsOwner = request.IsOwner,
            IsActive = request.IsActive,
            ApplicationUserId = linkedUserId
        };

        dbContext.Residents.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueDocumentViolation(exception))
        {
            return Conflict("Ya existe un residente con ese documento dentro de la empresa.");
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ResidentDto>> Update(Guid id, [FromBody] ResidentUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin && !accessScope.IsCompanyAdmin
            && !User.IsInRole("BuildingManager") && !User.IsInRole("CompanyOperator"))
        {
            return Forbid();
        }

        var entity = await dbContext.Residents.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!accessScope.IsSuperAdmin && accessScope.CompanyId != entity.CompanyId)
        {
            return Forbid();
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedDocumentNumber = request.DocumentNumber.Trim();
        var duplicatedDocument = await dbContext.Residents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.CompanyId == entity.CompanyId && x.Id != id && x.DocumentNumber == normalizedDocumentNumber, cancellationToken);

        if (duplicatedDocument)
        {
            return Conflict("Ya existe un residente con ese documento dentro de la empresa.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (normalizedEmail != entity.Email)
        {
            entity.ApplicationUserId = await FindMatchingUserIdAsync(entity.CompanyId, normalizedEmail, cancellationToken);
        }

        entity.FullName = request.FullName.Trim();
        entity.DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? null : request.DocumentType.Trim();
        entity.DocumentNumber = normalizedDocumentNumber;
        entity.Email = normalizedEmail;
        entity.PhoneNumber = request.PhoneNumber.Trim();
        entity.IsOwner = request.IsOwner;
        entity.IsActive = request.IsActive;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueDocumentViolation(exception))
        {
            return Conflict("Ya existe un residente con ese documento dentro de la empresa.");
        }

        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin && !accessScope.IsCompanyAdmin
            && !User.IsInRole("BuildingManager") && !User.IsInRole("CompanyOperator"))
        {
            return Forbid();
        }

        var entity = await dbContext.Residents.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!accessScope.IsSuperAdmin && accessScope.CompanyId != entity.CompanyId)
        {
            return Forbid();
        }

        var hasAssignments = await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.ResidentId == entity.Id, cancellationToken);

        if (hasAssignments)
        {
            return BadRequest("No se puede eliminar el residente porque tiene asignaciones activas.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string? ValidateRequest(ResidentUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName.Trim()))
        {
            return "El nombre completo es obligatorio.";
        }

        var normalizedDocumentNumber = request.DocumentNumber.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDocumentNumber))
        {
            return "El documento es obligatorio.";
        }

        if (!DocumentRegex().IsMatch(normalizedDocumentNumber))
        {
            return "El documento solo puede contener letras, numeros, puntos o guiones.";
        }

        var normalizedEmail = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return "El correo es obligatorio.";
        }

        if (!EmailRegex().IsMatch(normalizedEmail))
        {
            return "El correo no tiene un formato valido.";
        }

        var normalizedPhoneNumber = request.PhoneNumber.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPhoneNumber))
        {
            return "El telefono es obligatorio.";
        }

        if (!PhoneRegex().IsMatch(normalizedPhoneNumber))
        {
            return "El telefono solo puede contener numeros, espacios, parentesis, mas o guiones.";
        }

        return null;
    }

    private async Task<Guid?> FindMatchingUserIdAsync(Guid companyId, string normalizedEmail, CancellationToken ct) =>
        await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == companyId && x.Email == normalizedEmail)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

    private static bool IsUniqueDocumentViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains("IX_Residents_CompanyId_DocumentNumber", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9.-]+$")]
    private static partial Regex DocumentRegex();

    [GeneratedRegex("^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex("^[0-9+()\\-\\s]{6,20}$")]
    private static partial Regex PhoneRegex();

    private static ResidentDto ToDto(Resident entity) =>
        new()
        {
            Id = entity.Id,
            FullName = entity.FullName,
            DocumentType = entity.DocumentType,
            DocumentNumber = entity.DocumentNumber,
            Email = entity.Email,
            PhoneNumber = entity.PhoneNumber,
            IsOwner = entity.IsOwner,
            IsActive = entity.IsActive,
            HasLinkedAccount = entity.ApplicationUserId != null
        };
}
