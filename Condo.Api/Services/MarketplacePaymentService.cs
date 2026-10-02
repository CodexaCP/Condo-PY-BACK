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
/// Pago de una reserva y su revision. El comprador sube el comprobante (la reserva deja de correr el plazo y queda bloqueada
/// hasta que se revise); el personal del edificio confirma o rechaza. Confirmar exige que el monto del comprobante coincida
/// con el total esperado, asienta el ingreso en la cuenta aparte UNA sola vez y no se puede repetir; rechazar cierra la reserva.
/// </summary>
public class MarketplacePaymentService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    PushDispatcher push,
    IUnitOverdueService overdue)
{
    private const int ReasonMaxLength = 500;
    private const int ComprobanteMaxLength = 500;

    // Alertas al revisor: una al subir el comprobante, otra a los 15 minutos y despues cada hora hasta que se resuelva.
    public static readonly TimeSpan FirstReminderAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan NextReminderEvery = TimeSpan.FromHours(1);
    // Pasado este tiempo desde el fin de la reserva ya no se insiste (lo resuelve el Encargado a mano).
    public static readonly TimeSpan StopRemindingAfterEnd = TimeSpan.FromHours(24);

    // ── Comprador ────────────────────────────────────────────────────────────

    /// <summary>
    /// Datos para pagar. Los datos para transferir solo se muestran mientras la reserva espera el pago: no son un dato que se
    /// vea siempre.
    /// </summary>
    public async Task<MarketplaceResult<MarketplacePaymentInfoDto>> GetPaymentInfoAsync(Guid reservationId, CancellationToken ct)
    {
        var row = await db.MarketplaceReservations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == reservationId && x.BuyerUserId == tenant.UserId)
            .Select(x => new
            {
                x.Id, x.Reference, x.BuildingId, x.Status, x.TotalAmount, x.ExpiresAtUtc,
                Title = x.Listing != null ? x.Listing.Title : string.Empty
            })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return MarketplaceResult<MarketplacePaymentInfoDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(row.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplacePaymentInfoDto>.Fail(MarketplaceError.FromAccess(access));
        }

        if (row.Status != MarketplaceReservationStatus.PendingPayment || IsPastDue(row.ExpiresAtUtc))
        {
            return MarketplaceResult<MarketplacePaymentInfoDto>.Fail(MarketplaceError.Conflict(
                row.Status == MarketplaceReservationStatus.PendingPayment || row.Status == MarketplaceReservationStatus.Expired
                    ? "La reserva ya venció."
                    : "Esta reserva ya no espera el pago."));
        }

        var transferInfo = await db.Buildings.AsNoTracking()
            .Where(x => x.Id == row.BuildingId).Select(x => x.MarketplaceTransferInfo).FirstOrDefaultAsync(ct);

        return MarketplaceResult<MarketplacePaymentInfoDto>.Success(new MarketplacePaymentInfoDto
        {
            ReservationId = row.Id,
            Reference = row.Reference,
            Title = row.Title,
            TotalAmount = row.TotalAmount,
            ExpiresAtUtc = row.ExpiresAtUtc.HasValue ? AsUtc(row.ExpiresAtUtc.Value) : null,
            TransferInfo = transferInfo ?? string.Empty
        });
    }

    /// <summary>
    /// El comprador sube el comprobante: se detiene el plazo, la reserva pasa a "en revision" (el horario sigue bloqueado hasta
    /// que se revise) y se avisa a quienes revisan. Subirlo dos veces o fuera de plazo se rechaza.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceReservationDto>> SubmitPaymentAsync(
        Guid reservationId, MarketplaceSubmitPaymentRequest request, CancellationToken ct)
    {
        var urlError = ValidateComprobanteUrl(request.ComprobanteUrl, out var url);
        if (urlError is not null)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.BadRequest(urlError));
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

        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<MarketplaceReservationDto>? failure = null;

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
                if (reservation.Status != MarketplaceReservationStatus.PendingPayment)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.Conflict(
                        reservation.Status == MarketplaceReservationStatus.Expired ? "La reserva ya venció." : "Esta reserva ya no espera el pago."));
                    return;
                }

                // Fuera de plazo no se acepta, aunque el proceso de fondo todavia no la haya marcado vencida.
                if (IsPastDue(reservation.ExpiresAtUtc))
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.Conflict(
                        "La reserva ya venció. Hacé una nueva reserva si todavía querés el espacio."));
                    return;
                }

                MarketplaceStateMachine.EnsureTransition(reservation.Status, MarketplaceReservationStatus.InReview);
                var fromStatus = reservation.Status;
                reservation.Status = MarketplaceReservationStatus.InReview;
                // El plazo de 10 minutos corre solo hasta que se sube el comprobante.
                reservation.ExpiresAtUtc = null;

                var payment = new MarketplacePayment
                {
                    CompanyId = reservation.CompanyId,
                    ReservationId = reservation.Id,
                    BuildingId = reservation.BuildingId,
                    BuyerUserId = reservation.BuyerUserId,
                    ComprobanteUrl = url,
                    ExpectedAmount = reservation.TotalAmount,
                    Status = MarketplacePaymentStatus.Submitted,
                    SubmittedAtUtc = now,
                    // La primera alerta al revisor sale ahora mismo.
                    AlertCount = 1,
                    LastAlertAtUtc = now
                };
                db.MarketplacePayments.Add(payment);

                audit.Record(payment.CompanyId, payment.BuildingId, nameof(MarketplacePayment), payment.Id,
                    MarketplaceEventActions.PaymentSubmitted, null, payment.Status.ToString(),
                    new { reservation.Reference, payment.ExpectedAmount, payment.ComprobanteUrl });
                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                    MarketplaceEventActions.ReservationInReview, fromStatus.ToString(), reservation.Status.ToString(),
                    new { reservation.Reference, reservation.TotalAmount });

                await AddReviewAlertAsync(payment, reservation, isReminder: false, pushes, ct);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException)
        {
            // Dos subidas a la vez: la base dejo pasar solo una.
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.Conflict("Esta reserva ya espera el pago."));
        }

        if (failure is not null)
        {
            return failure;
        }

        await DispatchPushesAsync(pushes);
        return MarketplaceResult<MarketplaceReservationDto>.Success(await LoadReservationDtoAsync(reservationId, ct));
    }

    // ── Personal del edificio ────────────────────────────────────────────────

    /// <summary>Pagos esperando revision en el edificio, del mas antiguo al mas nuevo.</summary>
    public async Task<MarketplaceResult<List<MarketplaceReviewItemDto>>> GetPendingForReviewAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<List<MarketplaceReviewItemDto>>.Fail(MarketplaceError.FromAccess(access));
        }

        if (!access.Context!.IsStaff)
        {
            return MarketplaceResult<List<MarketplaceReviewItemDto>>.Fail(
                MarketplaceError.Forbidden("Solo el personal del edificio revisa los pagos."));
        }

        var query = db.MarketplacePayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == MarketplacePaymentStatus.Submitted);
        return MarketplaceResult<List<MarketplaceReviewItemDto>>.Success(await ToReviewItemsAsync(query, buildingId, ct));
    }

    public async Task<MarketplaceResult<MarketplaceReviewItemDto>> ApproveAsync(Guid paymentId, MarketplaceApproveRequest request, CancellationToken ct)
    {
        var gate = await RequireStaffForPaymentAsync(paymentId, ct);
        if (!gate.Ok)
        {
            return MarketplaceResult<MarketplaceReviewItemDto>.Fail(gate.Error!);
        }

        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<MarketplaceReviewItemDto>? failure = null;

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

                var payment = await db.MarketplacePayments.FirstAsync(x => x.Id == paymentId, ct);
                var reservation = await db.MarketplaceReservations.Include(x => x.Listing).FirstAsync(x => x.Id == payment.ReservationId, ct);

                if (payment.Status != MarketplacePaymentStatus.Submitted || reservation.Status != MarketplaceReservationStatus.InReview)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<MarketplaceReviewItemDto>.Fail(MarketplaceError.Conflict("Este pago ya fue revisado."));
                    return;
                }

                if (request.ReviewedAmount != payment.ExpectedAmount)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<MarketplaceReviewItemDto>.Fail(MarketplaceError.BadRequest(
                        $"El monto no coincide con el total esperado (Gs. {payment.ExpectedAmount:N0}). Si el comprobante no corresponde, rechazá el pago."));
                    return;
                }

                // Aprobar solo vale hasta el fin de la reserva; despues solo queda rechazar.
                if (AsUtc(reservation.EndsAtUtc) <= now)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<MarketplaceReviewItemDto>.Fail(MarketplaceError.Conflict(
                        "La reserva ya terminó, por eso no se puede aprobar. Podés rechazar el pago."));
                    return;
                }

                MarketplaceStateMachine.EnsureTransition(payment.Status, MarketplacePaymentStatus.Approved);
                MarketplaceStateMachine.EnsureTransition(reservation.Status, MarketplaceReservationStatus.Confirmed);
                MarketplaceStateMachine.EnsureTransition(reservation.CreditStatus, MarketplaceCreditStatus.Pending);

                payment.Status = MarketplacePaymentStatus.Approved;
                payment.ReviewedAmount = request.ReviewedAmount;
                payment.ReviewedByUserId = tenant.UserId;
                payment.ReviewedAtUtc = now;
                reservation.Status = MarketplaceReservationStatus.Confirmed;
                // La ganancia del propietario queda pendiente de acreditar (fin de la reserva + 24 h sin reclamos).
                reservation.CreditStatus = MarketplaceCreditStatus.Pending;

                // Ingreso en la cuenta aparte, una sola vez por reserva (indice unico): aprobar dos veces no duplica el asiento.
                var movement = new MarketplaceAccountMovement
                {
                    CompanyId = reservation.CompanyId,
                    BuildingId = reservation.BuildingId,
                    Kind = MarketplaceAccountMovementKind.PaymentIn,
                    Amount = payment.ExpectedAmount,
                    ReservationId = reservation.Id,
                    Concept = $"Pago de reserva {reservation.Reference} ({reservation.Listing?.Title})",
                    CreatedByUserId = null,
                    OccurredAtUtc = now
                };
                db.MarketplaceAccountMovements.Add(movement);

                audit.Record(payment.CompanyId, payment.BuildingId, nameof(MarketplacePayment), payment.Id,
                    MarketplaceEventActions.PaymentApproved, MarketplacePaymentStatus.Submitted.ToString(), payment.Status.ToString(),
                    new { reservation.Reference, payment.ExpectedAmount, payment.ReviewedAmount });
                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                    MarketplaceEventActions.ReservationConfirmed, MarketplaceReservationStatus.InReview.ToString(), reservation.Status.ToString(),
                    new { reservation.Reference, reservation.BaseAmount, reservation.CommissionAmount, reservation.TotalAmount, reservation.OwnerNetAmount });
                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceAccountMovement), movement.Id,
                    MarketplaceEventActions.AccountMovementRecorded, null, movement.Kind.ToString(),
                    new { movement.Amount, reservation.Reference }, automatic: true);

                var title = reservation.Listing?.Title ?? "el espacio";
                var range = FormatRange(reservation.StartsAtUtc, reservation.EndsAtUtc);
                AddNotice(reservation.CompanyId, reservation.BuyerUserId, NotificationType.MarketplaceReservationConfirmed,
                    "Reserva confirmada", $"Tu reserva de {title} {range} quedó confirmada.",
                    nameof(MarketplaceReservation), reservation.Id, pushes);
                AddNotice(reservation.CompanyId, reservation.OwnerId, NotificationType.MarketplaceNewReservation,
                    "Reservaron tu espacio", $"Reservaron {title} {range}. Se te acredita en tu saldo a favor cuando termine la reserva.",
                    nameof(MarketplaceReservation), reservation.Id, pushes);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException)
        {
            // Dos aprobaciones a la vez: la base dejo pasar solo una (version de fila e indices unicos).
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceReviewItemDto>.Fail(MarketplaceError.Conflict("Este pago ya fue revisado."));
        }

        if (failure is not null)
        {
            return failure;
        }

        await DispatchPushesAsync(pushes);
        return MarketplaceResult<MarketplaceReviewItemDto>.Success(await LoadReviewItemAsync(paymentId, ct));
    }

    public async Task<MarketplaceResult<MarketplaceReviewItemDto>> RejectAsync(Guid paymentId, MarketplaceRejectRequest request, CancellationToken ct)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length == 0)
        {
            return MarketplaceResult<MarketplaceReviewItemDto>.Fail(MarketplaceError.BadRequest("Indicá el motivo del rechazo."));
        }

        if (reason.Length > ReasonMaxLength)
        {
            return MarketplaceResult<MarketplaceReviewItemDto>.Fail(
                MarketplaceError.BadRequest($"El motivo no puede superar los {ReasonMaxLength} caracteres."));
        }

        var gate = await RequireStaffForPaymentAsync(paymentId, ct);
        if (!gate.Ok)
        {
            return MarketplaceResult<MarketplaceReviewItemDto>.Fail(gate.Error!);
        }

        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<MarketplaceReviewItemDto>? failure = null;

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

                var payment = await db.MarketplacePayments.FirstAsync(x => x.Id == paymentId, ct);
                var reservation = await db.MarketplaceReservations
                    .Include(x => x.Listing).Include(x => x.Slots)
                    .FirstAsync(x => x.Id == payment.ReservationId, ct);

                if (payment.Status != MarketplacePaymentStatus.Submitted || reservation.Status != MarketplaceReservationStatus.InReview)
                {
                    await transaction.RollbackAsync(ct);
                    failure = MarketplaceResult<MarketplaceReviewItemDto>.Fail(MarketplaceError.Conflict("Este pago ya fue revisado."));
                    return;
                }

                MarketplaceStateMachine.EnsureTransition(payment.Status, MarketplacePaymentStatus.Rejected);
                MarketplaceStateMachine.EnsureTransition(reservation.Status, MarketplaceReservationStatus.Rejected);

                payment.Status = MarketplacePaymentStatus.Rejected;
                payment.RejectionReason = reason;
                payment.ReviewedByUserId = tenant.UserId;
                payment.ReviewedAtUtc = now;

                // Rechazar cierra la reserva y libera el horario: el comprador tiene que hacer otra.
                reservation.Status = MarketplaceReservationStatus.Rejected;
                reservation.CancelReason = reason;
                reservation.CancelledAtUtc = now;
                reservation.CancelledByUserId = tenant.UserId;
                reservation.CancelledBy = MarketplaceCancellationActor.Staff;
                foreach (var slot in reservation.Slots.Where(x => !x.IsDeleted))
                {
                    slot.IsDeleted = true;
                }

                audit.Record(payment.CompanyId, payment.BuildingId, nameof(MarketplacePayment), payment.Id,
                    MarketplaceEventActions.PaymentRejected, MarketplacePaymentStatus.Submitted.ToString(), payment.Status.ToString(),
                    new { reservation.Reference, payment.ExpectedAmount, Reason = reason });
                audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                    MarketplaceEventActions.ReservationRejected, MarketplaceReservationStatus.InReview.ToString(), reservation.Status.ToString(),
                    new { reservation.Reference, Reason = reason });

                AddNotice(reservation.CompanyId, reservation.BuyerUserId, NotificationType.MarketplaceReservationRejected,
                    "Pago rechazado",
                    $"No se pudo confirmar tu reserva de {reservation.Listing?.Title ?? "el espacio"}. Motivo: {reason}",
                    nameof(MarketplaceReservation), reservation.Id, pushes);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceReviewItemDto>.Fail(MarketplaceError.Conflict("Este pago ya fue revisado."));
        }

        if (failure is not null)
        {
            return failure;
        }

        await DispatchPushesAsync(pushes);
        return MarketplaceResult<MarketplaceReviewItemDto>.Success(await LoadReviewItemAsync(paymentId, ct));
    }

    // ── Alertas al revisor ───────────────────────────────────────────────────

    /// <summary>
    /// Insiste con los pagos que siguen sin revisar: a los 15 minutos de subido y luego cada hora. Deja de insistir cuando se
    /// resuelve, o pasado un dia del fin de la reserva. Lo corre el proceso de fondo.
    /// </summary>
    public async Task<int> SendReviewAlertsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        db.ChangeTracker.Clear();

        var candidates = await db.MarketplacePayments
            .Include(x => x.Reservation).ThenInclude(r => r!.Listing)
            .Where(x => !x.IsDeleted && x.Status == MarketplacePaymentStatus.Submitted
                        && x.Reservation != null && x.Reservation.Status == MarketplaceReservationStatus.InReview)
            .ToListAsync(ct);

        var pushes = new List<MarketplacePushItem>();
        var sent = 0;
        foreach (var payment in candidates)
        {
            var reservation = payment.Reservation!;
            if (AsUtc(reservation.EndsAtUtc) + StopRemindingAfterEnd < now || !IsReminderDue(payment, now))
            {
                continue;
            }

            await AddReviewAlertAsync(payment, reservation, isReminder: true, pushes, ct);
            payment.AlertCount = Math.Max(payment.AlertCount, 1) + 1;
            payment.LastAlertAtUtc = now;
            sent++;
        }

        if (sent > 0)
        {
            await db.SaveChangesAsync(ct);
            await DispatchPushesAsync(pushes);
        }

        return sent;
    }

    // Reglas de cuando toca la siguiente alerta (separadas para poder probarlas).
    internal static bool IsReminderDue(MarketplacePayment payment, DateTime now) => payment.AlertCount switch
    {
        <= 0 => true,
        1 => AsUtc(payment.SubmittedAtUtc) + FirstReminderAfter <= now,
        _ => payment.LastAlertAtUtc is null || AsUtc(payment.LastAlertAtUtc.Value) + NextReminderEvery <= now
    };

    // Avisa a quienes revisan (notificacion en la app + push). No lleva la cuenta: la lleva quien llama.
    private async Task AddReviewAlertAsync(
        MarketplacePayment payment, MarketplaceReservation reservation, bool isReminder,
        List<MarketplacePushItem> pushes, CancellationToken ct)
    {
        var title = reservation.Listing?.Title ?? "una reserva";
        var heading = isReminder ? "Recordatorio: pago de reserva por revisar" : "Pago de reserva por revisar";
        var body = $"{title} · Gs. {payment.ExpectedAmount:N0} · {reservation.Reference}. Confirmalo o rechazalo desde Marketplace.";

        foreach (var reviewerId in await ReviewerIdsAsync(reservation.CompanyId, reservation.BuildingId, ct))
        {
            AddNotice(reservation.CompanyId, reviewerId, NotificationType.MarketplacePaymentPending, heading, body,
                nameof(MarketplacePayment), payment.Id, pushes);
        }
    }

    // Quienes revisan pagos: el Administrador de empresa (de la empresa y condominio del edificio) y los Encargados y
    // Operadores con acceso al edificio. Es la misma regla que ya usa el pago de expensas.
    private async Task<List<Guid>> ReviewerIdsAsync(Guid companyId, Guid buildingId, CancellationToken ct)
    {
        var condominiumId = await db.Buildings.AsNoTracking()
            .Where(x => x.Id == buildingId).Select(x => x.CondominiumId).FirstOrDefaultAsync(ct);

        return await db.ApplicationUsers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.CompanyId == companyId
                        && ((x.Role == UserRole.CompanyAdmin && (x.CondominiumId == null || x.CondominiumId == condominiumId))
                            || ((x.Role == UserRole.BuildingManager || x.Role == UserRole.CompanyOperator)
                                && x.BuildingAccesses.Any(a => !a.IsDeleted && a.IsActive && a.BuildingId == buildingId))))
            .Select(x => x.Id)
            .ToListAsync(ct);
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    // Valida que quien revisa sea personal con el edificio del pago en su alcance (no revela pagos de otros edificios).
    private async Task<MarketplaceResult<Guid>> RequireStaffForPaymentAsync(Guid paymentId, CancellationToken ct)
    {
        var buildingId = await db.MarketplacePayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == paymentId).Select(x => (Guid?)x.BuildingId).FirstOrDefaultAsync(ct);
        if (buildingId is null)
        {
            return MarketplaceResult<Guid>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(buildingId.Value, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<Guid>.Fail(MarketplaceError.FromAccess(access));
        }

        return access.Context!.IsStaff
            ? MarketplaceResult<Guid>.Success(buildingId.Value)
            : MarketplaceResult<Guid>.Fail(MarketplaceError.NotFound());
    }

    private void AddNotice(
        Guid companyId, Guid recipientId, NotificationType type, string title, string body,
        string entityType, Guid entityId, List<MarketplacePushItem> pushes)
    {
        db.Notifications.Add(new Notification
        {
            CompanyId = companyId,
            RecipientId = recipientId,
            Type = type,
            Title = title,
            Body = body,
            EntityType = entityType,
            EntityId = entityId
        });
        pushes.Add(new MarketplacePushItem(recipientId, title, body, entityId, entityType));
    }

    // El push se manda despues de guardar. Si falla, la operacion no se rompe (el aviso en la app ya quedo guardado).
    private async Task DispatchPushesAsync(IReadOnlyList<MarketplacePushItem> pushes)
    {
        foreach (var item in pushes)
        {
            await push.NotifyUserAsync(item.RecipientId, item.Title, item.Body, item.EntityType, item.EntityId, CancellationToken.None);
        }
    }

    private static string? ValidateComprobanteUrl(string? raw, out string url)
    {
        url = (raw ?? string.Empty).Trim();
        if (url.Length == 0)
        {
            return "Adjuntá el comprobante de la transferencia.";
        }

        // Solo se aceptan archivos que subio la propia plataforma (/api/uploads devuelve /uploads/...).
        if (url.Length > ComprobanteMaxLength
            || !url.StartsWith("/uploads/", StringComparison.Ordinal)
            || url.Contains("..", StringComparison.Ordinal)
            || url.Contains('\\')
            || url.Contains("//", StringComparison.Ordinal))
        {
            return "El comprobante no es válido. Subilo de nuevo.";
        }

        return null;
    }

    private static bool IsPastDue(DateTime? expiresAtUtc) => expiresAtUtc.HasValue && AsUtc(expiresAtUtc.Value) <= DateTime.UtcNow;

    private static string FormatRange(DateTime startsUtc, DateTime endsUtc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");
        var s = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(startsUtc), tz);
        var e = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(endsUtc), tz);
        return s.Date == e.Date
            ? $"el {s:dd/MM/yyyy} de {s:HH:mm} a {e:HH:mm} hs"
            : $"del {s:dd/MM/yyyy HH:mm} al {e:dd/MM/yyyy HH:mm} hs";
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private async Task<MarketplaceReservationDto> LoadReservationDtoAsync(Guid id, CancellationToken ct) =>
        (await MarketplaceReservationViews.LoadBuyerViewAsync(db, db.MarketplaceReservations.AsNoTracking().Where(r => r.Id == id), ct)).Single();

    private async Task<MarketplaceReviewItemDto> LoadReviewItemAsync(Guid paymentId, CancellationToken ct)
    {
        var buildingId = await db.MarketplacePayments.AsNoTracking().Where(x => x.Id == paymentId).Select(x => x.BuildingId).FirstAsync(ct);
        return (await ToReviewItemsAsync(db.MarketplacePayments.AsNoTracking().Where(x => x.Id == paymentId), buildingId, ct)).Single();
    }

    private async Task<List<MarketplaceReviewItemDto>> ToReviewItemsAsync(
        IQueryable<MarketplacePayment> query, Guid buildingId, CancellationToken ct)
    {
        var rows = await query
            .OrderBy(x => x.SubmittedAtUtc)
            .Select(x => new
            {
                PaymentId = x.Id,
                x.ReservationId,
                x.BuildingId,
                x.BuyerUserId,
                Reference = x.Reservation!.Reference,
                Title = x.Reservation.Listing != null ? x.Reservation.Listing.Title : string.Empty,
                UnitCode = x.Reservation.Unit != null ? x.Reservation.Unit.Code : string.Empty,
                OwnerName = x.Reservation.Owner != null ? x.Reservation.Owner.FullName : string.Empty,
                BuyerName = x.Buyer != null ? x.Buyer.FullName : string.Empty,
                x.Reservation.StartsAtUtc,
                x.Reservation.EndsAtUtc,
                x.Reservation.Hours,
                x.Reservation.BaseAmount,
                x.Reservation.CommissionAmount,
                x.ExpectedAmount,
                x.ComprobanteUrl,
                x.SubmittedAtUtc,
                x.Status,
                x.RejectionReason
            })
            .ToListAsync(ct);

        var buyerIds = rows.Select(x => x.BuyerUserId).Distinct().ToList();
        var unitsByBuyer = await BuyerUnitsAsync(buyerIds, buildingId, ct);
        var overdueUnitIds = await overdue.GetOverdueUnitIdsByBuildingAsync([buildingId], ct);
        var now = DateTime.UtcNow;

        return rows.Select(x =>
        {
            unitsByBuyer.TryGetValue(x.BuyerUserId, out var units);
            units ??= [];
            return new MarketplaceReviewItemDto
            {
                PaymentId = x.PaymentId,
                ReservationId = x.ReservationId,
                BuildingId = x.BuildingId,
                Reference = x.Reference,
                Title = x.Title,
                UnitCode = x.UnitCode,
                OwnerName = x.OwnerName,
                BuyerName = x.BuyerName,
                BuyerUnits = string.Join(", ", units.Select(u => u.Code).Distinct().OrderBy(c => c)),
                BuyerUnitOverdue = units.Any(u => overdueUnitIds.Contains(u.UnitId)),
                StartsAtUtc = AsUtc(x.StartsAtUtc),
                EndsAtUtc = AsUtc(x.EndsAtUtc),
                Hours = x.Hours,
                BaseAmount = x.BaseAmount,
                CommissionAmount = x.CommissionAmount,
                ExpectedAmount = x.ExpectedAmount,
                ComprobanteUrl = x.ComprobanteUrl,
                SubmittedAtUtc = AsUtc(x.SubmittedAtUtc),
                Status = x.Status.ToString(),
                RejectionReason = x.RejectionReason,
                ReservationEnded = AsUtc(x.EndsAtUtc) <= now
            };
        }).ToList();
    }

    // Unidades de cada comprador en el edificio (como propietario o residente vigente).
    private async Task<Dictionary<Guid, List<(Guid UnitId, string Code)>>> BuyerUnitsAsync(
        IReadOnlyCollection<Guid> userIds, Guid buildingId, CancellationToken ct)
    {
        var result = userIds.ToDictionary(x => x, _ => new List<(Guid, string)>());
        if (userIds.Count == 0)
        {
            return result;
        }

        var owned = await db.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && userIds.Contains(x.OwnerId) && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId)
            .Select(x => new { UserId = x.OwnerId, x.UnitId, x.Unit!.Code })
            .ToListAsync(ct);
        foreach (var row in owned)
        {
            result[row.UserId].Add((row.UnitId, row.Code));
        }

        var resided = await db.UnitResidents.AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                        && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId != null
                        && userIds.Contains(x.Resident.ApplicationUserId.Value)
                        && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId)
            .Select(x => new { UserId = x.Resident!.ApplicationUserId!.Value, x.UnitId, x.Unit!.Code })
            .ToListAsync(ct);
        foreach (var row in resided)
        {
            result[row.UserId].Add((row.UnitId, row.Code));
        }

        return result;
    }
}
