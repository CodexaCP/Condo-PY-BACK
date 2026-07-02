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
[Route("api/me")]
public class MeController(ICondoDbContext dbContext, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("units")]
    public async Task<ActionResult<IReadOnlyList<MyUnitDto>>> GetMyUnits(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();

        var results = await LoadAccessibleUnitsAsync(user, cancellationToken);
        return Ok(results.OrderBy(x => x.BuildingName).ThenBy(x => x.UnitCode).ToList());
    }

    [HttpGet("claims")]
    public async Task<ActionResult<IReadOnlyList<ClaimDto>>> GetMyClaims(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        if (user.Role is not (UserRole.Owner or UserRole.Resident)) return Forbid();

        var accessibleUnitIds = (await LoadAccessibleUnitsAsync(user, cancellationToken))
            .Select(x => x.UnitId)
            .ToHashSet();

        var items = await dbContext.Claims
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedByUserId == user.Id && accessibleUnitIds.Contains(x.UnitId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(ToClaimDto())
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("claims")]
    public async Task<ActionResult<ClaimDto>> CreateClaim(
        [FromBody] ClaimCreateRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        if (user.Role is not (UserRole.Owner or UserRole.Resident)) return Forbid();

        if (!IsValidClaimCategory(request.Category))
            return BadRequest("Categoría inválida.");

        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("La descripción es obligatoria.");

        var description = request.Description.Trim();
        if (description.Length > 2000)
            return BadRequest("La descripción no puede superar los 2000 caracteres.");

        var selectedUnit = (await LoadAccessibleUnitsAsync(user, cancellationToken))
            .FirstOrDefault(x => x.UnitId == request.UnitId);

        if (selectedUnit is null) return Forbid();

        var unitContext = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == request.UnitId)
            .Select(x => new
            {
                UnitId = x.Id,
                x.Code,
                x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                CondominiumId = x.Building != null ? x.Building.CondominiumId : null,
                CondominiumName = x.Building != null && x.Building.Condominium != null ? x.Building.Condominium.Name : string.Empty,
                CompanyId = x.CompanyId != Guid.Empty
                    ? x.CompanyId
                    : x.Building != null && x.Building.CompanyId.HasValue
                        ? x.Building.CompanyId.Value
                        : x.Building != null && x.Building.Condominium != null && x.Building.Condominium.CompanyId.HasValue
                            ? x.Building.Condominium.CompanyId.Value
                            : Guid.Empty
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (unitContext is null || unitContext.CondominiumId is null || unitContext.CompanyId == Guid.Empty)
            return BadRequest("No se pudo resolver el contexto de la unidad.");

        var claim = new Claim
        {
            CompanyId = unitContext.CompanyId,
            CondominiumId = unitContext.CondominiumId.Value,
            BuildingId = unitContext.BuildingId,
            UnitId = unitContext.UnitId,
            CreatedByUserId = user.Id,
            Category = request.Category.Trim(),
            Description = description,
            Status = "Pendiente"
        };

        dbContext.Claims.Add(claim);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = await dbContext.Claims
            .AsNoTracking()
            .Where(x => x.Id == claim.Id)
            .Select(ToClaimDto())
            .FirstAsync(cancellationToken);

        return CreatedAtAction(nameof(GetMyClaims), dto);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = tenantContext.UserId;
        return await dbContext.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Id == userId, cancellationToken);
    }

    private async Task<List<MyUnitDto>> LoadAccessibleUnitsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var results = new List<MyUnitDto>();

        if (user.Role == UserRole.Owner)
        {
            var ownerUnits = await dbContext.UnitOwners
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.OwnerId == user.Id)
                .Include(x => x.Unit)
                    .ThenInclude(u => u!.Building)
                        .ThenInclude(b => b!.Condominium)
                .ToListAsync(cancellationToken);

            foreach (var ou in ownerUnits.Where(x => x.Unit is not null))
            {
                var unit = ou.Unit!;
                var building = unit.Building!;

                results.Add(new MyUnitDto
                {
                    UnitId = unit.Id,
                    UnitCode = unit.Code,
                    Floor = unit.Floor,
                    Coefficient = unit.Coefficient,
                    BuildingId = building.Id,
                    BuildingName = building.Name,
                    BuildingCode = building.Code,
                    BuildingAddress = building.Address,
                    CondominiumId = building.CondominiumId,
                    CondominiumName = building.Condominium?.Name,
                    RelationRole = "Owner",
                    IsPrimary = ou.IsPrimary
                });
            }
        }

        if (user.Role == UserRole.Resident)
        {
            var residentUnits = await dbContext.UnitResidents
                .AsNoTracking()
                .Where(x => !x.IsDeleted
                         && x.EndDate == null
                         && x.Resident != null
                         && x.Resident.Email == user.Email
                         && !x.Resident.IsDeleted)
                .Include(x => x.Resident)
                .Include(x => x.Unit)
                    .ThenInclude(u => u!.Building)
                        .ThenInclude(b => b!.Condominium)
                .ToListAsync(cancellationToken);

            foreach (var ur in residentUnits.Where(x => x.Unit is not null))
            {
                var unit = ur.Unit!;
                var building = unit.Building!;

                results.Add(new MyUnitDto
                {
                    UnitId = unit.Id,
                    UnitCode = unit.Code,
                    Floor = unit.Floor,
                    Coefficient = unit.Coefficient,
                    BuildingId = building.Id,
                    BuildingName = building.Name,
                    BuildingCode = building.Code,
                    BuildingAddress = building.Address,
                    CondominiumId = building.CondominiumId,
                    CondominiumName = building.Condominium?.Name,
                    RelationRole = "Resident",
                    IsPrimary = ur.IsPrimary
                });
            }
        }

        return results;
    }

    private static System.Linq.Expressions.Expression<Func<Claim, ClaimDto>> ToClaimDto() =>
        x => new ClaimDto
        {
            Id = x.Id,
            CondominiumId = x.CondominiumId,
            CondominiumName = x.Condominium.Name,
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

    [HttpGet("announcements")]
    public async Task<ActionResult<IReadOnlyList<AnnouncementDto>>> GetMyAnnouncements(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        if (user.Role is not (UserRole.Owner or UserRole.Resident)) return Forbid();

        var myUnits = await LoadAccessibleUnitsAsync(user, cancellationToken);
        var buildingIds = myUnits.Select(x => x.BuildingId).Distinct().ToHashSet();

        if (buildingIds.Count == 0)
            return Ok(Array.Empty<AnnouncementDto>());

        var now = DateTime.UtcNow;
        var items = await dbContext.Announcements
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                     && x.IsActive
                     && buildingIds.Contains(x.BuildingId)
                     && (x.PublishedAt == null || x.PublishedAt <= now)
                     && (x.ExpiresAt == null || x.ExpiresAt >= now))
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAtUtc)
            .Select(x => new AnnouncementDto
            {
                Id = x.Id,
                BuildingId = x.BuildingId,
                BuildingName = x.Building.Name,
                Title = x.Title,
                Body = x.Body,
                Category = x.Category,
                PublishedAt = x.PublishedAt,
                ExpiresAt = x.ExpiresAt,
                IsActive = x.IsActive,
                CreatedByUserId = x.CreatedByUserId,
                CreatedByName = x.CreatedBy != null ? x.CreatedBy.FullName : string.Empty,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    private static bool IsValidClaimCategory(string? category) =>
        category is "Ruido" or "Limpieza" or "Mantenimiento" or "Otro";
}
