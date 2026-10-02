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
/// De la reserva confirmada al saldo a favor del propietario. Terminada la reserva pasa a "finalizada"; pasadas 24 horas sin
/// reclamo, su neto (el precio que fijo el propietario, sin la comision) se suma al saldo a favor que el propietario YA tiene
/// (OwnerCredit) como un lote nuevo con la reserva de origen. No hay billetera nueva: el motor de expensas usa ese saldo como
/// siempre. Cada paso es idempotente: indices unicos y comprobacion de estado hacen que una reserva se acredite una sola vez.
/// </summary>
public class MarketplaceCreditService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceAudit audit,
    PushDispatcher push)
{
    // Ventana de reclamo: la ganancia se acredita 24 horas despues del fin de la reserva, si no hubo reclamo.
    public static readonly TimeSpan ClaimWindow = MarketplaceCancellationRules.ClaimWindow;

    private const int BatchSize = 100;
    private const int ReasonMaxLength = 500;

    // ── Proceso de fondo ─────────────────────────────────────────────────────

    /// <summary>Pasa a "finalizadas" las reservas confirmadas que ya terminaron y acredita las que cumplieron la ventana de reclamo.</summary>
    public async Task<(int Completed, int Credited)> ProcessDueAsync(CancellationToken ct)
    {
        var completed = await CompleteFinishedAsync(ct);
        var credited = await CreditDueAsync(ct);
        return (completed, credited);
    }

    /// <summary>Confirmed -> Completed cuando termino el horario de la reserva.</summary>
    public async Task<int> CompleteFinishedAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;

        var finished = await db.MarketplaceReservations
            .Where(x => !x.IsDeleted && x.Status == MarketplaceReservationStatus.Confirmed && x.EndsAtUtc <= now)
            .OrderBy(x => x.EndsAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var reservation in finished)
        {
            MarketplaceStateMachine.EnsureTransition(reservation.Status, MarketplaceReservationStatus.Completed);
            reservation.Status = MarketplaceReservationStatus.Completed;

            audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                MarketplaceEventActions.ReservationCompleted, MarketplaceReservationStatus.Confirmed.ToString(),
                reservation.Status.ToString(), new { reservation.Reference, reservation.EndsAtUtc }, automatic: true);
        }

        if (finished.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return finished.Count;
    }

    /// <summary>Acredita al saldo del propietario las reservas finalizadas cuya ventana de reclamo ya paso.</summary>
    public async Task<int> CreditDueAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var threshold = DateTime.UtcNow - ClaimWindow;

        var ids = await db.MarketplaceReservations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == MarketplaceReservationStatus.Completed
                        && x.CreditStatus == MarketplaceCreditStatus.Pending && x.EndsAtUtc <= threshold)
            .OrderBy(x => x.EndsAtUtc)
            .Select(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var credited = 0;
        foreach (var id in ids)
        {
            if (await CreditOneAsync(id, ct))
            {
                credited++;
            }
        }

        return credited;
    }

    // Una reserva por transaccion: si una falla (por ejemplo una carrera con otra ejecucion), las demas siguen.
    private async Task<bool> CreditOneAsync(Guid reservationId, CancellationToken ct)
    {
        var pushes = new List<MarketplacePushItem>();
        var credited = false;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                pushes.Clear();
                credited = false;
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var now = DateTime.UtcNow;

                var reservation = await db.MarketplaceReservations.Include(x => x.Listing).FirstAsync(x => x.Id == reservationId, ct);

                // Idempotencia: solo se acredita una reserva finalizada con la acreditacion pendiente.
                if (reservation.Status != MarketplaceReservationStatus.Completed
                    || reservation.CreditStatus != MarketplaceCreditStatus.Pending
                    || AsUtc(reservation.EndsAtUtc) + ClaimWindow > now)
                {
                    await transaction.RollbackAsync(ct);
                    return;
                }

                MarketplaceStateMachine.EnsureTransition(reservation.CreditStatus, MarketplaceCreditStatus.Credited);

                // Deuda por gestion pendiente del propietario en este edificio (comision que asumio al cancelar): se descuenta de lo
                // que se le acredita, la mas antigua primero. No toca las expensas. Si la deuda iguala o supera la ganancia, no
                // se genera saldo: la reserva se aplica a la deuda.
                var net = reservation.OwnerNetAmount;
                var debts = await db.MarketplaceOwnerDebts
                    .Where(x => !x.IsDeleted && x.OwnerId == reservation.OwnerId && x.CompanyId == reservation.CompanyId
                                && x.BuildingId == reservation.BuildingId && x.SettledAtUtc == null)
                    .OrderBy(x => x.CreatedAtUtc)
                    .ToListAsync(ct);

                var deducted = 0m;
                foreach (var debt in debts)
                {
                    var room = net - deducted;
                    if (room <= 0m)
                    {
                        break;
                    }

                    var take = Math.Min(debt.Amount - debt.PaidAmount, room);
                    debt.PaidAmount += take;
                    deducted += take;
                    if (debt.PaidAmount >= debt.Amount)
                    {
                        debt.SettledAtUtc = now;
                    }

                    audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceOwnerDebt), debt.Id,
                        MarketplaceEventActions.OwnerDebtDeducted, null, debt.SettledAtUtc.HasValue ? "Settled" : "Pending",
                        new { reservation.Reference, Deducted = take, debt.PaidAmount, debt.Amount }, automatic: true);
                }

                var amount = net - deducted;
                var credit = await db.OwnerCredits
                    .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == reservation.OwnerId && x.CompanyId == reservation.CompanyId, ct);
                var previousBalance = credit?.Amount ?? 0m;
                var newBalance = previousBalance;

                if (amount > 0m)
                {
                    if (credit is null)
                    {
                        credit = new OwnerCredit { CompanyId = reservation.CompanyId, OwnerId = reservation.OwnerId, Amount = 0m };
                        db.OwnerCredits.Add(credit);
                    }

                    credit.Amount += amount;
                    newBalance = credit.Amount;

                    // Lote de saldo con su origen: asi cada guarani se puede rastrear hasta la reserva (y, al usarse, hasta el cargo).
                    db.OwnerCreditMovements.Add(new OwnerCreditMovement
                    {
                        CompanyId = reservation.CompanyId,
                        OwnerId = reservation.OwnerId,
                        Kind = OwnerCreditMovementKind.Generated,
                        Amount = amount,
                        RemainingAmount = amount,
                        SourceReference = reservation.Reference,
                        MarketplaceReservationId = reservation.Id,
                        Description = $"Saldo a favor generado por la reserva {reservation.Reference} del Marketplace ({reservation.Listing?.Title})" +
                                      (deducted > 0m ? $" — se descontaron Gs. {deducted:N0} de deuda por gestión" : string.Empty)
                    });
                }

                reservation.CreditStatus = MarketplaceCreditStatus.Credited;
                reservation.CreditedAtUtc = now;

                // Salida de la cuenta aparte del edificio (una sola vez por reserva: indice unico). Lo descontado por deuda no sale:
                // queda en la cuenta como ganancia de la gestion.
                MarketplaceAccountMovement? movement = null;
                if (amount > 0m)
                {
                    movement = new MarketplaceAccountMovement
                    {
                        CompanyId = reservation.CompanyId,
                        BuildingId = reservation.BuildingId,
                        Kind = MarketplaceAccountMovementKind.OwnerCredit,
                        Amount = -amount,
                        ReservationId = reservation.Id,
                        Concept = $"Acreditado al saldo del propietario: reserva {reservation.Reference} ({reservation.Listing?.Title})" +
                                  (deducted > 0m ? $" — neto de Gs. {deducted:N0} de deuda por gestión" : string.Empty),
                        CreatedByUserId = null,
                        OccurredAtUtc = now
                    };
                    db.MarketplaceAccountMovements.Add(movement);
                }

                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                    MarketplaceEventActions.CreditApplied, MarketplaceCreditStatus.Pending.ToString(), MarketplaceCreditStatus.Credited.ToString(),
                    new { reservation.OwnerId, Amount = amount, DebtDeducted = deducted, PreviousBalance = previousBalance, NewBalance = newBalance },
                    automatic: true);
                if (movement is not null)
                {
                    audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceAccountMovement), movement.Id,
                        MarketplaceEventActions.AccountMovementRecorded, null, movement.Kind.ToString(),
                        new { movement.Amount, reservation.Reference }, automatic: true);
                }

                const string heading = "Saldo a favor acreditado";
                var spaceName = reservation.Listing?.Title ?? "tu espacio";
                var body = amount > 0m
                    ? $"Se acreditaron Gs. {amount:N0} a tu saldo a favor por la reserva de {spaceName} del Marketplace. " +
                      (deducted > 0m ? $"Se descontaron Gs. {deducted:N0} de tu deuda por gestión. " : string.Empty) +
                      "Se usa en tu próximo pago de expensas."
                    : $"La reserva de {spaceName} del Marketplace se aplicó a tu deuda por gestión (Gs. {deducted:N0}): no se acreditó saldo.";
                db.Notifications.Add(new Notification
                {
                    CompanyId = reservation.CompanyId,
                    RecipientId = reservation.OwnerId,
                    Type = NotificationType.MarketplaceCreditApplied,
                    Title = heading,
                    Body = body,
                    EntityType = nameof(MarketplaceReservation),
                    EntityId = reservation.Id
                });
                pushes.Add(new MarketplacePushItem(reservation.OwnerId, heading, body, reservation.Id));

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                credited = true;
            });
        }
        catch (DbUpdateException)
        {
            // Otra ejecucion acredito esta reserva primero: los indices unicos dejaron pasar solo una. Nada se duplica.
            db.ChangeTracker.Clear();
            return false;
        }

        foreach (var item in pushes)
        {
            await push.NotifyUserAsync(item.RecipientId, item.Title, item.Body, item.EntityType, item.EntityId, CancellationToken.None);
        }

        return credited;
    }

    // ── Reversa (solo SuperAdmin) ────────────────────────────────────────────

    /// <summary>
    /// Revierte una acreditacion cuyo saldo todavia esta intacto (no se uso en expensas): el lote queda en cero, el saldo del
    /// propietario baja y el extracto registra el reintegro a la cuenta. Si el saldo ya se uso, la reversa la resuelve el
    /// SuperAdmin a mano fuera del sistema.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceReversalDto>> ReverseCreditAsync(Guid reservationId, string? reason, CancellationToken ct)
    {
        if (!tenant.IsSuperAdmin)
        {
            return MarketplaceResult<MarketplaceReversalDto>.Fail(
                MarketplaceError.Forbidden("Solo el SuperAdmin puede revertir una acreditación."));
        }

        var cleanReason = (reason ?? string.Empty).Trim();
        if (cleanReason.Length == 0)
        {
            return MarketplaceResult<MarketplaceReversalDto>.Fail(MarketplaceError.BadRequest("Indicá el motivo de la reversa."));
        }

        if (cleanReason.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceReversalDto>.Fail(
                MarketplaceError.BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres."));
        }

        if (!await db.MarketplaceReservations.AsNoTracking().AnyAsync(x => !x.IsDeleted && x.Id == reservationId, ct))
        {
            return MarketplaceResult<MarketplaceReversalDto>.Fail(MarketplaceError.NotFound());
        }

        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<MarketplaceReversalDto>? outcome = null;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                pushes.Clear();
                outcome = null;
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var now = DateTime.UtcNow;

                var reservation = await db.MarketplaceReservations.Include(x => x.Listing).FirstAsync(x => x.Id == reservationId, ct);
                if (reservation.CreditStatus != MarketplaceCreditStatus.Credited)
                {
                    await transaction.RollbackAsync(ct);
                    outcome = MarketplaceResult<MarketplaceReversalDto>.Fail(MarketplaceError.Conflict(
                        reservation.CreditStatus == MarketplaceCreditStatus.Reversed
                            ? "Esta acreditación ya fue revertida."
                            : "Esta reserva todavía no tiene el saldo acreditado."));
                    return;
                }

                var lot = await db.OwnerCreditMovements.FirstOrDefaultAsync(x => !x.IsDeleted
                    && x.MarketplaceReservationId == reservation.Id && x.Kind == OwnerCreditMovementKind.Generated, ct);
                if (lot is null)
                {
                    await transaction.RollbackAsync(ct);
                    outcome = MarketplaceResult<MarketplaceReversalDto>.Fail(MarketplaceError.Conflict(
                        "Esta reserva no generó saldo (se aplicó a una deuda por gestión del propietario): no hay nada que revertir."));
                    return;
                }

                if (lot.RemainingAmount != lot.Amount)
                {
                    await transaction.RollbackAsync(ct);
                    outcome = MarketplaceResult<MarketplaceReversalDto>.Fail(MarketplaceError.Conflict(
                        "El saldo de esta reserva ya se usó en expensas: no se puede revertir desde acá. Resolvelo a mano."));
                    return;
                }

                var credit = await db.OwnerCredits.FirstAsync(x => !x.IsDeleted && x.OwnerId == reservation.OwnerId && x.CompanyId == reservation.CompanyId, ct);
                if (credit.Amount < lot.Amount)
                {
                    await transaction.RollbackAsync(ct);
                    outcome = MarketplaceResult<MarketplaceReversalDto>.Fail(MarketplaceError.Conflict(
                        "El saldo del propietario no alcanza para revertir esta acreditación."));
                    return;
                }

                MarketplaceStateMachine.EnsureTransition(reservation.CreditStatus, MarketplaceCreditStatus.Reversed);

                var amount = lot.Amount;
                credit.Amount -= amount;
                lot.RemainingAmount = 0m;
                lot.Description += $" — REVERTIDO el {now:dd/MM/yyyy}: {cleanReason}";
                reservation.CreditStatus = MarketplaceCreditStatus.Reversed;

                // El dinero vuelve a la cuenta aparte: ajuste manual con el motivo.
                var movement = new MarketplaceAccountMovement
                {
                    CompanyId = reservation.CompanyId,
                    BuildingId = reservation.BuildingId,
                    Kind = MarketplaceAccountMovementKind.Adjustment,
                    Amount = amount,
                    ReservationId = reservation.Id,
                    Concept = $"Reversa de la acreditación de la reserva {reservation.Reference}: {cleanReason}",
                    CreatedByUserId = tenant.UserId,
                    OccurredAtUtc = now
                };
                db.MarketplaceAccountMovements.Add(movement);

                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                    MarketplaceEventActions.CreditReversed, MarketplaceCreditStatus.Credited.ToString(), MarketplaceCreditStatus.Reversed.ToString(),
                    new { reservation.OwnerId, Amount = amount, Reason = cleanReason, NewBalance = credit.Amount });
                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceAccountMovement), movement.Id,
                    MarketplaceEventActions.AccountMovementRecorded, null, movement.Kind.ToString(),
                    new { movement.Amount, reservation.Reference, Reason = cleanReason });

                const string heading = "Saldo a favor revertido";
                var body = $"Se revirtió la acreditación de Gs. {amount:N0} de la reserva {reservation.Reference}. Motivo: {cleanReason}";
                db.Notifications.Add(new Notification
                {
                    CompanyId = reservation.CompanyId,
                    RecipientId = reservation.OwnerId,
                    Type = NotificationType.MarketplaceCreditReversed,
                    Title = heading,
                    Body = body,
                    EntityType = nameof(MarketplaceReservation),
                    EntityId = reservation.Id
                });
                pushes.Add(new MarketplacePushItem(reservation.OwnerId, heading, body, reservation.Id));

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                outcome = MarketplaceResult<MarketplaceReversalDto>.Success(new MarketplaceReversalDto
                {
                    ReservationId = reservation.Id,
                    Reference = reservation.Reference,
                    Amount = amount,
                    OwnerBalance = credit.Amount
                });
            });
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceReversalDto>.Fail(MarketplaceError.Conflict("Esta acreditación ya fue revertida."));
        }

        foreach (var item in pushes)
        {
            await push.NotifyUserAsync(item.RecipientId, item.Title, item.Body, item.EntityType, item.EntityId, CancellationToken.None);
        }

        return outcome!;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
