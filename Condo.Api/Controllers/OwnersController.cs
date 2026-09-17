using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/owners")]
public class OwnersController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    Condo.Application.Abstractions.IPasswordHasher passwordHasher) : ControllerBase
{
    private static readonly Regex EmailRegex    = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
    private static readonly Regex UsernameRegex = new(@"^[a-z0-9][a-z0-9.\-_]*$",  RegexOptions.Compiled);

    // ─── GET ALL ────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OwnerDto>>> GetAll(
        [FromQuery] bool includeResidents,
        CancellationToken cancellationToken)
    {
        if (!CanManageOwners()) return Forbid();

        // includeResidents=true se usa desde la pantalla de asignación de propietarios,
        // para poder elegir también a una cuenta Resident (una misma persona puede ser
        // propietaria de una unidad y residente de otra, o de la misma).
        IQueryable<ApplicationUser> query = dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && (x.Role == UserRole.Owner || (includeResidents && x.Role == UserRole.Resident)));

        if (!accessScope.IsSuperAdmin)
        {
            if (!accessScope.CompanyId.HasValue) return Forbid();
            query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
        }

        var owners = await query.OrderBy(x => x.FullName).ToListAsync(cancellationToken);
        return Ok(owners.Select(ToDto).ToList());
    }

    // ─── GET BY ID ──────────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnerDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!CanManageOwners()) return Forbid();

        var owner = await dbContext.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.Role == UserRole.Owner, cancellationToken);

        if (owner is null) return NotFound();

        if (!accessScope.IsSuperAdmin &&
            (!accessScope.CompanyId.HasValue || owner.CompanyId != accessScope.CompanyId.Value))
            return Forbid();

        return Ok(ToDto(owner));
    }

    // ─── CREATE ─────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<OwnerDto>> Create([FromBody] OwnerUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageOwners()) return Forbid();
        if (!accessScope.CompanyId.HasValue) return Forbid();

        var companyId = accessScope.CompanyId.Value;

        var validationError = ValidateRequest(request);
        if (validationError is not null) return validationError;

        var normalizedEmail    = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        if (await EmailExistsAsync(companyId, normalizedEmail, null, cancellationToken))
            return Conflict("Ya existe un usuario con ese correo dentro de la empresa.");

        if (await UsernameExistsAsync(companyId, normalizedUsername, null, cancellationToken))
            return Conflict("Ya existe un usuario con ese nombre de usuario dentro de la empresa.");

        var firstName = request.FirstName.Trim();
        var lastName  = request.LastName.Trim();

        var owner = new ApplicationUser
        {
            CompanyId        = companyId,
            FirstName        = firstName,
            LastName         = lastName,
            FullName         = string.IsNullOrWhiteSpace(request.FullName)
                                 ? $"{firstName} {lastName}".Trim()
                                 : request.FullName.Trim(),
            Username         = normalizedUsername,
            Email            = normalizedEmail,
            PasswordHash     = passwordHasher.Hash(string.IsNullOrWhiteSpace(request.Password) ? "Condo*Temp1" : request.Password),
            DocumentType     = NormalizeDocumentType(request.DocumentType),
            DocumentNumber   = request.DocumentNumber?.Trim() ?? null,
            PhonePrefix      = request.PhonePrefix?.Trim() ?? null,
            Phone            = request.Phone?.Trim() ?? null,
            Address          = request.Address?.Trim() ?? null,
            IsResident       = request.IsResident,
            Role             = UserRole.Owner,
            IsActive         = request.IsActive,
            MustChangePassword = true
        };

        dbContext.ApplicationUsers.Add(owner);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict("Ya existe un propietario con ese correo o nombre de usuario.");
        }

        return Ok(ToDto(owner));
    }

    // ─── UPDATE ─────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OwnerDto>> Update(Guid id, [FromBody] OwnerUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageOwners()) return Forbid();

        var owner = await dbContext.ApplicationUsers
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.Role == UserRole.Owner, cancellationToken);

        if (owner is null) return NotFound();

        if (!accessScope.IsSuperAdmin &&
            (!accessScope.CompanyId.HasValue || owner.CompanyId != accessScope.CompanyId.Value))
            return Forbid();

        var companyId = owner.CompanyId ?? accessScope.CompanyId!.Value;

        var validationError = ValidateRequest(request);
        if (validationError is not null) return validationError;

        var normalizedEmail    = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        if (await EmailExistsAsync(companyId, normalizedEmail, owner.Id, cancellationToken))
            return Conflict("Ya existe un usuario con ese correo dentro de la empresa.");

        if (await UsernameExistsAsync(companyId, normalizedUsername, owner.Id, cancellationToken))
            return Conflict("Ya existe un usuario con ese nombre de usuario dentro de la empresa.");

        var firstName = request.FirstName.Trim();
        var lastName  = request.LastName.Trim();

        owner.FirstName      = firstName;
        owner.LastName       = lastName;
        owner.FullName       = string.IsNullOrWhiteSpace(request.FullName)
                                 ? $"{firstName} {lastName}".Trim()
                                 : request.FullName.Trim();
        owner.Username       = normalizedUsername;
        owner.Email          = normalizedEmail;
        owner.DocumentType   = NormalizeDocumentType(request.DocumentType);
        owner.DocumentNumber = request.DocumentNumber?.Trim() ?? null;
        owner.PhonePrefix    = request.PhonePrefix?.Trim() ?? null;
        owner.Phone          = request.Phone?.Trim() ?? null;
        owner.Address        = request.Address?.Trim() ?? null;
        owner.IsResident     = request.IsResident;
        owner.IsActive       = request.IsActive;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            owner.PasswordHash       = passwordHasher.Hash(request.Password);
            owner.MustChangePassword = true;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict("Ya existe un propietario con ese correo o nombre de usuario.");
        }

        return Ok(ToDto(owner));
    }

    // ─── DELETE ─────────────────────────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!CanManageOwners()) return Forbid();

        var owner = await dbContext.ApplicationUsers
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.Role == UserRole.Owner, cancellationToken);

        if (owner is null) return NotFound();

        if (!accessScope.IsSuperAdmin &&
            (!accessScope.CompanyId.HasValue || owner.CompanyId != accessScope.CompanyId.Value))
            return Forbid();

        owner.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ─── HELPERS ────────────────────────────────────────────────────────────

    private bool CanManageOwners()
    {
        if (tenantContext.IsSuperAdmin) return true;
        var role = tenantContext.Role;
        return string.Equals(role, "BuildingManager", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "CompanyAdmin",    StringComparison.OrdinalIgnoreCase);
    }

    private ActionResult? ValidateRequest(OwnerUpsertRequest request)
    {
        if (request is null) return BadRequest("La solicitud es obligatoria.");

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(firstName))
            return BadRequest("El nombre es obligatorio.");
        if (firstName.Length > 80)
            return BadRequest("El nombre no puede superar los 80 caracteres.");

        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(lastName))
            return BadRequest("Los apellidos son obligatorios.");
        if (lastName.Length > 100)
            return BadRequest("Los apellidos no pueden superar los 100 caracteres.");

        var username = request.Username?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("El nombre de usuario es obligatorio.");
        if (username.Length > 60)
            return BadRequest("El nombre de usuario no puede superar los 60 caracteres.");
        if (!UsernameRegex.IsMatch(username))
            return BadRequest("Nombre de usuario inválido. Solo minúsculas, números, puntos y guiones.");

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("El correo es obligatorio.");
        if (email.Length > 160)
            return BadRequest("El correo no puede superar los 160 caracteres.");
        if (!EmailRegex.IsMatch(email))
            return BadRequest("El correo no tiene un formato válido.");

        return null;
    }

    private async Task<bool> EmailExistsAsync(Guid companyId, string email, Guid? excludeId, CancellationToken ct) =>
        await dbContext.ApplicationUsers.AnyAsync(
            x => !x.IsDeleted && x.CompanyId == companyId && x.Email == email
                 && (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    private async Task<bool> UsernameExistsAsync(Guid companyId, string username, Guid? excludeId, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(username) &&
        await dbContext.ApplicationUsers.AnyAsync(
            x => !x.IsDeleted && x.CompanyId == companyId && x.Username == username
                 && (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private static string? NormalizeDocumentType(string? raw) =>
        raw?.Trim() is { Length: > 0 } t ? t : null;

    private static OwnerDto ToDto(ApplicationUser u) => new()
    {
        Id             = u.Id,
        CompanyId      = u.CompanyId,
        FirstName      = u.FirstName,
        LastName       = u.LastName,
        FullName       = u.FullName,
        Username       = u.Username,
        Email          = u.Email,
        DocumentType   = u.DocumentType,
        DocumentNumber = u.DocumentNumber,
        PhonePrefix    = u.PhonePrefix,
        Phone          = u.Phone,
        Address        = u.Address,
        IsResident     = u.IsResident,
        IsActive       = u.IsActive
    };
}
