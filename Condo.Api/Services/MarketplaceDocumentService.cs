using System.Text.Json;
using Condo.Api.Documents;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Condo.Api.Services;

public sealed record MarketplaceDocumentFile(byte[] Bytes, string FileName);

/// <summary>
/// Documentos del marketplace (fase 8): el comprobante interno de reserva en PDF (no fiscal, armado al pedirlo desde los importes
/// congelados), la nota de cambio de propietario principal y el historial economico de una operacion reconstruido desde los eventos
/// de auditoria. Cada parte ve solo lo que le corresponde; el historial y las notas son del personal del edificio.
/// </summary>
public class MarketplaceDocumentService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    MarketplaceHandoverService handovers,
    IWebHostEnvironment env)
{
    // ── Comprobante interno de reserva (PDF) ─────────────────────────────────

    /// <summary>
    /// Comprobante de una reserva con pago confirmado. Lo pueden pedir el comprador (ve precio, comision y total), el propietario de
    /// la reserva (ve quien reservo y lo que recibe) y el personal del edificio (ve todo, con el comprobante de transferencia).
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceDocumentFile>> GetReceiptAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await db.MarketplaceReservations.AsNoTracking()
            .Include(x => x.Listing).Include(x => x.Unit).Include(x => x.Building)
            .Include(x => x.Buyer).Include(x => x.Owner)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == reservationId, ct);
        if (reservation is null)
        {
            return MarketplaceResult<MarketplaceDocumentFile>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(reservation.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceDocumentFile>.Fail(MarketplaceError.FromAccess(access));
        }

        MarketplaceReceiptAudience audience;
        if (access.Context!.IsStaff)
        {
            audience = MarketplaceReceiptAudience.Staff;
        }
        else if (reservation.BuyerUserId == tenant.UserId)
        {
            audience = MarketplaceReceiptAudience.Buyer;
        }
        else if (reservation.OwnerId == tenant.UserId)
        {
            audience = MarketplaceReceiptAudience.Owner;
        }
        else
        {
            return MarketplaceResult<MarketplaceDocumentFile>.Fail(MarketplaceError.NotFound());
        }

        var payment = await db.MarketplacePayments.AsNoTracking()
            .Include(x => x.ReviewedByUser)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ReservationId == reservationId && x.Status == MarketplacePaymentStatus.Approved, ct);
        if (payment is null)
        {
            return MarketplaceResult<MarketplaceDocumentFile>.Fail(
                MarketplaceError.Conflict("Todavía no hay un pago confirmado para esta reserva: el comprobante se genera cuando se confirma el pago."));
        }

        var refund = await db.MarketplaceRefunds.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ReservationId == reservationId, ct);
        var buyerUnits = (await MarketplaceReservationViews.BuyerUnitsAsync(db, [reservation.BuyerUserId], [reservation.BuildingId], ct))
            .GetValueOrDefault((reservation.BuyerUserId, reservation.BuildingId)) ?? [];

        var now = DateTime.UtcNow;
        var data = new MarketplaceReceiptData(
            Audience: audience,
            Reference: reservation.Reference,
            Status: reservation.Status.ToString(),
            BuildingName: reservation.Building?.Name ?? string.Empty,
            UnitCode: reservation.Unit?.Code ?? string.Empty,
            ListingTitle: reservation.Listing?.Title ?? string.Empty,
            OwnerName: reservation.Owner?.FullName ?? string.Empty,
            BuyerName: reservation.Buyer?.FullName ?? string.Empty,
            BuyerUnits: string.Join(", ", buyerUnits.Distinct().OrderBy(x => x)),
            StartsAtUtc: reservation.StartsAtUtc,
            EndsAtUtc: reservation.EndsAtUtc,
            Hours: reservation.Hours,
            HourlyPrice: reservation.HourlyPrice,
            BaseAmount: reservation.BaseAmount,
            CommissionPercent: reservation.CommissionPercent,
            CommissionAmount: reservation.CommissionAmount,
            TotalAmount: reservation.TotalAmount,
            OwnerNetAmount: reservation.OwnerNetAmount,
            CreatedAtUtc: reservation.CreatedAtUtc,
            PaymentSubmittedAtUtc: payment.SubmittedAtUtc,
            PaymentApprovedAtUtc: payment.ReviewedAtUtc,
            ApprovedByName: payment.ReviewedByUser?.FullName,
            ComprobanteUrl: audience == MarketplaceReceiptAudience.Staff ? payment.ComprobanteUrl : null,
            ComprobanteImage: audience == MarketplaceReceiptAudience.Staff ? TemplateImageReader.Read(env.WebRootPath ?? string.Empty, payment.ComprobanteUrl) : null,
            CreditStatus: reservation.CreditStatus.ToString(),
            CreditedAtUtc: reservation.CreditedAtUtc,
            CancelledBy: reservation.CancelledBy?.ToString(),
            CancelledAtUtc: reservation.CancelledAtUtc,
            CancelReason: reservation.CancelReason,
            RefundAmount: refund?.Amount,
            RefundStatus: refund?.Status.ToString(),
            RefundReturnedAtUtc: refund?.ReturnedAtUtc,
            GeneratedAtUtc: now);

        var bytes = new MarketplaceReceiptPdfDocument(data).GeneratePdf();

        // Queda registrado quien lo pidio y para que parte: trazabilidad del documento.
        audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
            MarketplaceEventActions.ReceiptGenerated, null, null, new { reservation.Reference, Audience = audience.ToString() });
        await db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceDocumentFile>.Success(
            new MarketplaceDocumentFile(bytes, $"comprobante-reserva-{reservation.Reference}.pdf"));
    }

    // ── Nota de cambio de propietario principal (PDF) ────────────────────────

    /// <summary>Descarga la nota en PDF (con la situacion actual de cada operacion). Abrirla por primera vez la marca como leida.</summary>
    public async Task<MarketplaceResult<MarketplaceDocumentFile>> GetHandoverPdfAsync(Guid noteId, CancellationToken ct)
    {
        var note = await handovers.GetNoteAsync(noteId, ct);
        if (!note.Ok)
        {
            return MarketplaceResult<MarketplaceDocumentFile>.Fail(note.Error!);
        }

        var bytes = new MarketplaceHandoverPdfDocument(note.Value!, DateTime.UtcNow).GeneratePdf();
        return MarketplaceResult<MarketplaceDocumentFile>.Success(
            new MarketplaceDocumentFile(bytes, $"cambio-propietario-unidad-{note.Value!.UnitCode}.pdf"));
    }

    // ── Historial economico de una operacion ─────────────────────────────────

    /// <summary>
    /// La operacion de punta a punta (publicacion, reserva, importes, pago, cancelacion, reembolso, reclamo, acreditacion), armada
    /// con los eventos de auditoria. Solo el personal del edificio.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceOperationHistoryDto>> GetHistoryAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await db.MarketplaceReservations.AsNoTracking()
            .Include(x => x.Listing).Include(x => x.Unit).Include(x => x.Buyer).Include(x => x.Owner)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == reservationId, ct);
        if (reservation is null)
        {
            return MarketplaceResult<MarketplaceOperationHistoryDto>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(reservation.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceOperationHistoryDto>.Fail(MarketplaceError.FromAccess(access));
        }

        if (!access.Context!.IsStaff)
        {
            return MarketplaceResult<MarketplaceOperationHistoryDto>.Fail(MarketplaceError.NotFound());
        }

        // Todo lo que cuelga de la operacion: la reserva, sus pagos, su reembolso, sus reclamos, sus asientos de cuenta y su deuda.
        var entityIds = new HashSet<Guid> { reservation.Id };
        entityIds.UnionWith(await db.MarketplacePayments.AsNoTracking().Where(x => x.ReservationId == reservationId).Select(x => x.Id).ToListAsync(ct));
        entityIds.UnionWith(await db.MarketplaceRefunds.AsNoTracking().Where(x => x.ReservationId == reservationId).Select(x => x.Id).ToListAsync(ct));
        entityIds.UnionWith(await db.MarketplaceClaims.AsNoTracking().Where(x => x.ReservationId == reservationId).Select(x => x.Id).ToListAsync(ct));
        entityIds.UnionWith(await db.MarketplaceOwnerDebts.AsNoTracking().Where(x => x.ReservationId == reservationId).Select(x => x.Id).ToListAsync(ct));
        entityIds.UnionWith(await db.MarketplaceAccountMovements.AsNoTracking().Where(x => x.ReservationId == reservationId).Select(x => x.Id).ToListAsync(ct));

        var ids = entityIds.ToList();
        var events = await db.MarketplaceEvents.AsNoTracking()
            .Where(x => x.BuildingId == reservation.BuildingId
                        && (ids.Contains(x.EntityId)
                            || (x.EntityId == reservation.ListingId && x.Action == MarketplaceEventActions.ListingCreated)))
            .OrderBy(x => x.TimestampUtc)
            .ToListAsync(ct);

        var userIds = events.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ApplicationUsers.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        return MarketplaceResult<MarketplaceOperationHistoryDto>.Success(new MarketplaceOperationHistoryDto
        {
            ReservationId = reservation.Id,
            BuildingId = reservation.BuildingId,
            Reference = reservation.Reference,
            Title = reservation.Listing?.Title ?? string.Empty,
            UnitCode = reservation.Unit?.Code ?? string.Empty,
            OwnerName = reservation.Owner?.FullName ?? string.Empty,
            BuyerName = reservation.Buyer?.FullName ?? string.Empty,
            Status = reservation.Status.ToString(),
            CreditStatus = reservation.CreditStatus.ToString(),
            StartsAtUtc = MarketplaceReservationViews.AsUtc(reservation.StartsAtUtc),
            EndsAtUtc = MarketplaceReservationViews.AsUtc(reservation.EndsAtUtc),
            Hours = reservation.Hours,
            HourlyPrice = reservation.HourlyPrice,
            BaseAmount = reservation.BaseAmount,
            CommissionPercent = reservation.CommissionPercent,
            CommissionAmount = reservation.CommissionAmount,
            TotalAmount = reservation.TotalAmount,
            OwnerNetAmount = reservation.OwnerNetAmount,
            Items = events.Select(e => ToItem(e, e.UserId.HasValue ? names.GetValueOrDefault(e.UserId.Value) : null)).ToList()
        });
    }

    // ── Traduccion de los eventos a lenguaje humano ──────────────────────────

    public static MarketplaceHistoryItemDto ToItem(MarketplaceEvent e, string? actorName)
    {
        JsonElement? data = null;
        if (!string.IsNullOrWhiteSpace(e.DataJson))
        {
            try { data = JsonDocument.Parse(e.DataJson).RootElement.Clone(); }
            catch (JsonException) { /* un evento con datos ilegibles igual se muestra, sin detalle */ }
        }

        var (title, detail, amount) = Describe(e.Action, data);
        return new MarketplaceHistoryItemDto
        {
            TimestampUtc = MarketplaceReservationViews.AsUtc(e.TimestampUtc),
            Action = e.Action,
            Title = title,
            Detail = detail,
            ActorName = e.UserId.HasValue ? actorName : null,
            Amount = amount,
            FromStatus = e.FromStatus,
            ToStatus = e.ToStatus
        };
    }

    private static (string Title, string? Detail, decimal? Amount) Describe(string action, JsonElement? d)
    {
        string? Text(string name) => d is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;
        decimal? Num(string name) => d is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetDecimal(out var v) ? v : null;
        string Gs(decimal? v) => MarketplaceNotices.Gs(v ?? 0m);
        static string? Join(params string?[] parts) => parts.Any(x => !string.IsNullOrWhiteSpace(x))
            ? string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x))) : null;

        return action switch
        {
            MarketplaceEventActions.ListingCreated => ("Publicación creada", null, null),
            MarketplaceEventActions.ReservationCreated => ("Reserva creada",
                $"Precio {Gs(Num("BaseAmount"))} + comisión {Gs(Num("CommissionAmount"))} ({Num("CommissionPercent"):0.##} %) = total {Gs(Num("TotalAmount"))}. " +
                $"Ganancia del propietario {Gs(Num("OwnerNetAmount"))}.", Num("TotalAmount")),
            MarketplaceEventActions.PaymentSubmitted => ("Comprobante de pago enviado", null, Num("ExpectedAmount")),
            MarketplaceEventActions.PaymentApproved => ("Pago confirmado", $"Monto verificado: {Gs(Num("ReviewedAmount"))}.", Num("ExpectedAmount")),
            MarketplaceEventActions.PaymentRejected => ("Pago rechazado", Text("Reason"), null),
            MarketplaceEventActions.ReservationConfirmed => ("Reserva confirmada", null, null),
            MarketplaceEventActions.ReservationRejected => ("Reserva rechazada", Text("Reason"), null),
            MarketplaceEventActions.ReservationExpired => ("Reserva vencida por falta de pago", null, null),
            MarketplaceEventActions.ReservationCancelled => ("Reserva cancelada",
                Join(Text("By") switch { "Buyer" => "Por el comprador", "Owner" => "Por el propietario", _ => null }, Text("Reason")), Num("RefundAmount")),
            MarketplaceEventActions.ReservationCompleted => ("Reserva finalizada", null, null),
            MarketplaceEventActions.StartNoticeSent => ("Aviso de inicio enviado al comprador", null, null),
            MarketplaceEventActions.StartResponded => (
                Text("Response") == "Attending" ? "El comprador respondió: «Sí, voy»" : "El comprador respondió: «No la voy a usar»", Text("Reason"), null),
            MarketplaceEventActions.ClaimOpened => ("Reclamo abierto",
                Join(Text("OpenedBy") == "Buyer" ? "Por el comprador" : "Por el propietario", Text("Reason")), null),
            MarketplaceEventActions.ClaimResolved => ("Reclamo resuelto",
                Join(Text("Outcome") == "InFavorOfBuyer" ? "A favor del comprador" : "A favor del propietario", Text("Note")), null),
            MarketplaceEventActions.CreditHeld => ("Acreditación retenida por un reclamo", null, null),
            MarketplaceEventActions.CreditReleased => ("Acreditación liberada", null, null),
            MarketplaceEventActions.CreditApplied => ("Saldo acreditado al propietario",
                Num("DebtDeducted") is > 0 ? $"Se descontaron {Gs(Num("DebtDeducted"))} de deuda por gestión." : null, Num("Amount")),
            MarketplaceEventActions.CreditReversed => ("Acreditación revertida", Text("Reason"), Num("Amount")),
            MarketplaceEventActions.AccountMovementRecorded => ("Movimiento en la cuenta del Marketplace", null, Num("Amount")),
            MarketplaceEventActions.RefundCreated => ("Reembolso pendiente al comprador", Text("Reason"), Num("Amount")),
            MarketplaceEventActions.RefundReturned => ("Reembolso devuelto al comprador", null, Num("Amount")),
            MarketplaceEventActions.RefundOverdueAlert => ("Alerta: pasaron 72 horas sin devolver el reembolso", null, Num("Amount")),
            MarketplaceEventActions.OwnerFeeCharged => ("Comisión asumida por el propietario",
                $"De su saldo a favor: {Gs(Num("FromBalance"))}. Como deuda por gestión: {Gs(Num("ToDebt"))}.", Num("Fee")),
            MarketplaceEventActions.OwnerDebtCreated => ("Deuda por gestión registrada", Text("Reason"), Num("Amount")),
            MarketplaceEventActions.OwnerDebtDeducted => ("Deuda por gestión descontada de una acreditación", null, Num("Deducted")),
            MarketplaceEventActions.ReceiptGenerated => ("Comprobante interno generado", Text("Audience") switch
            {
                "Staff" => "Versión del personal",
                "Buyer" => "Versión del comprador",
                "Owner" => "Versión del propietario",
                _ => null
            }, null),
            _ => (action, null, null)
        };
    }
}
