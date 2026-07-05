using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/amenities")]
public class AmenitiesController(
    CondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext) : ControllerBase
{
    private static readonly AmenityReservationStatus[] BlockingStatuses =
    [
        AmenityReservationStatus.PendingPayment,
        AmenityReservationStatus.PendingReview,
        AmenityReservationStatus.Confirmed
    ];

    // ─────────────────────────── GESTIÓN (manager/admin) ───────────────────────────

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
    public async Task<ActionResult<IReadOnlyList<AmenityDto>>> GetAll([FromQuery] Guid? buildingId, CancellationToken ct)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(ct);

        var query = dbContext.Amenities.AsNoTracking().Where(x => !x.IsDeleted);
        if (buildingId.HasValue)
        {
            if (!accessScope.IsSuperAdmin && !accessibleBuildingIds.Contains(buildingId.Value)) return Forbid();
            query = query.Where(x => x.BuildingId == buildingId.Value);
        }
        else if (!accessScope.IsSuperAdmin)
        {
            query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
        }

        return Ok(await query.OrderBy(x => x.Building!.Name).ThenBy(x => x.Name).Select(ToAmenityDto()).ToListAsync(ct));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
    public async Task<ActionResult<AmenityDto>> Create([FromBody] AmenityUpsertRequest request, CancellationToken ct)
    {
        var error = Validate(request);
        if (error is not null) return BadRequest(error);

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(request.BuildingId, ct)) return Forbid();

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, ct);
        if (building is null) return BadRequest("El edificio no existe.");

        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue) return BadRequest("No se pudo resolver la empresa del edificio.");

        var name = request.Name.Trim();
        var duplicated = await dbContext.Amenities
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == request.BuildingId && x.Name == name, ct);
        if (duplicated) return Conflict("Ya existe un amenity con ese nombre en el edificio.");

        var entity = new Amenity
        {
            CompanyId = companyId.Value,
            BuildingId = request.BuildingId,
            Name = name,
            Description = request.Description.Trim(),
            ReservationPrice = decimal.Round(request.ReservationPrice, 2),
            IsActive = request.IsActive
        };

        dbContext.Amenities.Add(entity);
        await dbContext.SaveChangesAsync(ct);

        var dto = await dbContext.Amenities.AsNoTracking().Where(x => x.Id == entity.Id).Select(ToAmenityDto()).FirstAsync(ct);
        return CreatedAtAction(nameof(GetAll), new { buildingId = entity.BuildingId }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
    public async Task<ActionResult<AmenityDto>> Update(Guid id, [FromBody] AmenityUpsertRequest request, CancellationToken ct)
    {
        var error = Validate(request);
        if (error is not null) return BadRequest(error);

        var entity = await dbContext.Amenities.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        if (entity is null) return NotFound();

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(entity.BuildingId, ct)) return Forbid();

        var name = request.Name.Trim();
        var duplicated = await dbContext.Amenities
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == entity.BuildingId && x.Id != id && x.Name == name, ct);
        if (duplicated) return Conflict("Ya existe un amenity con ese nombre en el edificio.");

        entity.Name = name;
        entity.Description = request.Description.Trim();
        entity.ReservationPrice = decimal.Round(request.ReservationPrice, 2);
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);

        var dto = await dbContext.Amenities.AsNoTracking().Where(x => x.Id == id).Select(ToAmenityDto()).FirstAsync(ct);
        return Ok(dto);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await dbContext.Amenities.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        if (entity is null) return NotFound();

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(entity.BuildingId, ct)) return Forbid();

        var hasFutureReservations = await dbContext.AmenityReservations
            .AnyAsync(x => !x.IsDeleted && x.AmenityId == id
                        && BlockingStatuses.Contains(x.Status)
                        && x.EndsAt > DateTime.UtcNow, ct);
        if (hasFutureReservations) return BadRequest("No se puede eliminar: el amenity tiene reservas vigentes o futuras.");

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("reservations")]
    [Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
    public async Task<ActionResult<IReadOnlyList<AmenityReservationDto>>> GetReservations(
        [FromQuery] Guid? buildingId, [FromQuery] string? status, CancellationToken ct)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(ct);

        var query = dbContext.AmenityReservations.AsNoTracking().Where(x => !x.IsDeleted);
        if (buildingId.HasValue)
        {
            if (!accessScope.IsSuperAdmin && !accessibleBuildingIds.Contains(buildingId.Value)) return Forbid();
            query = query.Where(x => x.BuildingId == buildingId.Value);
        }
        else if (!accessScope.IsSuperAdmin)
        {
            query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AmenityReservationStatus>(status, true, out var parsed))
        {
            query = query.Where(x => x.Status == parsed);
        }

        return Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Select(ToReservationDto()).ToListAsync(ct));
    }

    [HttpPost("reservations/{id:guid}/review")]
    [Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
    public async Task<ActionResult<AmenityReservationDto>> Review(
        Guid id, [FromBody] AmenityReservationReviewRequest request, CancellationToken ct)
    {
        var reservation = await dbContext.AmenityReservations
            .Include(x => x.Amenity)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        if (reservation is null) return NotFound();

        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(reservation.BuildingId, ct)) return Forbid();

        if (reservation.Status is not (AmenityReservationStatus.PendingPayment or AmenityReservationStatus.PendingReview))
            return BadRequest("La reserva ya fue procesada.");

        reservation.ReviewedByUserId = tenantContext.UserId;
        reservation.ReviewedAtUtc = DateTime.UtcNow;

        if (request.Approve)
        {
            reservation.Status = AmenityReservationStatus.Confirmed;

            // Comunicado global automático, sin identificar al usuario
            var startsLocal = DateTime.SpecifyKind(reservation.StartsAt, DateTimeKind.Utc).ToLocalTime();
            var endsLocal = DateTime.SpecifyKind(reservation.EndsAt, DateTimeKind.Utc).ToLocalTime();
            var rangeText = startsLocal.Date == endsLocal.Date
                ? $"el {startsLocal:dd/MM/yyyy} de {startsLocal:HH:mm} a {endsLocal:HH:mm} hs"
                : $"del {startsLocal:dd/MM/yyyy HH:mm} al {endsLocal:dd/MM/yyyy HH:mm} hs";

            dbContext.Announcements.Add(new Announcement
            {
                BuildingId = reservation.BuildingId,
                Title = $"{reservation.Amenity!.Name} reservado",
                Body = $"{reservation.Amenity.Name} del edificio reservado {rangeText} — evento privado. No estará disponible en ese horario.",
                Category = "General",
                PublishedAt = DateTime.UtcNow,
                ExpiresAt = reservation.EndsAt,
                IsActive = true,
                CreatedByUserId = tenantContext.UserId
            });

            dbContext.Notifications.Add(new Notification
            {
                CompanyId = reservation.CompanyId,
                RecipientId = reservation.ReservedByUserId,
                Type = NotificationType.AmenityReservationUpdated,
                Title = "Reserva confirmada",
                Body = $"Tu reserva de {reservation.Amenity.Name} {FormatRange(reservation)} fue confirmada.",
                EntityType = "AmenityReservation",
                EntityId = reservation.Id
            });
        }
        else
        {
            reservation.Status = AmenityReservationStatus.Rejected;
            reservation.RejectionReason = string.IsNullOrWhiteSpace(request.RejectionReason) ? null : request.RejectionReason.Trim();

            dbContext.Notifications.Add(new Notification
            {
                CompanyId = reservation.CompanyId,
                RecipientId = reservation.ReservedByUserId,
                Type = NotificationType.AmenityReservationUpdated,
                Title = "Reserva rechazada",
                Body = $"Tu reserva de {reservation.Amenity!.Name} {FormatRange(reservation)} fue rechazada." +
                       (reservation.RejectionReason is null ? string.Empty : $" Motivo: {reservation.RejectionReason}"),
                EntityType = "AmenityReservation",
                EntityId = reservation.Id
            });
        }

        await dbContext.SaveChangesAsync(ct);

        var dto = await dbContext.AmenityReservations.AsNoTracking().Where(x => x.Id == id).Select(ToReservationDto()).FirstAsync(ct);
        return Ok(dto);
    }

    // ─────────────────────────── APP (propietarios/residentes) ───────────────────────────

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<AmenityDto>>> GetMine(CancellationToken ct)
    {
        var buildingIds = await GetMyBuildingIdsAsync(ct);
        if (buildingIds.Count == 0) return Ok(Array.Empty<AmenityDto>());

        var items = await dbContext.Amenities
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && buildingIds.Contains(x.BuildingId))
            .OrderBy(x => x.Building!.Name).ThenBy(x => x.Name)
            .Select(ToAmenityDto())
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{id:guid}/schedule")]
    public async Task<ActionResult<IReadOnlyList<AmenityScheduleSlotDto>>> GetSchedule(
        Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var amenity = await dbContext.Amenities.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        if (amenity is null) return NotFound();

        if (!await CanUseAmenityAsync(amenity.BuildingId, ct)) return Forbid();

        var rangeFrom = from ?? DateTime.UtcNow.AddDays(-1);
        var rangeTo = to ?? DateTime.UtcNow.AddDays(60);

        var slots = await dbContext.AmenityReservations
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.AmenityId == id
                     && BlockingStatuses.Contains(x.Status)
                     && x.StartsAt < rangeTo && x.EndsAt > rangeFrom)
            .OrderBy(x => x.StartsAt)
            .Select(x => new AmenityScheduleSlotDto { StartsAt = x.StartsAt, EndsAt = x.EndsAt, Status = x.Status })
            .ToListAsync(ct);

        return Ok(slots);
    }

    [HttpPost("{id:guid}/reservations")]
    public async Task<ActionResult<AmenityReservationDto>> Reserve(
        Guid id, [FromBody] CreateAmenityReservationRequest request, CancellationToken ct)
    {
        if (request.StartsAt >= request.EndsAt)
            return BadRequest("La hora de fin debe ser posterior a la de inicio.");
        if (request.EndsAt <= DateTime.UtcNow)
            return BadRequest("No se puede reservar en el pasado.");

        var amenity = await dbContext.Amenities
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Id == id, ct);
        if (amenity is null) return NotFound();

        if (!await CanUseAmenityAsync(amenity.BuildingId, ct)) return Forbid();

        // Transacción serializable: el chequeo de solape y la inserción son atómicos.
        // Ante dos usuarios simultáneos, gana el primero que entra; el segundo recibe el conflicto.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var conflict = await dbContext.AmenityReservations
            .Where(x => !x.IsDeleted && x.AmenityId == id
                     && BlockingStatuses.Contains(x.Status)
                     && x.StartsAt < request.EndsAt && request.StartsAt < x.EndsAt)
            .OrderBy(x => x.StartsAt)
            .Select(x => new { x.StartsAt, x.EndsAt })
            .FirstOrDefaultAsync(ct);

        if (conflict is not null)
        {
            await transaction.RollbackAsync(ct);
            var cs = DateTime.SpecifyKind(conflict.StartsAt, DateTimeKind.Utc).ToLocalTime();
            var ce = DateTime.SpecifyKind(conflict.EndsAt, DateTimeKind.Utc).ToLocalTime();
            return Conflict($"El amenity ya está reservado el {cs:dd/MM/yyyy} de {cs:HH:mm} a {ce:HH:mm} hs. Podés reservar a partir de las {ce:HH:mm} hs.");
        }

        var reservation = new AmenityReservation
        {
            CompanyId = amenity.CompanyId,
            AmenityId = amenity.Id,
            BuildingId = amenity.BuildingId,
            ReservedByUserId = tenantContext.UserId,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Price = amenity.ReservationPrice,
            Status = AmenityReservationStatus.PendingPayment,
            Notes = request.Notes.Trim()
        };

        dbContext.AmenityReservations.Add(reservation);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var dto = await dbContext.AmenityReservations.AsNoTracking().Where(x => x.Id == reservation.Id).Select(ToReservationDto()).FirstAsync(ct);
        return CreatedAtAction(nameof(GetMyReservations), dto);
    }

    [HttpGet("reservations/mine")]
    public async Task<ActionResult<IReadOnlyList<AmenityReservationDto>>> GetMyReservations(CancellationToken ct)
    {
        var userId = tenantContext.UserId;
        var items = await dbContext.AmenityReservations
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ReservedByUserId == userId)
            .OrderByDescending(x => x.StartsAt)
            .Select(ToReservationDto())
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("reservations/{id:guid}/comprobante")]
    public async Task<ActionResult<AmenityReservationDto>> AttachComprobante(
        Guid id, [FromBody] AmenityReservationComprobanteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ComprobanteUrl))
            return BadRequest("El comprobante es obligatorio.");

        var reservation = await dbContext.AmenityReservations
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.ReservedByUserId == tenantContext.UserId, ct);
        if (reservation is null) return NotFound();

        if (reservation.Status is not (AmenityReservationStatus.PendingPayment or AmenityReservationStatus.PendingReview))
            return BadRequest("La reserva ya fue procesada.");

        reservation.ComprobanteUrl = request.ComprobanteUrl.Trim();
        reservation.Status = AmenityReservationStatus.PendingReview;
        await dbContext.SaveChangesAsync(ct);

        var dto = await dbContext.AmenityReservations.AsNoTracking().Where(x => x.Id == id).Select(ToReservationDto()).FirstAsync(ct);
        return Ok(dto);
    }

    [HttpPost("reservations/{id:guid}/cancel")]
    public async Task<ActionResult<AmenityReservationDto>> Cancel(Guid id, CancellationToken ct)
    {
        var reservation = await dbContext.AmenityReservations
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.ReservedByUserId == tenantContext.UserId, ct);
        if (reservation is null) return NotFound();

        if (reservation.Status is not (AmenityReservationStatus.PendingPayment or AmenityReservationStatus.PendingReview))
            return BadRequest("Solo se pueden cancelar reservas pendientes. Contactá a la administración.");

        reservation.Status = AmenityReservationStatus.Cancelled;
        await dbContext.SaveChangesAsync(ct);

        var dto = await dbContext.AmenityReservations.AsNoTracking().Where(x => x.Id == id).Select(ToReservationDto()).FirstAsync(ct);
        return Ok(dto);
    }

    // ─────────────────────────── helpers ───────────────────────────

    private static string? Validate(AmenityUpsertRequest request)
    {
        if (request.BuildingId == Guid.Empty) return "El edificio es obligatorio.";
        if (string.IsNullOrWhiteSpace(request.Name)) return "El nombre es obligatorio.";
        if (request.ReservationPrice < 0m) return "El precio de reserva no puede ser negativo.";
        return null;
    }

    private static string FormatRange(AmenityReservation reservation)
    {
        var s = DateTime.SpecifyKind(reservation.StartsAt, DateTimeKind.Utc).ToLocalTime();
        var e = DateTime.SpecifyKind(reservation.EndsAt, DateTimeKind.Utc).ToLocalTime();
        return s.Date == e.Date
            ? $"del {s:dd/MM/yyyy} ({s:HH:mm} a {e:HH:mm} hs)"
            : $"del {s:dd/MM/yyyy HH:mm} al {e:dd/MM/yyyy HH:mm}";
    }

    private async Task<bool> CanUseAmenityAsync(Guid buildingId, CancellationToken ct)
    {
        if (accessScope.IsSuperAdmin || await accessScope.CanAccessBuildingAsync(buildingId, ct)) return true;
        var buildingIds = await GetMyBuildingIdsAsync(ct);
        return buildingIds.Contains(buildingId);
    }

    private async Task<HashSet<Guid>> GetMyBuildingIdsAsync(CancellationToken ct)
    {
        var userId = tenantContext.UserId;
        var user = await dbContext.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Id == userId, ct);
        if (user is null) return [];

        var result = new HashSet<Guid>();

        if (user.Role == UserRole.Owner)
        {
            result.UnionWith(await dbContext.UnitOwners
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.OwnerId == user.Id && x.Unit != null && !x.Unit.IsDeleted)
                .Select(x => x.Unit!.BuildingId)
                .ToListAsync(ct));
        }

        if (user.Role == UserRole.Resident)
        {
            result.UnionWith(await dbContext.UnitResidents
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.EndDate == null
                         && x.Resident != null && !x.Resident.IsDeleted && x.Resident.Email == user.Email
                         && x.Unit != null && !x.Unit.IsDeleted)
                .Select(x => x.Unit!.BuildingId)
                .ToListAsync(ct));
        }

        return result;
    }

    private static System.Linq.Expressions.Expression<Func<Amenity, AmenityDto>> ToAmenityDto() =>
        x => new AmenityDto
        {
            Id = x.Id,
            BuildingId = x.BuildingId,
            BuildingName = x.Building != null ? x.Building.Name : string.Empty,
            Name = x.Name,
            Description = x.Description,
            ReservationPrice = x.ReservationPrice,
            IsActive = x.IsActive
        };

    private static System.Linq.Expressions.Expression<Func<AmenityReservation, AmenityReservationDto>> ToReservationDto() =>
        x => new AmenityReservationDto
        {
            Id = x.Id,
            AmenityId = x.AmenityId,
            AmenityName = x.Amenity != null ? x.Amenity.Name : string.Empty,
            BuildingId = x.BuildingId,
            BuildingName = x.Building != null ? x.Building.Name : string.Empty,
            StartsAt = x.StartsAt,
            EndsAt = x.EndsAt,
            Price = x.Price,
            Status = x.Status,
            Notes = x.Notes,
            ComprobanteUrl = x.ComprobanteUrl,
            ReservedByName = x.ReservedByUser != null ? x.ReservedByUser.FullName : string.Empty,
            RejectionReason = x.RejectionReason,
            CreatedAtUtc = x.CreatedAtUtc
        };
}
