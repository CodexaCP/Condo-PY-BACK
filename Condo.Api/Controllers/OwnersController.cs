using Condo.Api.Services;
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
    Condo.Application.Abstractions.IPasswordHasher passwordHasher,
    IOwnerResidencySyncService residencySync) : ControllerBase
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

        // Encargado/Operador (o admin de un condominio): solo propietarios con alguna unidad en sus edificios,
        // mas los que todavia no tienen unidad asignada.
        HashSet<Guid>? scope = null;
        if (!accessScope.HasFullCompanyScope)
        {
            scope = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
            query = query.VisibleUsers(dbContext, scope);
        }

        var owners = await query.OrderBy(x => x.FullName).ToListAsync(cancellationToken);
        var presidencies = await LoadPresidenciesAsync(owners.Select(x => x.Id).ToList(), scope, cancellationToken);
        return Ok(owners.Select(o => ToDto(o, presidencies)).ToList());
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

        var access = await ResolveAccessAsync(owner.Id, cancellationToken);
        if (!access.Visible) return NotFound();

        var presidencies = await LoadPresidenciesAsync([owner.Id], access.Scope, cancellationToken);
        return Ok(ToDto(owner, presidencies));
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

        if (await DocumentExistsAsync(companyId, request.DocumentNumber, null, cancellationToken))
            return Conflict("Ya existe un propietario con ese documento dentro de la empresa.");

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
        OwnerProfileRules.Apply(owner, request);

        dbContext.ApplicationUsers.Add(owner);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict("Ya existe un propietario con ese correo o nombre de usuario.");
        }

        await residencySync.SyncAsync(owner, companyId, cancellationToken);

        return Ok(ToDto(owner, new Dictionary<Guid, List<OwnerPresidentBuildingDto>>()));
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

        var access = await ResolveAccessAsync(owner.Id, cancellationToken);
        if (!access.Visible) return NotFound();

        var companyId = owner.CompanyId ?? accessScope.CompanyId!.Value;

        var validationError = ValidateRequest(request, owner);
        if (validationError is not null) return validationError;

        var normalizedEmail    = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        // Si el propietario tambien figura en edificios que el usuario no tiene asignados, no puede tocar lo que da
        // acceso a la cuenta (contrasena, usuario, correo, estado) ni la residencia: eso afectaria esos otros edificios.
        if (!access.FullyInScope &&
            (!string.IsNullOrWhiteSpace(request.Password)
             || request.IsActive != owner.IsActive
             || request.IsResident != owner.IsResident
             || normalizedEmail != owner.Email
             || normalizedUsername != owner.Username))
            return StatusCode(StatusCodes.Status403Forbidden, SharedOwnerMessage);

        if (await EmailExistsAsync(companyId, normalizedEmail, owner.Id, cancellationToken))
            return Conflict("Ya existe un usuario con ese correo dentro de la empresa.");

        if (await UsernameExistsAsync(companyId, normalizedUsername, owner.Id, cancellationToken))
            return Conflict("Ya existe un usuario con ese nombre de usuario dentro de la empresa.");

        var newDocument = PersonRules.TrimOrNull(request.DocumentNumber);
        if (newDocument != PersonRules.TrimOrNull(owner.DocumentNumber)
            && await DocumentExistsAsync(companyId, newDocument, owner.Id, cancellationToken))
            return Conflict("Ya existe un propietario con ese documento dentro de la empresa.");

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
        OwnerProfileRules.Apply(owner, request);

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            owner.PasswordHash       = passwordHasher.Hash(request.Password);
            owner.MustChangePassword = true;
        }

        // La firma pertenece al presidente (posiblemente de otro edificio): solo la gestiona quien tiene alcance total.
        if (access.FullyInScope)
        {
            if (!string.IsNullOrWhiteSpace(request.SignatureUrl))
            {
                if (request.SignatureUrl.Length > 500)
                    return BadRequest("La URL de la firma no puede superar los 500 caracteres.");

                var isPresident = await dbContext.Buildings
                    .AnyAsync(x => !x.IsDeleted && x.PresidentUserId == owner.Id, cancellationToken);
                if (!isPresident)
                    return BadRequest("Solo un propietario marcado como presidente de consorcio puede tener una firma.");

                owner.SignatureUrl = request.SignatureUrl.Trim();
            }
            else
            {
                owner.SignatureUrl = null;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict("Ya existe un propietario con ese correo o nombre de usuario.");
        }

        // La sincronizacion de residencia puede crear vinculos en cualquier unidad del propietario.
        if (access.FullyInScope)
            await residencySync.SyncAsync(owner, companyId, cancellationToken);

        var presidencies = await LoadPresidenciesAsync([owner.Id], access.Scope, cancellationToken);
        return Ok(ToDto(owner, presidencies));
    }

    // ─── PRESIDENTE DE CONSORCIO ────────────────────────────────────────────

    [HttpGet("{id:guid}/eligible-president-buildings")]
    public async Task<ActionResult<IReadOnlyList<OwnerEligibleBuildingDto>>> GetEligiblePresidentBuildings(
        Guid id, CancellationToken cancellationToken)
    {
        if (!CanManageOwners()) return Forbid();

        var owner = await dbContext.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.Role == UserRole.Owner, cancellationToken);

        if (owner is null) return NotFound();

        if (!accessScope.IsSuperAdmin &&
            (!accessScope.CompanyId.HasValue || owner.CompanyId != accessScope.CompanyId.Value))
            return Forbid();

        var access = await ResolveAccessAsync(id, cancellationToken);
        if (!access.Visible) return NotFound();

        var buildingIds = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OwnerId == id && x.Unit != null && !x.Unit.IsDeleted)
            .Select(x => x.Unit!.BuildingId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var buildings = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        // Solo los edificios que el usuario tiene asignados.
        if (access.Scope is not null)
            buildings = buildings.Where(x => access.Scope.Contains(x.Id)).ToList();

        var presidentIds = buildings.Where(x => x.PresidentUserId.HasValue && x.PresidentUserId != id)
            .Select(x => x.PresidentUserId!.Value).Distinct().ToList();
        var presidentNames = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => presidentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var result = buildings
            .OrderBy(x => x.Name)
            .Select(x => new OwnerEligibleBuildingDto
            {
                BuildingId = x.Id,
                BuildingName = x.Name,
                HasOtherPresident = x.PresidentUserId.HasValue && x.PresidentUserId != id,
                OtherPresidentName = x.PresidentUserId.HasValue && x.PresidentUserId != id
                    ? presidentNames.GetValueOrDefault(x.PresidentUserId.Value)
                    : null
            })
            .ToList();

        return Ok(result);
    }

    [HttpPut("{id:guid}/president-building")]
    public async Task<ActionResult<OwnerDto>> SetPresidentBuilding(
        Guid id, [FromBody] SetOwnerPresidentBuildingRequest request, CancellationToken cancellationToken)
    {
        if (!CanManageOwners()) return Forbid();

        var owner = await dbContext.ApplicationUsers
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.Role == UserRole.Owner, cancellationToken);

        if (owner is null) return NotFound();

        if (!accessScope.IsSuperAdmin &&
            (!accessScope.CompanyId.HasValue || owner.CompanyId != accessScope.CompanyId.Value))
            return Forbid();

        var access = await ResolveAccessAsync(id, cancellationToken);
        if (!access.Visible) return NotFound();

        // Sacamos al owner de cualquier edificio donde ya figure como presidente (solo puede serlo de uno).
        var currentPresidencies = await dbContext.Buildings
            .Where(x => !x.IsDeleted && x.PresidentUserId == id)
            .ToListAsync(cancellationToken);

        // Quien tiene edificios acotados no puede quitarle la presidencia de un edificio ajeno (al reasignar
        // o quitar se limpian todas las presidencias del propietario).
        if (access.Scope is not null && currentPresidencies.Any(x => !access.Scope.Contains(x.Id)))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Este propietario ya es presidente de un edificio que no tenés asignado. Lo debe modificar un Administrador de empresa.");

        foreach (var b in currentPresidencies)
        {
            b.PresidentUserId = null;
            b.PresidentAssignedAtUtc = null;
            b.PresidentAssignedByUserId = null;
        }

        if (request.BuildingId.HasValue)
        {
            var building = await dbContext.Buildings
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId.Value, cancellationToken);

            if (building is null) return BadRequest("El edificio indicado no existe.");

            if (!accessScope.IsSuperAdmin &&
                (!accessScope.CompanyId.HasValue || building.CompanyId != accessScope.CompanyId.Value))
                return Forbid();

            // Solo se puede nombrar presidente en un edificio que el usuario tiene asignado.
            if (access.Scope is not null && !access.Scope.Contains(building.Id))
                return Forbid();

            var hasUnitInBuilding = await dbContext.UnitOwners
                .AnyAsync(x => !x.IsDeleted && x.OwnerId == id && x.Unit != null && x.Unit.BuildingId == building.Id, cancellationToken);
            if (!hasUnitInBuilding)
                return BadRequest("El propietario no tiene ninguna unidad vinculada en ese edificio.");

            if (building.PresidentUserId.HasValue && building.PresidentUserId != id)
            {
                var currentName = await dbContext.ApplicationUsers
                    .Where(x => x.Id == building.PresidentUserId.Value)
                    .Select(x => x.FullName)
                    .FirstOrDefaultAsync(cancellationToken);
                return Conflict($"Este edificio ya tiene un presidente de consorcio asignado ({currentName}). No puede haber más de un presidente por edificio.");
            }

            building.PresidentUserId = id;
            building.PresidentAssignedAtUtc = DateTime.UtcNow;
            building.PresidentAssignedByUserId = tenantContext.UserId;
        }
        else
        {
            // Sin edificio asignado: si no le queda ninguna presidencia, no puede conservar firma.
            owner.SignatureUrl = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var presidencies = await LoadPresidenciesAsync([owner.Id], access.Scope, cancellationToken);
        return Ok(ToDto(owner, presidencies));
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

        var access = await ResolveAccessAsync(owner.Id, cancellationToken);
        if (!access.Visible) return NotFound();
        if (!access.FullyInScope) return StatusCode(StatusCodes.Status403Forbidden, SharedOwnerMessage);

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
            || string.Equals(role, "CompanyAdmin",    StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "CompanyOperator", StringComparison.OrdinalIgnoreCase);
    }

    private ActionResult? ValidateRequest(OwnerUpsertRequest request, ApplicationUser? current = null)
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

        var profileError = OwnerProfileRules.Validate(request, current);
        if (profileError is not null) return BadRequest(profileError);

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

    // El documento de un propietario es unico dentro de la empresa (se revisa solo al crear o cuando cambia).
    private async Task<bool> DocumentExistsAsync(Guid companyId, string? documentNumber, Guid? excludeId, CancellationToken ct)
    {
        var number = PersonRules.TrimOrNull(documentNumber);
        return number is not null &&
               await dbContext.ApplicationUsers.AnyAsync(
                   x => !x.IsDeleted && x.CompanyId == companyId && x.Role == UserRole.Owner && x.DocumentNumber == number
                        && (!excludeId.HasValue || x.Id != excludeId.Value), ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private static string? NormalizeDocumentType(string? raw) =>
        raw?.Trim() is { Length: > 0 } t ? t : null;

    // ─── ALCANCE POR EDIFICIO ───────────────────────────────────────────────
    // Los propietarios son de la empresa, pero Encargado/Operador (o admin de un condominio) solo trabajan con
    // los que tienen alguna unidad en sus edificios (o ninguna todavia). Ver PeopleScope.

    private const string SharedOwnerMessage =
        "Este propietario también tiene unidades en edificios que no tenés asignados, así que no podés cambiar su " +
        "contraseña, usuario, correo, estado o residencia, ni eliminarlo. Lo debe hacer un Administrador de empresa.";

    private readonly record struct OwnerAccess(bool Visible, bool FullyInScope, HashSet<Guid>? Scope);

    private async Task<OwnerAccess> ResolveAccessAsync(Guid ownerId, CancellationToken ct)
    {
        if (accessScope.HasFullCompanyScope) return new OwnerAccess(true, true, null);

        var scope = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        var linked = await PeopleScope.BuildingsOfUserAsync(dbContext, ownerId, ct);
        return new OwnerAccess(PeopleScope.IsVisible(linked, scope), PeopleScope.IsFullyInScope(linked, scope), scope);
    }

    private async Task<Dictionary<Guid, List<OwnerPresidentBuildingDto>>> LoadPresidenciesAsync(
        List<Guid> ownerIds, HashSet<Guid>? scope, CancellationToken ct)
    {
        var buildingsQuery = dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.PresidentUserId.HasValue && ownerIds.Contains(x.PresidentUserId.Value));

        if (scope is not null)
            buildingsQuery = buildingsQuery.Where(x => scope.Contains(x.Id));

        var buildings = await buildingsQuery
            .Select(x => new { x.Id, x.Name, PresidentUserId = x.PresidentUserId!.Value })
            .ToListAsync(ct);

        return buildings
            .GroupBy(x => x.PresidentUserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new OwnerPresidentBuildingDto { BuildingId = x.Id, BuildingName = x.Name }).ToList());
    }

    private static OwnerDto ToDto(ApplicationUser u, Dictionary<Guid, List<OwnerPresidentBuildingDto>> presidencies)
    {
        var dto = new OwnerDto
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
        IsActive       = u.IsActive,
        SignatureUrl   = u.SignatureUrl,
        PresidentOfBuildings = presidencies.GetValueOrDefault(u.Id, new List<OwnerPresidentBuildingDto>())
        };
        OwnerProfileRules.Fill(dto, u);
        return dto;
    }
}
