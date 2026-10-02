using System.Data;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Condo.Application.Abstractions;

namespace Condo.Api.Services;

/// <summary>Como se cubrio la comision que asume el propietario: de su saldo a favor y/o como deuda por gestion.</summary>
public sealed record MarketplaceFeeOutcome(decimal FromBalance, decimal ToDebt);

/// <summary>
/// Reembolsos pendientes al comprador, comision que asume el propietario cuando cancela y deudas por gestion. El dinero del
/// reembolso se devuelve fuera del sistema: el Encargado lo marca "devuelto" y recien ahi sale del extracto de la cuenta aparte.
/// Los metodos "dentro de la transaccion" no guardan: los llama quien cancela o resuelve un reclamo, que guarda todo junto.
/// </summary>
public class MarketplaceRefundService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    PushDispatcher push,
    OwnerCreditService credits)
{
    private const int BatchSize = 100;
    private const int ReturnedHistorySize = 50;

    // ── Dentro de la transaccion de quien cancela o resuelve ─────────────────

    /// <summary>
    /// Crea el reembolso pendiente de la reserva (monto segun el origen) y avisa a quienes lo devuelven. La reserva debe traer la
    /// publicacion cargada. Es unico por reserva (indice unico): una operacion no se reembolsa dos veces.
    /// </summary>
    public async Task<MarketplaceRefund> RegisterRefundAsync(
        MarketplaceReservation reservation, MarketplaceRefundOrigin origin, string reason,
        List<MarketplacePushItem> pushes, CancellationToken ct)
    {
        var amount = MarketplaceCancellationRules.RefundAmount(origin, reservation.BaseAmount, reservation.TotalAmount);
        var refund = new MarketplaceRefund
        {
            CompanyId = reservation.CompanyId,
            ReservationId = reservation.Id,
            BuildingId = reservation.BuildingId,
            RecipientUserId = reservation.BuyerUserId,
            Amount = amount,
            Origin = origin,
            Reason = reason,
            Status = MarketplaceRefundStatus.Pending
        };
        db.MarketplaceRefunds.Add(refund);

        audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceRefund), refund.Id,
            MarketplaceEventActions.RefundCreated, null, refund.Status.ToString(),
            new { reservation.Reference, Amount = amount, Origin = origin.ToString(), Reason = reason });

        var title = reservation.Listing?.Title ?? "una reserva";
        var body = $"{title} · {MarketplaceNotices.Gs(amount)} · {reservation.Reference}. Devolvélo al comprador (hasta en " +
                   $"{MarketplaceCancellationRules.RefundMaxHours} horas) y marcalo como devuelto desde Marketplace.";
        foreach (var reviewerId in await MarketplaceNotices.ReviewerIdsAsync(db, reservation.CompanyId, reservation.BuildingId, ct))
        {
            MarketplaceNotices.Add(db, reservation.CompanyId, reviewerId, NotificationType.MarketplaceRefundPending,
                "Reembolso pendiente", body, nameof(MarketplaceRefund), refund.Id, pushes);
        }

        return refund;
    }

    /// <summary>
    /// El propietario asume la comision de la gestion (cancelo, o perdio un reclamo): se le descuenta de su saldo a favor y, si no
    /// alcanza, lo que falta queda como deuda por gestion, que se descuenta de su proxima acreditacion del marketplace. No toca
    /// las expensas. Lo descontado del saldo entra a la cuenta aparte como "comision por cancelacion".
    /// </summary>
    public async Task<MarketplaceFeeOutcome> ChargeOwnerCommissionAsync(
        MarketplaceReservation reservation, string reason, DateTime now, CancellationToken ct)
    {
        var fee = reservation.CommissionAmount;
        if (fee <= 0m)
        {
            return new MarketplaceFeeOutcome(0m, 0m);
        }

        var credit = await db.OwnerCredits
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == reservation.OwnerId && x.CompanyId == reservation.CompanyId, ct);
        var fromBalance = credit is null ? 0m : Math.Min(credit.Amount, fee);

        if (credit is not null && fromBalance > 0m)
        {
            // Se consume de los lotes de saldo (el mas antiguo primero), como cualquier uso del saldo: queda trazable.
            var lots = await credits.EnsureLotsAsync(reservation.OwnerId, reservation.CompanyId, credit.Amount, ct);
            var pending = fromBalance;
            foreach (var lot in lots.Where(x => x.RemainingAmount > 0m))
            {
                if (pending <= 0m)
                {
                    break;
                }

                var take = decimal.Min(lot.RemainingAmount, pending);
                lot.RemainingAmount -= take;
                pending -= take;

                db.OwnerCreditMovements.Add(new OwnerCreditMovement
                {
                    CompanyId = reservation.CompanyId,
                    OwnerId = reservation.OwnerId,
                    Kind = OwnerCreditMovementKind.Applied,
                    Amount = take,
                    RemainingAmount = 0m,
                    SourceReference = lot.SourceReference,
                    OwnerPaymentId = lot.OwnerPaymentId,
                    MarketplaceReservationId = reservation.Id,
                    Description = $"Descontado por la comisión de gestión de la reserva {reservation.Reference} del Marketplace ({reason})"
                });
            }

            credit.Amount -= fromBalance;

            db.MarketplaceAccountMovements.Add(new MarketplaceAccountMovement
            {
                CompanyId = reservation.CompanyId,
                BuildingId = reservation.BuildingId,
                Kind = MarketplaceAccountMovementKind.CancellationFee,
                Amount = fromBalance,
                ReservationId = reservation.Id,
                Concept = $"Comisión asumida por el propietario: reserva {reservation.Reference} ({reservation.Listing?.Title})",
                CreatedByUserId = null,
                OccurredAtUtc = now
            });
        }

        var toDebt = fee - fromBalance;
        if (toDebt > 0m)
        {
            var debt = new MarketplaceOwnerDebt
            {
                CompanyId = reservation.CompanyId,
                BuildingId = reservation.BuildingId,
                OwnerId = reservation.OwnerId,
                ReservationId = reservation.Id,
                Amount = toDebt,
                PaidAmount = 0m,
                Reason = reason
            };
            db.MarketplaceOwnerDebts.Add(debt);
            audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceOwnerDebt), debt.Id,
                MarketplaceEventActions.OwnerDebtCreated, null, "Pending",
                new { reservation.Reference, reservation.OwnerId, Amount = toDebt, Reason = reason });
        }

        audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
            MarketplaceEventActions.OwnerFeeCharged, null, null,
            new { reservation.Reference, reservation.OwnerId, Fee = fee, FromBalance = fromBalance, ToDebt = toDebt, Reason = reason });

        return new MarketplaceFeeOutcome(fromBalance, toDebt);
    }

    // ── Lista del Encargado ──────────────────────────────────────────────────

    /// <summary>Reembolsos del edificio: los pendientes primero (el mas antiguo arriba) y, si se pide, los ultimos devueltos.</summary>
    public async Task<MarketplaceResult<List<MarketplaceRefundDto>>> GetRefundsAsync(Guid buildingId, bool includeReturned, CancellationToken ct)
    {
        var staff = await RequireStaffAsync(buildingId, ct);
        if (!staff.Ok)
        {
            return MarketplaceResult<List<MarketplaceRefundDto>>.Fail(staff.Error!);
        }

        var pending = await ToDtosAsync(db.MarketplaceRefunds.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == MarketplaceRefundStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc), ct);

        if (includeReturned)
        {
            pending.AddRange(await ToDtosAsync(db.MarketplaceRefunds.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == MarketplaceRefundStatus.Returned)
                .OrderByDescending(x => x.ReturnedAtUtc).Take(ReturnedHistorySize), ct));
        }

        return MarketplaceResult<List<MarketplaceRefundDto>>.Success(pending);
    }

    /// <summary>Deudas por gestion pendientes de los propietarios del edificio (se descuentan de su proxima acreditacion).</summary>
    public async Task<MarketplaceResult<List<MarketplaceOwnerDebtDto>>> GetOwnerDebtsAsync(Guid buildingId, CancellationToken ct)
    {
        var staff = await RequireStaffAsync(buildingId, ct);
        if (!staff.Ok)
        {
            return MarketplaceResult<List<MarketplaceOwnerDebtDto>>.Fail(staff.Error!);
        }

        var rows = await db.MarketplaceOwnerDebts.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.SettledAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.ReservationId, x.BuildingId, x.Amount, x.PaidAmount, x.Reason, x.CreatedAtUtc,
                Reference = x.Reservation != null ? x.Reservation.Reference : string.Empty,
                OwnerName = x.Owner != null ? x.Owner.FullName : string.Empty
            })
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceOwnerDebtDto>>.Success(rows.Select(x => new MarketplaceOwnerDebtDto
        {
            Id = x.Id,
            ReservationId = x.ReservationId,
            BuildingId = x.BuildingId,
            Reference = x.Reference,
            OwnerName = x.OwnerName,
            Amount = x.Amount,
            PaidAmount = x.PaidAmount,
            Remaining = x.Amount - x.PaidAmount,
            Reason = x.Reason,
            CreatedAtUtc = MarketplaceReservationViews.AsUtc(x.CreatedAtUtc)
        }).ToList());
    }

    // ── Marcar como devuelto ─────────────────────────────────────────────────

    /// <summary>
    /// El Encargado devolvio el dinero (fuera del sistema) y lo marca: queda quien y cuando, y el extracto de la cuenta registra la
    /// salida "devolucion al comprador" una sola vez (indice unico). Marcarlo dos veces no duplica nada.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceRefundDto>> MarkReturnedAsync(Guid refundId, CancellationToken ct)
    {
        var buildingId = await db.MarketplaceRefunds.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == refundId).Select(x => (Guid?)x.BuildingId).FirstOrDefaultAsync(ct);
        if (buildingId is null)
        {
            return MarketplaceResult<MarketplaceRefundDto>.Fail(MarketplaceError.NotFound());
        }

        var staff = await RequireStaffAsync(buildingId.Value, ct);
        if (!staff.Ok)
        {
            return MarketplaceResult<MarketplaceRefundDto>.Fail(staff.Error!);
        }

        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<MarketplaceRefundDto>? failure = null;

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

                var refund = await db.MarketplaceRefunds.FirstAsync(x => x.Id == refundId, ct);
                if (refund.Status != MarketplaceRefundStatus.Pending)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<MarketplaceRefundDto>.Fail(MarketplaceError.Conflict("Este reembolso ya fue marcado como devuelto."));
                    return;
                }

                var reservation = await db.MarketplaceReservations.Include(x => x.Listing).FirstAsync(x => x.Id == refund.ReservationId, ct);

                refund.Status = MarketplaceRefundStatus.Returned;
                refund.ReturnedAtUtc = now;
                refund.ReturnedByUserId = tenant.UserId;

                var movement = new MarketplaceAccountMovement
                {
                    CompanyId = refund.CompanyId,
                    BuildingId = refund.BuildingId,
                    Kind = MarketplaceAccountMovementKind.RefundOut,
                    Amount = -refund.Amount,
                    ReservationId = refund.ReservationId,
                    Concept = $"Devolución al comprador: reserva {reservation.Reference} ({reservation.Listing?.Title})",
                    CreatedByUserId = tenant.UserId,
                    OccurredAtUtc = now
                };
                db.MarketplaceAccountMovements.Add(movement);

                audit.Record(refund.CompanyId, refund.BuildingId, nameof(MarketplaceRefund), refund.Id,
                    MarketplaceEventActions.RefundReturned, MarketplaceRefundStatus.Pending.ToString(), refund.Status.ToString(),
                    new { reservation.Reference, refund.Amount });
                audit.Record(refund.CompanyId, refund.BuildingId, nameof(MarketplaceAccountMovement), movement.Id,
                    MarketplaceEventActions.AccountMovementRecorded, null, movement.Kind.ToString(),
                    new { movement.Amount, reservation.Reference });

                MarketplaceNotices.Add(db, refund.CompanyId, refund.RecipientUserId, NotificationType.MarketplaceRefundReturned,
                    "Reembolso realizado",
                    $"Te devolvieron {MarketplaceNotices.Gs(refund.Amount)} de tu reserva de {reservation.Listing?.Title ?? "el espacio"} ({reservation.Reference}).",
                    nameof(MarketplaceReservation), reservation.Id, pushes);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException)
        {
            // Dos marcas a la vez: la base dejo pasar solo una.
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceRefundDto>.Fail(MarketplaceError.Conflict("Este reembolso ya fue marcado como devuelto."));
        }

        if (failure is not null)
        {
            return failure;
        }

        await MarketplaceNotices.DispatchAsync(push, pushes);
        var dto = (await ToDtosAsync(db.MarketplaceRefunds.AsNoTracking().Where(x => x.Id == refundId), ct)).Single();
        return MarketplaceResult<MarketplaceRefundDto>.Success(dto);
    }

    // ── Alerta a las 72 horas ────────────────────────────────────────────────

    /// <summary>
    /// Si pasaron 72 horas desde que se creo el reembolso y sigue sin devolverse, avisa UNA vez a quienes lo devuelven. Lo corre el
    /// proceso de fondo.
    /// </summary>
    public async Task<int> SendOverdueAlertsAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        var threshold = now - MarketplaceCancellationRules.RefundMaxTime;

        var overdue = await db.MarketplaceRefunds
            .Include(x => x.Reservation).ThenInclude(r => r!.Listing)
            .Where(x => !x.IsDeleted && x.Status == MarketplaceRefundStatus.Pending
                        && x.OverdueAlertSentAtUtc == null && x.CreatedAtUtc <= threshold)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var pushes = new List<MarketplacePushItem>();
        foreach (var refund in overdue)
        {
            refund.OverdueAlertSentAtUtc = now;
            var reservation = refund.Reservation!;
            var body = $"{reservation.Listing?.Title ?? "Una reserva"} · {MarketplaceNotices.Gs(refund.Amount)} · {reservation.Reference}. " +
                       $"Pasaron {MarketplaceCancellationRules.RefundMaxHours} horas: devolvé el dinero al comprador y marcalo como devuelto.";

            foreach (var reviewerId in await MarketplaceNotices.ReviewerIdsAsync(db, refund.CompanyId, refund.BuildingId, ct))
            {
                MarketplaceNotices.Add(db, refund.CompanyId, reviewerId, NotificationType.MarketplaceRefundOverdue,
                    "Reembolso vencido", body, nameof(MarketplaceRefund), refund.Id, pushes);
            }

            audit.Record(refund.CompanyId, refund.BuildingId, nameof(MarketplaceRefund), refund.Id,
                MarketplaceEventActions.RefundOverdueAlert, refund.Status.ToString(), refund.Status.ToString(),
                new { reservation.Reference, refund.Amount }, automatic: true);
        }

        if (overdue.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await MarketplaceNotices.DispatchAsync(push, pushes);
        }

        return overdue.Count;
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    // Solo el personal con el edificio en su alcance (los mismos que revisan pagos). Un edificio ajeno responde 404.
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

    private async Task<List<MarketplaceRefundDto>> ToDtosAsync(IQueryable<MarketplaceRefund> query, CancellationToken ct)
    {
        var rows = await query
            .Select(x => new
            {
                x.Id, x.ReservationId, x.BuildingId, x.Amount, x.Origin, x.Reason, x.Status, x.CreatedAtUtc, x.ReturnedAtUtc,
                x.ReturnedByUserId,
                Reference = x.Reservation!.Reference,
                Title = x.Reservation.Listing != null ? x.Reservation.Listing.Title : string.Empty,
                UnitCode = x.Reservation.Unit != null ? x.Reservation.Unit.Code : string.Empty,
                BuyerName = x.Recipient != null ? x.Recipient.FullName : string.Empty,
                ReturnedByName = x.ReturnedByUser != null ? x.ReturnedByUser.FullName : null
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        return rows.Select(x =>
        {
            var due = MarketplaceReservationViews.AsUtc(x.CreatedAtUtc) + MarketplaceCancellationRules.RefundMaxTime;
            return new MarketplaceRefundDto
            {
                Id = x.Id,
                ReservationId = x.ReservationId,
                BuildingId = x.BuildingId,
                Reference = x.Reference,
                Title = x.Title,
                UnitCode = x.UnitCode,
                BuyerName = x.BuyerName,
                Amount = x.Amount,
                Origin = x.Origin.ToString(),
                Reason = x.Reason,
                Status = x.Status.ToString(),
                CreatedAtUtc = MarketplaceReservationViews.AsUtc(x.CreatedAtUtc),
                DueAtUtc = due,
                Overdue = x.Status == MarketplaceRefundStatus.Pending && due <= now,
                ReturnedAtUtc = x.ReturnedAtUtc.HasValue ? MarketplaceReservationViews.AsUtc(x.ReturnedAtUtc.Value) : null,
                ReturnedByName = x.ReturnedByName
            };
        }).ToList();
    }
}
