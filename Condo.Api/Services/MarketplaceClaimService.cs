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
/// "Reportar un problema": el comprador o el propietario lo abren desde que empieza la reserva hasta 24 horas despues de su fin.
/// Mientras esta abierto retiene la acreditacion del saldo y avisa a quienes resuelven en el edificio. El Encargado lo resuelve a
/// favor del propietario (se libera la acreditacion) o a favor del comprador (se le devuelve todo y el propietario asume la
/// comision, igual que si hubiera cancelado).
/// </summary>
public class MarketplaceClaimService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    PushDispatcher push,
    MarketplaceRefundService refunds)
{
    private const int ReasonMaxLength = 500;
    private const int ResolvedHistorySize = 30;

    // ── Abrir (comprador o propietario) ──────────────────────────────────────

    public async Task<MarketplaceResult<MarketplaceClaimDto>> OpenAsync(Guid reservationId, string? reason, CancellationToken ct)
    {
        var cleanReason = (reason ?? string.Empty).Trim();
        if (cleanReason.Length == 0)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(MarketplaceError.BadRequest("Contanos cuál fue el problema."));
        }

        if (cleanReason.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(
                MarketplaceError.BadRequest($"El texto no puede superar los {ReasonMaxLength} caracteres."));
        }

        var current = await db.MarketplaceReservations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == reservationId
                                      && (x.BuyerUserId == tenant.UserId || x.OwnerId == tenant.UserId), ct);
        if (current is null)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(current.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(MarketplaceError.FromAccess(access));
        }

        var party = current.BuyerUserId == tenant.UserId ? MarketplaceClaimParty.Buyer : MarketplaceClaimParty.Owner;
        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<Guid>? failure = null;
        var claimId = Guid.Empty;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                pushes.Clear();
                failure = null;
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var now = DateTime.UtcNow;

                var reservation = await db.MarketplaceReservations.Include(x => x.Listing).FirstAsync(x => x.Id == reservationId, ct);
                var hasOpen = await db.MarketplaceClaims.AnyAsync(x => !x.IsDeleted && x.ReservationId == reservationId
                                                                        && x.Status == MarketplaceClaimStatus.Open, ct);

                if (!MarketplaceCancellationRules.CanOpenClaim(reservation.Status, reservation.CreditStatus,
                        reservation.StartsAtUtc, reservation.EndsAtUtc, hasOpen, now))
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<Guid>.Fail(MarketplaceError.Conflict(CannotOpenMessage(reservation, hasOpen, now)));
                    return;
                }

                var claim = new MarketplaceClaim
                {
                    CompanyId = reservation.CompanyId,
                    ReservationId = reservation.Id,
                    BuildingId = reservation.BuildingId,
                    OpenedByUserId = tenant.UserId,
                    OpenedBy = party,
                    Reason = cleanReason,
                    Status = MarketplaceClaimStatus.Open
                };
                db.MarketplaceClaims.Add(claim);
                claimId = claim.Id;

                // Retiene la acreditacion mientras el reclamo siga abierto.
                if (reservation.CreditStatus == MarketplaceCreditStatus.Pending)
                {
                    MarketplaceStateMachine.EnsureTransition(reservation.CreditStatus, MarketplaceCreditStatus.Held);
                    reservation.CreditStatus = MarketplaceCreditStatus.Held;
                    audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                        MarketplaceEventActions.CreditHeld, MarketplaceCreditStatus.Pending.ToString(),
                        MarketplaceCreditStatus.Held.ToString(), new { reservation.Reference, ClaimId = claim.Id });
                }

                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceClaim), claim.Id,
                    MarketplaceEventActions.ClaimOpened, null, claim.Status.ToString(),
                    new { reservation.Reference, OpenedBy = party.ToString(), Reason = cleanReason });

                var title = reservation.Listing?.Title ?? "una reserva";
                var heading = "Reclamo en una reserva";
                var body = $"{title} · {reservation.Reference}. {(party == MarketplaceClaimParty.Buyer ? "El comprador" : "El propietario")} " +
                           $"reportó un problema: {Truncate(cleanReason, 200)}";
                foreach (var reviewerId in await MarketplaceNotices.ReviewerIdsAsync(db, reservation.CompanyId, reservation.BuildingId, ct))
                {
                    MarketplaceNotices.Add(db, reservation.CompanyId, reviewerId, NotificationType.MarketplaceClaimOpened,
                        heading, body, nameof(MarketplaceClaim), claim.Id, pushes);
                }

                // La otra parte se entera de que hay un reclamo (sin el detalle) y de que la acreditacion queda retenida.
                var counterpart = party == MarketplaceClaimParty.Buyer ? reservation.OwnerId : reservation.BuyerUserId;
                MarketplaceNotices.Add(db, reservation.CompanyId, counterpart, NotificationType.MarketplaceClaimOpened,
                    "Reportaron un problema en una reserva",
                    $"Se reportó un problema en la reserva {reservation.Reference} de {title}. La administración lo va a revisar.",
                    nameof(MarketplaceReservation), reservation.Id, pushes);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException)
        {
            // Dos reclamos a la vez: la base dejo pasar solo uno.
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceClaimDto>.Fail(MarketplaceError.Conflict("Ya hay un reclamo abierto para esta reserva."));
        }

        if (failure is not null)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(failure.Error!);
        }

        await MarketplaceNotices.DispatchAsync(push, pushes);
        return MarketplaceResult<MarketplaceClaimDto>.Success(await LoadDtoAsync(claimId, ct));
    }

    // ── Lista del Encargado ──────────────────────────────────────────────────

    /// <summary>Reclamos del edificio: los abiertos primero (el mas antiguo arriba) y, si se pide, los ultimos resueltos.</summary>
    public async Task<MarketplaceResult<List<MarketplaceClaimDto>>> GetClaimsAsync(Guid buildingId, bool includeResolved, CancellationToken ct)
    {
        var staff = await RequireStaffAsync(buildingId, ct);
        if (!staff.Ok)
        {
            return MarketplaceResult<List<MarketplaceClaimDto>>.Fail(staff.Error!);
        }

        var list = await ToDtosAsync(db.MarketplaceClaims.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == MarketplaceClaimStatus.Open)
            .OrderBy(x => x.CreatedAtUtc), ct);

        if (includeResolved)
        {
            list.AddRange(await ToDtosAsync(db.MarketplaceClaims.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == MarketplaceClaimStatus.Resolved)
                .OrderByDescending(x => x.ResolvedAtUtc).Take(ResolvedHistorySize), ct));
        }

        return MarketplaceResult<List<MarketplaceClaimDto>>.Success(list);
    }

    // ── Resolver (Encargado) ─────────────────────────────────────────────────

    public async Task<MarketplaceResult<MarketplaceClaimDto>> ResolveAsync(
        Guid claimId, MarketplaceClaimResolveRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<MarketplaceClaimResolution>(request.Outcome, ignoreCase: true, out var outcome)
            || !Enum.IsDefined(outcome))
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(MarketplaceError.BadRequest("Elegí cómo se resuelve el reclamo."));
        }

        var note = (request.Note ?? string.Empty).Trim();
        if (note.Length == 0)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(
                MarketplaceError.BadRequest("Escribí una explicación: la ven las dos partes."));
        }

        if (note.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(
                MarketplaceError.BadRequest($"La explicación no puede superar los {ReasonMaxLength} caracteres."));
        }

        var buildingId = await db.MarketplaceClaims.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == claimId).Select(x => (Guid?)x.BuildingId).FirstOrDefaultAsync(ct);
        if (buildingId is null)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(MarketplaceError.NotFound());
        }

        var staff = await RequireStaffAsync(buildingId.Value, ct);
        if (!staff.Ok)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(staff.Error!);
        }

        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<Guid>? failure = null;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                pushes.Clear();
                failure = null;
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var now = DateTime.UtcNow;

                var claim = await db.MarketplaceClaims.FirstAsync(x => x.Id == claimId, ct);
                if (claim.Status != MarketplaceClaimStatus.Open)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<Guid>.Fail(MarketplaceError.Conflict("Este reclamo ya fue resuelto."));
                    return;
                }

                var reservation = await db.MarketplaceReservations
                    .Include(x => x.Listing).Include(x => x.Slots)
                    .FirstAsync(x => x.Id == claim.ReservationId, ct);

                claim.Status = MarketplaceClaimStatus.Resolved;
                claim.Resolution = outcome;
                claim.ResolutionNote = note;
                claim.ResolvedByUserId = tenant.UserId;
                claim.ResolvedAtUtc = now;

                var title = reservation.Listing?.Title ?? "el espacio";

                if (outcome == MarketplaceClaimResolution.InFavorOfOwner)
                {
                    // Se libera la acreditacion: la toma el proceso de fondo cuando termine la ventana de 24 horas.
                    ReleaseCredit(reservation, claim, MarketplaceCreditStatus.Pending);

                    MarketplaceNotices.Add(db, reservation.CompanyId, reservation.OwnerId, NotificationType.MarketplaceClaimResolved,
                        "Reclamo resuelto a tu favor",
                        $"La administración resolvió el reclamo de la reserva {reservation.Reference} a tu favor: se te acredita normalmente. {note}",
                        nameof(MarketplaceReservation), reservation.Id, pushes);
                    MarketplaceNotices.Add(db, reservation.CompanyId, reservation.BuyerUserId, NotificationType.MarketplaceClaimResolved,
                        "Reclamo resuelto",
                        $"La administración resolvió el reclamo de la reserva {reservation.Reference} de {title} sin devolución. {note}",
                        nameof(MarketplaceReservation), reservation.Id, pushes);
                }
                else
                {
                    // A favor del comprador: se le devuelve todo y el propietario asume la comision, como si hubiera cancelado.
                    ReleaseCredit(reservation, claim, MarketplaceCreditStatus.None);
                    if (reservation.Status == MarketplaceReservationStatus.Confirmed)
                    {
                        MarketplaceStateMachine.EnsureTransition(reservation.Status, MarketplaceReservationStatus.Cancelled);
                        reservation.Status = MarketplaceReservationStatus.Cancelled;
                        reservation.CancelledAtUtc = now;
                        reservation.CancelledByUserId = tenant.UserId;
                        reservation.CancelledBy = MarketplaceCancellationActor.Staff;
                        reservation.CancelReason = note;
                        foreach (var slot in reservation.Slots.Where(x => !x.IsDeleted))
                        {
                            slot.IsDeleted = true;
                        }
                    }

                    var refund = await refunds.RegisterRefundAsync(reservation, MarketplaceRefundOrigin.ClaimResolution, note, pushes, ct);
                    var fee = await refunds.ChargeOwnerCommissionAsync(reservation, note, now, ct);

                    MarketplaceNotices.Add(db, reservation.CompanyId, reservation.BuyerUserId, NotificationType.MarketplaceClaimResolved,
                        "Reclamo resuelto a tu favor",
                        $"La administración resolvió el reclamo de la reserva {reservation.Reference} a tu favor. Se te devuelve todo " +
                        $"({MarketplaceNotices.Gs(refund.Amount)}) hasta en {MarketplaceCancellationRules.RefundMaxHours} horas. {note}",
                        nameof(MarketplaceReservation), reservation.Id, pushes);
                    MarketplaceNotices.Add(db, reservation.CompanyId, reservation.OwnerId, NotificationType.MarketplaceClaimResolved,
                        "Reclamo resuelto",
                        $"La administración resolvió el reclamo de la reserva {reservation.Reference} a favor del comprador. {note} " +
                        MarketplaceCancellationService.FeeSentence(reservation, fee),
                        nameof(MarketplaceReservation), reservation.Id, pushes);
                }

                audit.Record(claim.CompanyId, claim.BuildingId, nameof(MarketplaceClaim), claim.Id,
                    MarketplaceEventActions.ClaimResolved, MarketplaceClaimStatus.Open.ToString(), claim.Status.ToString(),
                    new { reservation.Reference, Outcome = outcome.ToString(), Note = note });

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceClaimDto>.Fail(MarketplaceError.Conflict("Este reclamo ya fue resuelto."));
        }

        if (failure is not null)
        {
            return MarketplaceResult<MarketplaceClaimDto>.Fail(failure.Error!);
        }

        await MarketplaceNotices.DispatchAsync(push, pushes);
        return MarketplaceResult<MarketplaceClaimDto>.Success(await LoadDtoAsync(claimId, ct));
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    // La acreditacion retenida vuelve a Pending (se acredita) o a None (no se acredita): nunca queda "colgada" en Held.
    private void ReleaseCredit(MarketplaceReservation reservation, MarketplaceClaim claim, MarketplaceCreditStatus target)
    {
        if (reservation.CreditStatus != MarketplaceCreditStatus.Held)
        {
            return;
        }

        MarketplaceStateMachine.EnsureTransition(reservation.CreditStatus, target);
        reservation.CreditStatus = target;
        audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
            MarketplaceEventActions.CreditReleased, MarketplaceCreditStatus.Held.ToString(), target.ToString(),
            new { reservation.Reference, ClaimId = claim.Id, Outcome = claim.Resolution?.ToString() });
    }

    private static string CannotOpenMessage(MarketplaceReservation reservation, bool hasOpen, DateTime now)
    {
        if (hasOpen)
        {
            return "Ya hay un reclamo abierto para esta reserva.";
        }

        if (reservation.CreditStatus == MarketplaceCreditStatus.Credited || reservation.CreditStatus == MarketplaceCreditStatus.Reversed)
        {
            return "La ganancia de esta reserva ya se acreditó: el plazo para reportar un problema terminó.";
        }

        if (reservation.Status == MarketplaceReservationStatus.Confirmed && now < MarketplaceReservationViews.AsUtc(reservation.StartsAtUtc))
        {
            return "La reserva todavía no empezó. Si no vas a usarla, cancelala.";
        }

        if (reservation.Status is MarketplaceReservationStatus.Confirmed or MarketplaceReservationStatus.Completed)
        {
            return $"Pasaron más de {MarketplaceCancellationRules.ClaimWindow.TotalHours:0} horas desde el fin de la reserva.";
        }

        return "No se puede reportar un problema en esta reserva.";
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private async Task<MarketplaceResult<Guid>> RequireStaffAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<Guid>.Fail(MarketplaceError.FromAccess(access));
        }

        return access.Context!.IsStaff
            ? MarketplaceResult<Guid>.Success(buildingId)
            : MarketplaceResult<Guid>.Fail(MarketplaceError.NotFound());
    }

    private async Task<MarketplaceClaimDto> LoadDtoAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync(db.MarketplaceClaims.AsNoTracking().Where(x => x.Id == id), ct)).Single();

    private async Task<List<MarketplaceClaimDto>> ToDtosAsync(IQueryable<MarketplaceClaim> query, CancellationToken ct)
    {
        var rows = await query
            .Select(x => new
            {
                x.Id, x.ReservationId, x.BuildingId, x.OpenedBy, x.Reason, x.Status, x.Resolution, x.ResolutionNote,
                x.CreatedAtUtc, x.ResolvedAtUtc,
                Reference = x.Reservation!.Reference,
                Title = x.Reservation.Listing != null ? x.Reservation.Listing.Title : string.Empty,
                UnitCode = x.Reservation.Unit != null ? x.Reservation.Unit.Code : string.Empty,
                OwnerName = x.Reservation.Owner != null ? x.Reservation.Owner.FullName : string.Empty,
                BuyerName = x.Reservation.Buyer != null ? x.Reservation.Buyer.FullName : string.Empty,
                x.Reservation.BuyerUserId,
                x.Reservation.StartsAtUtc, x.Reservation.EndsAtUtc, x.Reservation.BaseAmount, x.Reservation.CommissionAmount,
                x.Reservation.TotalAmount, x.Reservation.StartResponse, x.Reservation.StartResponseReason,
                OpenedByName = x.OpenedByUser != null ? x.OpenedByUser.FullName : string.Empty
            })
            .ToListAsync(ct);

        var unitsByBuyer = await MarketplaceReservationViews.BuyerUnitsAsync(db,
            rows.Select(x => x.BuyerUserId).Distinct().ToList(), rows.Select(x => x.BuildingId).Distinct().ToList(), ct);

        return rows.Select(x =>
        {
            unitsByBuyer.TryGetValue((x.BuyerUserId, x.BuildingId), out var units);
            return new MarketplaceClaimDto
            {
                Id = x.Id,
                ReservationId = x.ReservationId,
                BuildingId = x.BuildingId,
                Reference = x.Reference,
                Title = x.Title,
                UnitCode = x.UnitCode,
                OwnerName = x.OwnerName,
                BuyerName = x.BuyerName,
                BuyerUnits = units is null ? string.Empty : string.Join(", ", units.Distinct().OrderBy(c => c)),
                StartsAtUtc = MarketplaceReservationViews.AsUtc(x.StartsAtUtc),
                EndsAtUtc = MarketplaceReservationViews.AsUtc(x.EndsAtUtc),
                BaseAmount = x.BaseAmount,
                CommissionAmount = x.CommissionAmount,
                TotalAmount = x.TotalAmount,
                OpenedBy = x.OpenedBy.ToString(),
                OpenedByName = x.OpenedByName,
                Reason = x.Reason,
                Status = x.Status.ToString(),
                Resolution = x.Resolution?.ToString(),
                ResolutionNote = x.ResolutionNote,
                CreatedAtUtc = MarketplaceReservationViews.AsUtc(x.CreatedAtUtc),
                ResolvedAtUtc = x.ResolvedAtUtc.HasValue ? MarketplaceReservationViews.AsUtc(x.ResolvedAtUtc.Value) : null,
                BuyerStartResponse = x.StartResponse?.ToString(),
                BuyerStartResponseReason = x.StartResponseReason
            };
        }).ToList();
    }
}
