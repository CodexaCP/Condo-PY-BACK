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
/// Cancelar una reserva ya pagada. El comprador puede hacerlo solo antes de que empiece el horario: se le devuelve la base y la
/// comision de gestion NO se devuelve. Si cancela el propietario (con motivo), se le devuelve todo al comprador y el propietario
/// asume la comision (de su saldo a favor o como deuda por gestion). En los dos casos se libera el horario, no se acredita nada
/// al propietario y queda un reembolso pendiente para el Encargado. Cancelar una reserva todavia sin pagar sigue en
/// <see cref="MarketplaceReservationService.CancelPendingAsync"/>.
/// </summary>
public class MarketplaceCancellationService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    PushDispatcher push,
    MarketplaceReservationService reservations,
    MarketplaceRefundService refunds)
{
    private const int ReasonMaxLength = 500;

    // ── Vista previa: lo que pasa si cancela (para avisarlo antes de confirmar) ──

    public async Task<MarketplaceResult<MarketplaceCancelPreviewDto>> PreviewAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await db.MarketplaceReservations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == reservationId
                                      && (x.BuyerUserId == tenant.UserId || x.OwnerId == tenant.UserId), ct);
        if (reservation is null)
        {
            return MarketplaceResult<MarketplaceCancelPreviewDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(reservation.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceCancelPreviewDto>.Fail(MarketplaceError.FromAccess(access));
        }

        var asBuyer = reservation.BuyerUserId == tenant.UserId;
        var now = DateTime.UtcNow;
        var preview = new MarketplaceCancelPreviewDto
        {
            ReservationId = reservation.Id,
            Role = asBuyer ? "Buyer" : "Owner",
            CommissionAmount = reservation.CommissionAmount,
            RequiresReason = !asBuyer
        };

        if (asBuyer && reservation.Status == MarketplaceReservationStatus.PendingPayment)
        {
            preview.CanCancel = true;
            preview.BeforePayment = true;
            return MarketplaceResult<MarketplaceCancelPreviewDto>.Success(preview);
        }

        if (MarketplaceCancellationRules.CanCancelPaid(reservation.Status, reservation.StartsAtUtc, now))
        {
            preview.CanCancel = true;
            preview.RefundAmount = MarketplaceCancellationRules.RefundAmount(
                asBuyer ? MarketplaceRefundOrigin.BuyerCancellation : MarketplaceRefundOrigin.OwnerCancellation,
                reservation.BaseAmount, reservation.TotalAmount);
        }
        else
        {
            preview.BlockedReason = BlockedMessage(reservation.Status, asBuyer);
        }

        return MarketplaceResult<MarketplaceCancelPreviewDto>.Success(preview);
    }

    // ── Comprador ────────────────────────────────────────────────────────────

    /// <summary>Cancela la reserva del comprador: sin pagar, la libera; ya pagada y antes del inicio, genera el reembolso de la base.</summary>
    public async Task<MarketplaceResult<MarketplaceReservationDto>> CancelByBuyerAsync(Guid id, string? reason, CancellationToken ct)
    {
        var current = await db.MarketplaceReservations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.BuyerUserId == tenant.UserId, ct);
        if (current is null)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.NotFound());
        }

        if (current.Status == MarketplaceReservationStatus.PendingPayment)
        {
            return await reservations.CancelPendingAsync(id, ct);
        }

        var cleanReason = (reason ?? string.Empty).Trim();
        if (cleanReason.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(
                MarketplaceError.BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres."));
        }

        var access = await scope.ResolveBuildingAsync(current.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.FromAccess(access));
        }

        var result = await CancelPaidAsync(id, MarketplaceCancellationActor.Buyer, cleanReason.Length == 0 ? null : cleanReason, ct);
        return await ToBuyerDtoAsync(result, id, ct);
    }

    // ── Propietario ──────────────────────────────────────────────────────────

    /// <summary>El propietario cancela una reserva ya pagada, con motivo: devolucion total al comprador y comision a su cargo.</summary>
    public async Task<MarketplaceResult<MarketplaceOwnerReservationDto>> CancelByOwnerAsync(Guid id, string? reason, CancellationToken ct)
    {
        var cleanReason = (reason ?? string.Empty).Trim();
        if (cleanReason.Length == 0)
        {
            return MarketplaceResult<MarketplaceOwnerReservationDto>.Fail(MarketplaceError.BadRequest("Indicá el motivo de la cancelación."));
        }

        if (cleanReason.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceOwnerReservationDto>.Fail(
                MarketplaceError.BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres."));
        }

        var current = await db.MarketplaceReservations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.OwnerId == tenant.UserId, ct);
        if (current is null)
        {
            return MarketplaceResult<MarketplaceOwnerReservationDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(current.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceOwnerReservationDto>.Fail(MarketplaceError.FromAccess(access));
        }

        var result = await CancelPaidAsync(id, MarketplaceCancellationActor.Owner, cleanReason, ct);
        if (!result.Ok)
        {
            return MarketplaceResult<MarketplaceOwnerReservationDto>.Fail(result.Error!);
        }

        var dto = (await MarketplaceReservationViews.LoadOwnerViewAsync(db, db.MarketplaceReservations.AsNoTracking().Where(x => x.Id == id), ct)).Single();
        return MarketplaceResult<MarketplaceOwnerReservationDto>.Success(dto);
    }

    // ── Nucleo compartido ────────────────────────────────────────────────────

    private async Task<MarketplaceResult<Guid>> CancelPaidAsync(
        Guid id, MarketplaceCancellationActor actor, string? reason, CancellationToken ct)
    {
        var asBuyer = actor == MarketplaceCancellationActor.Buyer;
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

                var reservation = await db.MarketplaceReservations
                    .Include(x => x.Listing).Include(x => x.Slots)
                    .FirstAsync(x => x.Id == id, ct);

                // La identidad se vuelve a validar dentro de la transaccion: solo la parte que corresponde cancela.
                if ((asBuyer ? reservation.BuyerUserId : reservation.OwnerId) != tenant.UserId)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<Guid>.Fail(MarketplaceError.NotFound());
                    return;
                }

                if (!MarketplaceCancellationRules.CanCancelPaid(reservation.Status, reservation.StartsAtUtc, now))
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<Guid>.Fail(MarketplaceError.Conflict(BlockedMessage(reservation.Status, asBuyer)));
                    return;
                }

                var from = reservation.Status;
                MarketplaceStateMachine.EnsureTransition(reservation.Status, MarketplaceReservationStatus.Cancelled);
                MarketplaceStateMachine.EnsureTransition(reservation.CreditStatus, MarketplaceCreditStatus.None);

                reservation.Status = MarketplaceReservationStatus.Cancelled;
                reservation.ExpiresAtUtc = null;
                reservation.CancelledAtUtc = now;
                reservation.CancelledByUserId = tenant.UserId;
                reservation.CancelledBy = actor;
                reservation.CancelReason = reason;
                // No se acredita nada al propietario por una reserva cancelada antes del uso.
                reservation.CreditStatus = MarketplaceCreditStatus.None;
                foreach (var slot in reservation.Slots.Where(x => !x.IsDeleted))
                {
                    slot.IsDeleted = true;
                }

                var origin = asBuyer ? MarketplaceRefundOrigin.BuyerCancellation : MarketplaceRefundOrigin.OwnerCancellation;
                var refund = await refunds.RegisterRefundAsync(
                    reservation, origin, reason ?? "Cancelada por el comprador antes del inicio", pushes, ct);
                var fee = asBuyer ? null : await refunds.ChargeOwnerCommissionAsync(reservation, reason!, now, ct);

                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                    MarketplaceEventActions.ReservationCancelled, from.ToString(), reservation.Status.ToString(),
                    new
                    {
                        By = actor.ToString(), Reason = reason, RefundAmount = refund.Amount,
                        reservation.CommissionAmount, OwnerFeeFromBalance = fee?.FromBalance, OwnerFeeToDebt = fee?.ToDebt
                    });

                var title = reservation.Listing?.Title ?? "el espacio";
                var range = MarketplaceNotices.FormatRange(reservation.StartsAtUtc, reservation.EndsAtUtc);
                if (asBuyer)
                {
                    MarketplaceNotices.Add(db, reservation.CompanyId, reservation.OwnerId, NotificationType.MarketplaceReservationCancelled,
                        "Reserva cancelada",
                        $"Cancelaron la reserva de {title} {range}. El horario quedó libre y no se te acredita nada por esta reserva.",
                        nameof(MarketplaceReservation), reservation.Id, pushes);
                }
                else
                {
                    MarketplaceNotices.Add(db, reservation.CompanyId, reservation.BuyerUserId, NotificationType.MarketplaceReservationCancelled,
                        "El propietario canceló tu reserva",
                        $"El propietario canceló tu reserva de {title} {range}. Motivo: {reason}. Se te devuelve todo " +
                        $"({MarketplaceNotices.Gs(refund.Amount)}) hasta en {MarketplaceCancellationRules.RefundMaxHours} horas.",
                        nameof(MarketplaceReservation), reservation.Id, pushes);
                    MarketplaceNotices.Add(db, reservation.CompanyId, reservation.OwnerId, NotificationType.MarketplaceReservationCancelled,
                        "Cancelaste una reserva",
                        $"Cancelaste la reserva {reservation.Reference}: se le devuelve todo al comprador. {FeeSentence(reservation, fee!)}",
                        nameof(MarketplaceReservation), reservation.Id, pushes);
                }

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException)
        {
            // Dos cancelaciones a la vez: la base dejo pasar solo una (indice unico del reembolso).
            db.ChangeTracker.Clear();
            return MarketplaceResult<Guid>.Fail(MarketplaceError.Conflict("Esta reserva ya fue cancelada."));
        }

        if (failure is not null)
        {
            return failure;
        }

        await MarketplaceNotices.DispatchAsync(push, pushes);
        return MarketplaceResult<Guid>.Success(id);
    }

    // Lo que le pasa al propietario con la comision que asume (de su saldo a favor y/o como deuda por gestion).
    internal static string FeeSentence(MarketplaceReservation reservation, MarketplaceFeeOutcome fee)
    {
        var text = $"Asumís la comisión de gestión ({MarketplaceNotices.Gs(reservation.CommissionAmount)}).";
        if (fee.FromBalance > 0m)
        {
            text += $" Se descontaron {MarketplaceNotices.Gs(fee.FromBalance)} de tu saldo a favor.";
        }

        if (fee.ToDebt > 0m)
        {
            text += $" Los {MarketplaceNotices.Gs(fee.ToDebt)} restantes quedan como deuda por gestión y se descuentan de tu próxima acreditación del Marketplace.";
        }

        return text;
    }

    private static string BlockedMessage(MarketplaceReservationStatus status, bool asBuyer) => status switch
    {
        MarketplaceReservationStatus.Confirmed =>
            "El horario ya empezó, por eso no se puede cancelar. Si hubo un problema, usá «Reportar un problema».",
        MarketplaceReservationStatus.Completed => "La reserva ya terminó.",
        MarketplaceReservationStatus.InReview =>
            "El pago está en revisión. Esperá a que la administración lo confirme o lo rechace.",
        MarketplaceReservationStatus.PendingPayment when !asBuyer => "La reserva todavía no está pagada.",
        _ => "Esta reserva ya no está activa."
    };

    private async Task<MarketplaceResult<MarketplaceReservationDto>> ToBuyerDtoAsync(
        MarketplaceResult<Guid> result, Guid id, CancellationToken ct)
    {
        if (!result.Ok)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(result.Error!);
        }

        var dto = (await MarketplaceReservationViews.LoadBuyerViewAsync(
            db, db.MarketplaceReservations.AsNoTracking().Where(x => x.Id == id), ct)).Single();
        return MarketplaceResult<MarketplaceReservationDto>.Success(dto);
    }
}
