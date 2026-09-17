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
[Route("api/users")]
public class UsersController(ICondoDbContext dbContext, IAccessScopeService accessScope, IPasswordHasher passwordHasher) : ControllerBase
{
    private static readonly Regex EmailRegex    = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
    private static readonly Regex UsernameRegex = new(@"^[a-z0-9][a-z0-9.\-_]*$",  RegexOptions.Compiled);

    private const int BuildingManagerLimit  = 2;
    private const int CompanyOperatorLimit  = 5;

    // ─── CAPACITY ───────────────────────────────────────────────────────────
    [HttpGet("capacity")]
    public async Task<ActionResult<BuildingCapacityResponse>> GetCapacity(CancellationToken cancellationToken)
    {
        if (!accessScope.IsCompanyAdmin)
            return Forbid();

        var accessibleIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var usersWithRoles = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive &&
                       (u.Role == UserRole.BuildingManager || u.Role == UserRole.CompanyOperator))
            .Select(u => new { u.Id, u.Role })
            .ToListAsync(cancellationToken);

        var userIds     = usersWithRoles.Select(u => u.Id).ToHashSet();
        var roleByUser  = usersWithRoles.ToDictionary(u => u.Id, u => u.Role);

        var accesses = await dbContext.UserBuildingAccesses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive &&
                        accessibleIds.Contains(x.BuildingId) &&
                        userIds.Contains(x.ApplicationUserId))
            .Select(x => new { x.BuildingId, x.ApplicationUserId })
            .ToListAsync(cancellationToken);

        var items = accessibleIds.Select(bid =>
        {
            var list = accesses.Where(a => a.BuildingId == bid).ToList();
            return new BuildingCapacityItem
            {
                BuildingId           = bid,
                BuildingManagerCount = list.Count(a => roleByUser.TryGetValue(a.ApplicationUserId, out var r) && r == UserRole.BuildingManager),
                CompanyOperatorCount = list.Count(a => roleByUser.TryGetValue(a.ApplicationUserId, out var r) && r == UserRole.CompanyOperator)
            };
        }).ToList();

        return Ok(new BuildingCapacityResponse { Items = items });
    }

    // ─── GET ALL ────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken cancellationToken)
    {
        IQueryable<ApplicationUser> query = dbContext.ApplicationUsers
            .AsNoTracking()
            .Include(x => x.BuildingAccesses.Where(a => !a.IsDeleted && a.IsActive))
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (!accessScope.CompanyId.HasValue) return Forbid();
            query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
        }

        var users = await query.OrderBy(x => x.FullName).ToListAsync(cancellationToken);
        return Ok(users.Select(ToDto).ToList());
    }

    // ─── GET BY ID ──────────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Include(x => x.BuildingAccesses.Where(a => !a.IsDeleted && a.IsActive))
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (user is null) return NotFound();

        if (!accessScope.IsSuperAdmin &&
            (!accessScope.CompanyId.HasValue || user.CompanyId != accessScope.CompanyId.Value))
            return Forbid();

        return Ok(ToDto(user));
    }

    // ─── CREATE ─────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] UserUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest("El rol indicado no es válido.");

        if (!accessScope.IsSuperAdmin && (role == UserRole.SuperAdmin || role == UserRole.CompanyAdmin))
            return BadRequest("Un administrador de empresa solo puede crear usuarios con alcance acotado.");

        bool isSuperAdminRole = role == UserRole.SuperAdmin;

        Guid? companyId;
        if (isSuperAdminRole)
        {
            if (!accessScope.IsSuperAdmin) return Forbid();
            companyId = null;
        }
        else
        {
            companyId = accessScope.IsSuperAdmin ? request.CompanyId : accessScope.CompanyId;
            if (!companyId.HasValue || !await accessScope.CanManageCompanyAsync(companyId.Value, cancellationToken))
                return Forbid();
        }

        var validationError = await ValidateRequestAsync(request, companyId, null, cancellationToken);
        if (validationError is not null) return validationError;

        if (!isSuperAdminRole && !accessScope.IsSuperAdmin)
        {
            var accessibleIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
            if (!request.BuildingIds.All(id => accessibleIds.Contains(id)))
                return BadRequest("Uno o más edificios están fuera del alcance de su cuenta.");

            var limitsError = await ValidateBuildingRoleLimitsAsync(role, request.BuildingIds, null, cancellationToken);
            if (limitsError is not null) return limitsError;
        }

        var normalizedEmail    = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        if (await EmailExistsAsync(companyId, normalizedEmail, null, cancellationToken))
            return Conflict(isSuperAdminRole
                ? "Ya existe un SuperAdmin con ese correo."
                : "Ya existe un usuario con ese correo dentro de la empresa.");

        if (await UsernameExistsAsync(companyId, normalizedUsername, null, cancellationToken))
            return Conflict(isSuperAdminRole
                ? "Ya existe un SuperAdmin con ese nombre de usuario."
                : "Ya existe un usuario con ese nombre de usuario dentro de la empresa.");

        var firstName = request.FirstName.Trim();
        var lastName  = request.LastName.Trim();

        var user = new ApplicationUser
        {
            CompanyId          = companyId,
            CondominiumId      = isSuperAdminRole ? null : request.CondominiumId,
            FirstName          = firstName,
            LastName           = lastName,
            FullName           = string.IsNullOrWhiteSpace(request.FullName)
                                   ? $"{firstName} {lastName}".Trim()
                                   : request.FullName.Trim(),
            Username           = normalizedUsername,
            Email              = normalizedEmail,
            PasswordHash       = passwordHasher.Hash(string.IsNullOrWhiteSpace(request.Password) ? "Condo*Temp1" : request.Password),
            PhonePrefix        = request.PhonePrefix?.Trim() ?? null,
            Phone              = request.Phone?.Trim() ?? null,
            Address            = request.Address?.Trim() ?? null,
            Role               = role,
            IsActive           = request.IsActive,
            MustChangePassword = true,
            SignatureUrl       = string.IsNullOrWhiteSpace(request.SignatureUrl) ? null : request.SignatureUrl.Trim()
        };

        dbContext.ApplicationUsers.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            if (!isSuperAdminRole && request.BuildingIds.Count > 0)
            {
                var syncError = await SyncBuildingAccessesAsync(user, request.BuildingIds, cancellationToken);
                if (syncError is not null) return syncError;
            }
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict("Ya existe un usuario con ese correo o nombre de usuario.");
        }

        return Ok(await GetUserDtoAsync(user.Id, cancellationToken));
    }

    // ─── UPDATE ─────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, [FromBody] UserUpsertRequest request, CancellationToken cancellationToken)
    {
        var user = await dbContext.ApplicationUsers
            .Include(x => x.BuildingAccesses)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (user is null) return NotFound();

        bool isExistingSuperAdmin = user.CompanyId is null;

        // SuperAdmin users can only be edited by SuperAdmins
        if (isExistingSuperAdmin && !accessScope.IsSuperAdmin)
            return Forbid();

        // Company-scoped users: verify caller can manage that company
        if (!isExistingSuperAdmin &&
            (user.CompanyId is null || !await accessScope.CanManageCompanyAsync(user.CompanyId.Value, cancellationToken)))
            return Forbid();

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest("El rol indicado no es válido.");

        if (!accessScope.IsSuperAdmin && (role == UserRole.SuperAdmin || role == UserRole.CompanyAdmin))
            return BadRequest("Un administrador de empresa solo puede asignar roles con alcance acotado.");

        // Determine target company (SuperAdmin users stay without company)
        Guid? targetCompanyId = isExistingSuperAdmin
            ? null
            : (accessScope.IsSuperAdmin ? request.CompanyId ?? user.CompanyId : user.CompanyId);

        if (!isExistingSuperAdmin)
        {
            if (!targetCompanyId.HasValue || !await accessScope.CanManageCompanyAsync(targetCompanyId.Value, cancellationToken))
                return Forbid();
        }

        var validationError = await ValidateRequestAsync(request, targetCompanyId, user.Id, cancellationToken);
        if (validationError is not null) return validationError;

        if (!isExistingSuperAdmin && !accessScope.IsSuperAdmin)
        {
            var accessibleIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
            if (!request.BuildingIds.All(bid => accessibleIds.Contains(bid)))
                return BadRequest("Uno o más edificios están fuera del alcance de su cuenta.");

            var limitsError = await ValidateBuildingRoleLimitsAsync(role, request.BuildingIds, user.Id, cancellationToken);
            if (limitsError is not null) return limitsError;
        }

        var normalizedEmail    = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        if (await EmailExistsAsync(targetCompanyId, normalizedEmail, user.Id, cancellationToken))
            return Conflict(isExistingSuperAdmin
                ? "Ya existe un SuperAdmin con ese correo."
                : "Ya existe un usuario con ese correo dentro de la empresa.");

        if (await UsernameExistsAsync(targetCompanyId, normalizedUsername, user.Id, cancellationToken))
            return Conflict(isExistingSuperAdmin
                ? "Ya existe un SuperAdmin con ese nombre de usuario."
                : "Ya existe un usuario con ese nombre de usuario dentro de la empresa.");

        var firstName = request.FirstName.Trim();
        var lastName  = request.LastName.Trim();

        user.CompanyId     = targetCompanyId;
        user.CondominiumId = isExistingSuperAdmin ? null : request.CondominiumId;
        user.FirstName     = firstName;
        user.LastName      = lastName;
        user.FullName      = string.IsNullOrWhiteSpace(request.FullName)
                               ? $"{firstName} {lastName}".Trim()
                               : request.FullName.Trim();
        user.Username      = normalizedUsername;
        user.Email         = normalizedEmail;
        user.PhonePrefix   = request.PhonePrefix?.Trim() ?? null;
        user.Phone         = request.Phone?.Trim() ?? null;
        user.Address       = request.Address?.Trim() ?? null;
        user.Role          = role;
        user.IsActive      = request.IsActive;
        user.SignatureUrl  = string.IsNullOrWhiteSpace(request.SignatureUrl) ? null : request.SignatureUrl.Trim();

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash       = passwordHasher.Hash(request.Password);
            user.MustChangePassword = true;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            if (!isExistingSuperAdmin && request.BuildingIds.Count > 0)
            {
                var syncError = await SyncBuildingAccessesAsync(user, request.BuildingIds, cancellationToken);
                if (syncError is not null) return syncError;
            }
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict("Ya existe un usuario con ese correo o nombre de usuario.");
        }

        return Ok(await GetUserDtoAsync(id, cancellationToken));
    }

    // ─── DELETE ─────────────────────────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var user = await dbContext.ApplicationUsers
            .Include(x => x.BuildingAccesses)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (user is null) return NotFound();

        if (user.CompanyId is null)
        {
            // SuperAdmin users can only be deleted by SuperAdmins
            if (!accessScope.IsSuperAdmin) return Forbid();
        }
        else
        {
            if (!await accessScope.CanManageCompanyAsync(user.CompanyId.Value, cancellationToken))
                return Forbid();
        }

        foreach (var access in user.BuildingAccesses.Where(x => !x.IsDeleted))
            access.IsDeleted = true;

        user.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ─── HELPERS ────────────────────────────────────────────────────────────

    private async Task<ActionResult?> ValidateRequestAsync(
        UserUpsertRequest request, Guid? companyId, Guid? excludeUserId, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(request.Role))
            return BadRequest("El rol es obligatorio.");

        if (!Enum.TryParse<UserRole>(request.Role, true, out var parsedRole))
            return BadRequest("El rol indicado no es válido.");

        if (!string.IsNullOrWhiteSpace(request.SignatureUrl))
        {
            if (request.SignatureUrl.Length > 500)
                return BadRequest("La URL de la firma no puede superar los 500 caracteres.");

            if (parsedRole != UserRole.BuildingManager && parsedRole != UserRole.CompanyAdmin)
                return BadRequest("Solo los encargados de edificio y administradores de empresa pueden tener una firma.");
        }

        // SuperAdmin users don't have company/building scope
        if (companyId.HasValue)
        {
            if (parsedRole == UserRole.BuildingManager && (request.BuildingIds is null || request.BuildingIds.Count == 0))
                return BadRequest("Debes asignar al menos un edificio.");

            if (request.BuildingIds.Any(x => x == Guid.Empty))
                return BadRequest("La lista de edificios contiene identificadores inválidos.");

            if (request.BuildingIds.Count != request.BuildingIds.Distinct().Count())
                return BadRequest("La lista de edificios no puede contener duplicados.");

            if (request.CondominiumId.HasValue)
            {
                var condoExists = await dbContext.Condominiums
                    .AnyAsync(x => !x.IsDeleted && x.Id == request.CondominiumId.Value && x.CompanyId == companyId.Value, ct);
                if (!condoExists)
                    return BadRequest("El condominio indicado no existe o no pertenece a la empresa.");
            }
        }

        return null;
    }

    private async Task<ActionResult?> SyncBuildingAccessesAsync(
        ApplicationUser user, IReadOnlyList<Guid> buildingIds, CancellationToken ct)
    {
        var allowedIds = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == user.CompanyId && buildingIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (allowedIds.Count != buildingIds.Distinct().Count())
            return BadRequest("Uno o más edificios no existen o no pertenecen a la empresa del usuario.");

        var desired  = allowedIds.ToHashSet();
        var existing = await dbContext.UserBuildingAccesses
            .Where(x => !x.IsDeleted && x.ApplicationUserId == user.Id)
            .ToListAsync(ct);

        foreach (var access in existing)
        {
            if (desired.Contains(access.BuildingId))
            {
                access.IsActive = true;
                desired.Remove(access.BuildingId);
            }
            else
            {
                access.IsDeleted = true;
            }
        }

        foreach (var buildingId in desired)
        {
            dbContext.UserBuildingAccesses.Add(new UserBuildingAccess
            {
                CompanyId          = user.CompanyId!.Value,
                ApplicationUserId  = user.Id,
                BuildingId         = buildingId,
                IsActive           = true
            });
        }

        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<ActionResult?> ValidateBuildingRoleLimitsAsync(
        UserRole role, IReadOnlyList<Guid> buildingIds, Guid? excludeUserId, CancellationToken ct)
    {
        if (role != UserRole.BuildingManager && role != UserRole.CompanyOperator)
            return null;

        int    limit    = role == UserRole.BuildingManager ? BuildingManagerLimit : CompanyOperatorLimit;
        string roleName = role == UserRole.BuildingManager ? "encargados" : "operadores";

        var userIdsWithRole = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && u.Role == role &&
                        (!excludeUserId.HasValue || u.Id != excludeUserId.Value))
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var buildingId in buildingIds)
        {
            var count = await dbContext.UserBuildingAccesses
                .AsNoTracking()
                .CountAsync(x => !x.IsDeleted && x.IsActive &&
                                 x.BuildingId == buildingId &&
                                 userIdsWithRole.Contains(x.ApplicationUserId), ct);

            if (count >= limit)
            {
                var buildingName = await dbContext.Buildings
                    .AsNoTracking()
                    .Where(x => x.Id == buildingId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(ct) ?? buildingId.ToString();

                return BadRequest($"El edificio \"{buildingName}\" ya tiene el máximo de {limit} {roleName} permitidos.");
            }
        }

        return null;
    }

    private async Task<bool> EmailExistsAsync(Guid? companyId, string email, Guid? excludeId, CancellationToken ct) =>
        await dbContext.ApplicationUsers.AnyAsync(
            x => !x.IsDeleted && x.CompanyId == companyId && x.Email == email
                 && (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    private async Task<bool> UsernameExistsAsync(Guid? companyId, string username, Guid? excludeId, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(username) &&
        await dbContext.ApplicationUsers.AnyAsync(
            x => !x.IsDeleted && x.CompanyId == companyId && x.Username == username
                 && (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private async Task<UserDto> GetUserDtoAsync(Guid id, CancellationToken ct)
    {
        var user = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Include(x => x.BuildingAccesses.Where(a => !a.IsDeleted && a.IsActive))
            .FirstAsync(x => x.Id == id, ct);
        return ToDto(user);
    }

    private static UserDto ToDto(ApplicationUser u) => new()
    {
        Id            = u.Id,
        CompanyId     = u.CompanyId,
        CondominiumId = u.CondominiumId,
        FirstName     = u.FirstName,
        LastName      = u.LastName,
        FullName      = u.FullName,
        Username      = u.Username,
        Email         = u.Email,
        PhonePrefix   = u.PhonePrefix,
        Phone         = u.Phone,
        Address       = u.Address,
        Role          = u.Role.ToString(),
        IsActive      = u.IsActive,
        BuildingIds   = u.BuildingAccesses
            .Where(x => !x.IsDeleted && x.IsActive)
            .Select(x => x.BuildingId)
            .ToList(),
        SignatureUrl  = u.SignatureUrl
    };
}
