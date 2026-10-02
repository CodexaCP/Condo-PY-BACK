using System.Data;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Publicaciones del marketplace: el propietario PRINCIPAL de una unidad la publica por horas. Toda regla se valida aca,
/// en el backend: titularidad vigente, modulo habilitado, ventana, precio, y que una unidad no tenga dos publicaciones
/// solapadas (la comprobacion corre en una transaccion Serializable para que dos pedidos simultaneos no la burlen).
/// </summary>
public class MarketplaceListingService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit)
{
    private const int TitleMaxLength = 200;
    private const int ReasonMaxLength = 500;

    // Reservas "vivas": las que ocupan horario. Las finales (vencida, cancelada, rechazada, completada) ya no.
    private static readonly MarketplaceReservationStatus[] LiveReservationStatuses =
    [
        MarketplaceReservationStatus.PendingPayment,
        MarketplaceReservationStatus.InReview,
        MarketplaceReservationStatus.Confirmed
    ];

    // ── Consultas ────────────────────────────────────────────────────────────

    /// <summary>Unidades del edificio que el usuario puede publicar: solo las de las que es propietario principal.</summary>
    public async Task<MarketplaceResult<List<MarketplacePublishableUnitDto>>> GetPublishableUnitsAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<List<MarketplacePublishableUnitDto>>.Fail(MarketplaceError.FromAccess(access));
        }

        var units = await scope.GetPrimaryOwnedUnitsAsync(tenant.UserId, buildingId, ct);
        return MarketplaceResult<List<MarketplacePublishableUnitDto>>.Success(
            units.Select(x => new MarketplacePublishableUnitDto { UnitId = x.UnitId, Code = x.Code, Floor = x.Floor }).ToList());
    }

    /// <summary>Mis publicaciones en el edificio.</summary>
    public async Task<MarketplaceResult<List<MarketplaceListingDto>>> GetMineAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<List<MarketplaceListingDto>>.Fail(MarketplaceError.FromAccess(access));
        }

        await SyncOwnershipAsync(buildingId, ct);

        var query = db.MarketplaceListings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.OwnerId == tenant.UserId);
        return MarketplaceResult<List<MarketplaceListingDto>>.Success(await ToDtosAsync(query, ct));
    }

    /// <summary>Todas las publicaciones del edificio, para el personal que lo administra.</summary>
    public async Task<MarketplaceResult<List<MarketplaceListingDto>>> GetAllForStaffAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<List<MarketplaceListingDto>>.Fail(MarketplaceError.FromAccess(access));
        }

        if (!access.Context!.IsStaff)
        {
            return MarketplaceResult<List<MarketplaceListingDto>>.Fail(
                MarketplaceError.Forbidden("Solo el personal del edificio ve todas las publicaciones."));
        }

        await SyncOwnershipAsync(buildingId, ct);

        var query = db.MarketplaceListings.AsNoTracking().Where(x => !x.IsDeleted && x.BuildingId == buildingId);
        return MarketplaceResult<List<MarketplaceListingDto>>.Success(await ToDtosAsync(query, ct));
    }

    // ── Publicar ─────────────────────────────────────────────────────────────

    public async Task<MarketplaceResult<MarketplaceListingDto>> CreateAsync(MarketplaceListingCreateRequest request, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(request.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.FromAccess(access));
        }

        var invalid = ValidateContent(request.Title, request.HourlyPrice, out var title);
        if (invalid is not null)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.BadRequest(invalid));
        }

        var start = AsUtc(request.WindowStartUtc);
        var end = AsUtc(request.WindowEndUtc);
        var windowError = MarketplaceWindowRules.ValidateListingWindow(start, end, DateTime.UtcNow);
        if (windowError is not null)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.BadRequest(windowError));
        }

        var unit = await db.Units.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId && x.BuildingId == request.BuildingId, ct);
        if (unit is null)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.NotFound("No se encontró la unidad."));
        }

        // La titularidad se valida en el momento contra UnitOwner: ser residente o copropietario no alcanza.
        if (!await scope.IsPrimaryOwnerOfUnitAsync(tenant.UserId, unit.Id, ct))
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(
                MarketplaceError.Forbidden("Solo el propietario principal de la unidad puede publicarla."));
        }

        MarketplaceResult<MarketplaceListingDto>? result = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            if (await OverlapsAnotherAsync(unit.Id, null, start, end, ct))
            {
                await transaction.RollbackAsync(ct);
                result = MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict(
                    "Esa unidad ya tiene una publicación en ese horario. Elegí otro horario o cerrá la otra publicación."));
                return;
            }

            var listing = new MarketplaceListing
            {
                CompanyId = access.Context!.CompanyId,
                BuildingId = request.BuildingId,
                UnitId = unit.Id,
                OwnerId = tenant.UserId,
                Title = title,
                WindowStartUtc = start,
                WindowEndUtc = end,
                HourlyPrice = request.HourlyPrice,
                Status = MarketplaceListingStatus.Active
            };
            db.MarketplaceListings.Add(listing);

            audit.Record(listing.CompanyId, listing.BuildingId, nameof(MarketplaceListing), listing.Id,
                MarketplaceEventActions.ListingCreated, null, listing.Status.ToString(),
                new { listing.UnitId, listing.Title, listing.WindowStartUtc, listing.WindowEndUtc, listing.HourlyPrice });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            result = MarketplaceResult<MarketplaceListingDto>.Success(new MarketplaceListingDto { Id = listing.Id });
        });

        return result!.Ok
            ? MarketplaceResult<MarketplaceListingDto>.Success(await LoadDtoAsync(result.Value!.Id, ct))
            : result;
    }

    // ── Editar ───────────────────────────────────────────────────────────────

    public async Task<MarketplaceResult<MarketplaceListingDto>> UpdateAsync(Guid id, MarketplaceListingUpdateRequest request, CancellationToken ct)
    {
        var current = await db.MarketplaceListings.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        if (current is null)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(current.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.FromAccess(access));
        }

        // Solo su dueño la edita; para cualquier otro la publicacion "no existe".
        if (current.OwnerId != tenant.UserId)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.NotFound());
        }

        if (!await scope.IsPrimaryOwnerOfUnitAsync(tenant.UserId, current.UnitId, ct))
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(
                MarketplaceError.Forbidden("Ya no sos el propietario principal de la unidad: no podés editar esta publicación."));
        }

        if (current.Status == MarketplaceListingStatus.Closed)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict("La publicación está cerrada."));
        }

        var invalid = ValidateContent(request.Title, request.HourlyPrice, out var title);
        if (invalid is not null)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.BadRequest(invalid));
        }

        var start = AsUtc(request.WindowStartUtc);
        var end = AsUtc(request.WindowEndUtc);
        var windowChanged = start != AsUtc(current.WindowStartUtc) || end != AsUtc(current.WindowEndUtc);
        if (windowChanged)
        {
            if (AsUtc(current.WindowStartUtc) <= DateTime.UtcNow)
            {
                return MarketplaceResult<MarketplaceListingDto>.Fail(
                    MarketplaceError.Conflict("La publicación ya empezó: no se puede cambiar su horario."));
            }

            var windowError = MarketplaceWindowRules.ValidateListingWindow(start, end, DateTime.UtcNow);
            if (windowError is not null)
            {
                return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.BadRequest(windowError));
            }
        }

        MarketplaceResult<MarketplaceListingDto>? result = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var listing = await db.MarketplaceListings.FirstAsync(x => x.Id == id, ct);
            if (listing.Status == MarketplaceListingStatus.Closed)
            {
                await transaction.RollbackAsync(ct);
                result = MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict("La publicación está cerrada."));
                return;
            }

            if (windowChanged)
            {
                if (await OverlapsAnotherAsync(listing.UnitId, listing.Id, start, end, ct))
                {
                    await transaction.RollbackAsync(ct);
                    result = MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict(
                        "Esa unidad ya tiene una publicación en ese horario."));
                    return;
                }

                // Las reservas ya hechas deben seguir cabiendo en la ventana.
                var outside = await db.MarketplaceReservations.AnyAsync(x => !x.IsDeleted && x.ListingId == id
                    && LiveReservationStatuses.Contains(x.Status)
                    && (x.StartsAtUtc < start || x.EndsAtUtc > end), ct);
                if (outside)
                {
                    await transaction.RollbackAsync(ct);
                    result = MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict(
                        "Hay reservas que quedarían fuera del nuevo horario."));
                    return;
                }
            }

            var before = new { listing.Title, listing.WindowStartUtc, listing.WindowEndUtc, listing.HourlyPrice };
            listing.Title = title;
            listing.WindowStartUtc = start;
            listing.WindowEndUtc = end;
            // Cambiar el precio solo rige para reservas nuevas: las ya hechas tienen sus importes congelados.
            listing.HourlyPrice = request.HourlyPrice;

            audit.Record(listing.CompanyId, listing.BuildingId, nameof(MarketplaceListing), listing.Id,
                MarketplaceEventActions.ListingUpdated, listing.Status.ToString(), listing.Status.ToString(),
                new { Before = before, After = new { listing.Title, listing.WindowStartUtc, listing.WindowEndUtc, listing.HourlyPrice } });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            result = MarketplaceResult<MarketplaceListingDto>.Success(new MarketplaceListingDto { Id = listing.Id });
        });

        return result!.Ok
            ? MarketplaceResult<MarketplaceListingDto>.Success(await LoadDtoAsync(id, ct))
            : result;
    }

    // ── Suspender, reanudar, cerrar ──────────────────────────────────────────

    public Task<MarketplaceResult<MarketplaceListingDto>> SuspendAsync(Guid id, string? reason, CancellationToken ct) =>
        ChangeStatusAsync(id, MarketplaceListingStatus.Suspended, reason, MarketplaceEventActions.ListingSuspended, ct);

    public Task<MarketplaceResult<MarketplaceListingDto>> ResumeAsync(Guid id, CancellationToken ct) =>
        ChangeStatusAsync(id, MarketplaceListingStatus.Active, null, MarketplaceEventActions.ListingResumed, ct);

    public Task<MarketplaceResult<MarketplaceListingDto>> CloseAsync(Guid id, string? reason, CancellationToken ct) =>
        ChangeStatusAsync(id, MarketplaceListingStatus.Closed, reason, MarketplaceEventActions.ListingClosed, ct);

    private async Task<MarketplaceResult<MarketplaceListingDto>> ChangeStatusAsync(
        Guid id, MarketplaceListingStatus target, string? reason, string action, CancellationToken ct)
    {
        var current = await db.MarketplaceListings.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        if (current is null)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(current.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.FromAccess(access));
        }

        // Pueden cambiarla su dueño y el personal del edificio; para cualquier otro "no existe".
        var isOwner = current.OwnerId == tenant.UserId;
        var isStaffAction = !isOwner && access.Context!.IsStaff;
        if (!isOwner && !isStaffAction)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.NotFound());
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is not null && cleanReason.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(
                MarketplaceError.BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres."));
        }

        // El personal que suspende o cierra la publicacion de otro tiene que dejar el motivo.
        if (isStaffAction && target != MarketplaceListingStatus.Active && cleanReason is null)
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.BadRequest("Indicá el motivo."));
        }

        if (!MarketplaceStateMachine.CanTransition(current.Status, target))
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict(StatusMessage(current.Status, target)));
        }

        if (target == MarketplaceListingStatus.Active)
        {
            if (!await scope.IsPrimaryOwnerOfUnitAsync(current.OwnerId, current.UnitId, ct))
            {
                return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict(
                    "El propietario ya no es el principal de la unidad: la publicación no se puede reanudar."));
            }

            if (AsUtc(current.WindowEndUtc) <= DateTime.UtcNow)
            {
                return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict("El horario de la publicación ya terminó."));
            }
        }

        if (target == MarketplaceListingStatus.Closed
            && await db.MarketplaceReservations.AnyAsync(x => !x.IsDeleted && x.ListingId == id && LiveReservationStatuses.Contains(x.Status), ct))
        {
            return MarketplaceResult<MarketplaceListingDto>.Fail(MarketplaceError.Conflict(
                "La publicación tiene reservas vigentes: no se puede cerrar hasta que terminen o se cancelen."));
        }

        db.ChangeTracker.Clear();
        var listing = await db.MarketplaceListings.FirstAsync(x => x.Id == id, ct);
        var from = listing.Status;
        MarketplaceStateMachine.EnsureTransition(from, target);
        listing.Status = target;
        listing.StatusReason = target == MarketplaceListingStatus.Active ? null : cleanReason;

        audit.Record(listing.CompanyId, listing.BuildingId, nameof(MarketplaceListing), listing.Id,
            action, from.ToString(), target.ToString(),
            new { Reason = cleanReason, ByStaff = isStaffAction });

        await db.SaveChangesAsync(ct);
        return MarketplaceResult<MarketplaceListingDto>.Success(await LoadDtoAsync(id, ct));
    }

    // ── Titularidad: si deja de ser el principal, la publicacion se suspende sola ──

    /// <summary>
    /// Suspende las publicaciones activas del edificio cuyo dueño ya no es el propietario principal de la unidad. Se corre
    /// al consultar publicaciones; el proceso en segundo plano de la fase 4 hace lo mismo periodicamente.
    /// </summary>
    public async Task<int> SyncOwnershipAsync(Guid buildingId, CancellationToken ct)
    {
        var active = await db.MarketplaceListings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == MarketplaceListingStatus.Active)
            .Select(x => new { x.Id, x.OwnerId, x.UnitId })
            .ToListAsync(ct);
        if (active.Count == 0)
        {
            return 0;
        }

        var unitIds = active.Select(x => x.UnitId).Distinct().ToList();
        var primaries = (await db.UnitOwners.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsPrimary && unitIds.Contains(x.UnitId))
                .Select(x => new { x.UnitId, x.OwnerId })
                .ToListAsync(ct))
            .Select(x => (x.UnitId, x.OwnerId))
            .ToHashSet();

        var staleIds = active.Where(x => !primaries.Contains((x.UnitId, x.OwnerId))).Select(x => x.Id).ToList();
        if (staleIds.Count == 0)
        {
            return 0;
        }

        db.ChangeTracker.Clear();
        var stale = await db.MarketplaceListings.Where(x => staleIds.Contains(x.Id) && x.Status == MarketplaceListingStatus.Active).ToListAsync(ct);
        foreach (var listing in stale)
        {
            listing.Status = MarketplaceListingStatus.Suspended;
            listing.StatusReason = "El propietario dejó de ser el principal de la unidad.";
            audit.Record(listing.CompanyId, listing.BuildingId, nameof(MarketplaceListing), listing.Id,
                MarketplaceEventActions.ListingSuspended, MarketplaceListingStatus.Active.ToString(), listing.Status.ToString(),
                new { Reason = listing.StatusReason, Automatic = true }, automatic: true);
        }

        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string? ValidateContent(string? rawTitle, decimal hourlyPrice, out string title)
    {
        title = (rawTitle ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return "El título es obligatorio.";
        }

        if (title.Length > TitleMaxLength)
        {
            return $"El título no puede superar los {TitleMaxLength} caracteres.";
        }

        return MarketplacePricing.ValidateHourlyPrice(hourlyPrice);
    }

    // Publicaciones no cerradas de la misma unidad que se pisan con la ventana (intervalos semiabiertos).
    private Task<bool> OverlapsAnotherAsync(Guid unitId, Guid? exceptListingId, DateTime start, DateTime end, CancellationToken ct) =>
        db.MarketplaceListings.AnyAsync(x => !x.IsDeleted && x.UnitId == unitId
            && (exceptListingId == null || x.Id != exceptListingId)
            && x.Status != MarketplaceListingStatus.Closed
            && x.WindowStartUtc < end && start < x.WindowEndUtc, ct);

    private static string StatusMessage(MarketplaceListingStatus from, MarketplaceListingStatus to) => (from, to) switch
    {
        (MarketplaceListingStatus.Closed, _) => "La publicación está cerrada.",
        (MarketplaceListingStatus.Suspended, MarketplaceListingStatus.Suspended) => "La publicación ya está suspendida.",
        (MarketplaceListingStatus.Active, MarketplaceListingStatus.Active) => "La publicación ya está activa.",
        _ => $"No se puede pasar la publicación de {from} a {to}."
    };

    private async Task<MarketplaceListingDto> LoadDtoAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync(db.MarketplaceListings.AsNoTracking().Where(x => x.Id == id), ct)).Single();

    private async Task<List<MarketplaceListingDto>> ToDtosAsync(IQueryable<MarketplaceListing> query, CancellationToken ct)
    {
        var rows = await query
            .OrderByDescending(x => x.WindowStartUtc)
            .Select(x => new
            {
                x.Id,
                x.BuildingId,
                x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                x.OwnerId,
                OwnerName = x.Owner != null ? x.Owner.FullName : string.Empty,
                x.Title,
                x.WindowStartUtc,
                x.WindowEndUtc,
                x.HourlyPrice,
                x.Status,
                x.StatusReason,
                x.CreatedAtUtc,
                ActiveReservations = x.Reservations.Count(r => !r.IsDeleted && LiveReservationStatuses.Contains(r.Status))
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        return rows.Select(x => new MarketplaceListingDto
        {
            Id = x.Id,
            BuildingId = x.BuildingId,
            UnitId = x.UnitId,
            UnitCode = x.UnitCode,
            OwnerId = x.OwnerId,
            OwnerName = x.OwnerName,
            Title = x.Title,
            WindowStartUtc = AsUtc(x.WindowStartUtc),
            WindowEndUtc = AsUtc(x.WindowEndUtc),
            WindowHours = MarketplaceWindowRules.WholeHours(x.WindowStartUtc, x.WindowEndUtc),
            HourlyPrice = x.HourlyPrice,
            Status = x.Status.ToString(),
            StatusReason = x.StatusReason,
            WindowEnded = AsUtc(x.WindowEndUtc) <= now,
            ActiveReservations = x.ActiveReservations,
            CreatedAtUtc = x.CreatedAtUtc
        }).ToList();
    }
}
