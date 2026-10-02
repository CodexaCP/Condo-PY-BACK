using System.Data;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Aviso de inicio: cuando empieza el horario de una reserva confirmada, el comprador recibe "¿La vas a usar?" con dos respuestas,
/// "Sí, voy" y "No la voy a usar" (con motivo). Responder NO devuelve dinero ni cambia nada para el propietario: el motivo solo
/// queda registrado. Si no responde, no pasa nada (se asume que la uso). Quien quiera su dinero usa "Reportar un problema".
/// </summary>
public class MarketplaceStartNoticeService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    PushDispatcher push)
{
    private const int BatchSize = 200;
    private const int ReasonMaxLength = 500;

    // ── Proceso de fondo ─────────────────────────────────────────────────────

    /// <summary>Manda el aviso a los compradores de las reservas confirmadas que ya empezaron y todavia no terminaron. Una sola vez por reserva.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;

        var due = await db.MarketplaceReservations
            .Include(x => x.Listing)
            .Where(x => !x.IsDeleted && x.Status == MarketplaceReservationStatus.Confirmed
                        && x.StartNoticeSentAtUtc == null && x.StartsAtUtc <= now && x.EndsAtUtc > now)
            .OrderBy(x => x.StartsAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var pushes = new List<MarketplacePushItem>();
        foreach (var reservation in due)
        {
            reservation.StartNoticeSentAtUtc = now;

            MarketplaceNotices.Add(db, reservation.CompanyId, reservation.BuyerUserId, NotificationType.MarketplaceStartNotice,
                "Tu reserva empezó",
                $"Tu reserva de {reservation.Listing?.Title ?? "el espacio"} ya empezó. ¿La vas a usar? Contanos desde Marketplace → Mis reservas.",
                nameof(MarketplaceReservation), reservation.Id, pushes);

            audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                MarketplaceEventActions.StartNoticeSent, reservation.Status.ToString(), reservation.Status.ToString(),
                new { reservation.Reference, reservation.StartsAtUtc }, automatic: true);
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await MarketplaceNotices.DispatchAsync(push, pushes);
        }

        return due.Count;
    }

    // ── Respuesta del comprador ──────────────────────────────────────────────

    public async Task<MarketplaceResult<MarketplaceReservationDto>> RespondAsync(
        Guid reservationId, MarketplaceStartResponseRequest request, CancellationToken ct)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (!request.Attending && reason.Length == 0)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.BadRequest("Contanos por qué no la vas a usar."));
        }

        if (reason.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(
                MarketplaceError.BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres."));
        }

        var current = await db.MarketplaceReservations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == reservationId && x.BuyerUserId == tenant.UserId, ct);
        if (current is null)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(current.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.FromAccess(access));
        }

        MarketplaceResult<MarketplaceReservationDto>? failure = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            failure = null;
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var now = DateTime.UtcNow;

            var reservation = await db.MarketplaceReservations.FirstAsync(x => x.Id == reservationId, ct);

            // Solo mientras la reserva esta en curso: confirmada, ya empezada y sin terminar.
            if (reservation.Status != MarketplaceReservationStatus.Confirmed
                || now < MarketplaceReservationViews.AsUtc(reservation.StartsAtUtc)
                || now >= MarketplaceReservationViews.AsUtc(reservation.EndsAtUtc))
            {
                await transaction.RollbackAsync(ct);
                failure = MarketplaceResult<MarketplaceReservationDto>.Fail(
                    MarketplaceError.Conflict("Solo se responde mientras la reserva está en curso."));
                return;
            }

            if (reservation.StartResponse is not null)
            {
                await transaction.RollbackAsync(ct);
                failure = MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.Conflict("Ya respondiste el aviso de esta reserva."));
                return;
            }

            reservation.StartResponse = request.Attending ? MarketplaceStartResponse.Attending : MarketplaceStartResponse.NotUsing;
            reservation.StartResponseReason = request.Attending || reason.Length == 0 ? null : reason;
            reservation.StartResponseAtUtc = now;

            // Solo queda registrado: no hay devolucion automatica y el propietario cobra normal.
            audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                MarketplaceEventActions.StartResponded, reservation.Status.ToString(), reservation.Status.ToString(),
                new { reservation.Reference, Response = reservation.StartResponse.ToString(), Reason = reservation.StartResponseReason });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        if (failure is not null)
        {
            return failure;
        }

        var dto = (await MarketplaceReservationViews.LoadBuyerViewAsync(
            db, db.MarketplaceReservations.AsNoTracking().Where(x => x.Id == reservationId), ct)).Single();
        return MarketplaceResult<MarketplaceReservationDto>.Success(dto);
    }
}
