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
[Route("api/announcements")]
public class AnnouncementsController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AnnouncementDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        if (buildingId.HasValue && !accessScope.IsSuperAdmin && !accessibleBuildingIds.Contains(buildingId.Value))
            return Forbid();

        var query = dbContext.Announcements
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (buildingId.HasValue)
        {
            query = query.Where(x => x.BuildingId == buildingId.Value);
        }
        else if (!accessScope.IsSuperAdmin)
        {
            query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AnnouncementDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.Announcements
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
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
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null) return NotFound();

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(item.BuildingId, cancellationToken))
            return Forbid();

        return Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<AnnouncementDto>> Create(
        [FromBody] AnnouncementUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(request.BuildingId, cancellationToken))
            return Forbid();

        var validationError = Validate(request.Title, request.Body, request.Category);
        if (validationError is not null) return BadRequest(validationError);

        var entity = BuildEntity(request.BuildingId, request);
        dbContext.Announcements.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (entity.IsActive)
            await NotifyBuildingUsersAsync(entity.BuildingId, entity.Id, entity.Title, cancellationToken);

        var dto = await LoadDtoAsync(entity.Id, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AnnouncementDto>> Update(
        Guid id,
        [FromBody] AnnouncementUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Announcements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
            return Forbid();

        var validationError = Validate(request.Title, request.Body, request.Category);
        if (validationError is not null) return BadRequest(validationError);

        var wasInactive = !entity.IsActive;

        entity.Title = request.Title.Trim();
        entity.Body = request.Body.Trim();
        entity.Category = request.Category.Trim();
        entity.PublishedAt = request.PublishedAt;
        entity.ExpiresAt = request.ExpiresAt;
        entity.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync(cancellationToken);

        if (request.IsActive && wasInactive)
            await NotifyBuildingUsersAsync(entity.BuildingId, entity.Id, entity.Title, cancellationToken);

        var dto = await LoadDtoAsync(entity.Id, cancellationToken);
        return Ok(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Announcements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
            return Forbid();

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Crea el mismo comunicado en múltiples edificios (vacío = todos los accesibles).
    [HttpPost("broadcast")]
    public async Task<ActionResult<IReadOnlyList<AnnouncementDto>>> Broadcast(
        [FromBody] AnnouncementBroadcastRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request.Title, request.Body, request.Category);
        if (validationError is not null) return BadRequest(validationError);

        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        HashSet<Guid> targetIds;
        if (request.BuildingIds.Count > 0)
        {
            targetIds = request.BuildingIds.ToHashSet();
            if (!accessScope.IsSuperAdmin)
                targetIds.IntersectWith(accessibleBuildingIds);
        }
        else
        {
            targetIds = accessibleBuildingIds;
        }

        if (targetIds.Count == 0)
            return BadRequest("No hay edificios destino accesibles.");

        var entities = targetIds.Select(buildingId =>
        {
            var upsert = new AnnouncementUpsertRequest
            {
                BuildingId = buildingId,
                Title = request.Title,
                Body = request.Body,
                Category = request.Category,
                PublishedAt = request.PublishedAt,
                ExpiresAt = request.ExpiresAt,
                IsActive = request.IsActive
            };
            return BuildEntity(buildingId, upsert);
        }).ToList();

        dbContext.Announcements.AddRange(entities);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var e in entities.Where(x => x.IsActive))
            await NotifyBuildingUsersAsync(e.BuildingId, e.Id, e.Title, cancellationToken);

        var ids = entities.Select(x => x.Id).ToHashSet();
        var dtos = await dbContext.Announcements
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
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

        return Ok(dtos);
    }

    private async Task NotifyBuildingUsersAsync(Guid buildingId, Guid announcementId, string title, CancellationToken ct)
    {
        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId, ct);

        var companyId = building?.CompanyId ?? building?.Condominium?.CompanyId;
        if (companyId is null) return;

        var recipientIds = await GetBuildingUserIdsAsync(buildingId, ct);
        if (recipientIds.Count == 0) return;

        foreach (var rid in recipientIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = companyId.Value,
                RecipientId = rid,
                Type = NotificationType.AnnouncementPublished,
                Title = "Nuevo comunicado",
                Body = title,
                EntityType = "Announcement",
                EntityId = announcementId
            });
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>> GetBuildingUserIdsAsync(Guid buildingId, CancellationToken ct)
    {
        var ownerIds = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId)
            .Select(x => x.OwnerId)
            .ToListAsync(ct);

        var residentEmails = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                     && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId
                     && x.Resident != null && !x.Resident.IsDeleted)
            .Select(x => x.Resident!.Email)
            .Distinct()
            .ToListAsync(ct);

        var residentUserIds = residentEmails.Count > 0
            ? await dbContext.ApplicationUsers
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive && residentEmails.Contains(x.Email))
                .Select(x => x.Id)
                .ToListAsync(ct)
            : [];

        return ownerIds.Concat(residentUserIds).Distinct().ToList();
    }

    private Announcement BuildEntity(Guid buildingId, AnnouncementUpsertRequest r) =>
        new()
        {
            BuildingId = buildingId,
            Title = r.Title.Trim(),
            Body = r.Body.Trim(),
            Category = r.Category.Trim(),
            PublishedAt = r.PublishedAt,
            ExpiresAt = r.ExpiresAt,
            IsActive = r.IsActive,
            CreatedByUserId = tenant.UserId
        };

    private async Task<AnnouncementDto?> LoadDtoAsync(Guid id, CancellationToken ct) =>
        await dbContext.Announcements
            .AsNoTracking()
            .Where(x => x.Id == id)
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
            .FirstOrDefaultAsync(ct);

    private static string? Validate(string title, string body, string category)
    {
        if (string.IsNullOrWhiteSpace(title)) return "El título es obligatorio.";
        if (title.Length > 200) return "El título no puede superar los 200 caracteres.";
        if (string.IsNullOrWhiteSpace(body)) return "El contenido es obligatorio.";
        var validCategories = new[] { "General", "Mantenimiento", "Seguridad", "Financiero", "Convocatoria", "Otro" };
        if (!validCategories.Contains(category)) return "Categoría inválida.";
        return null;
    }
}
