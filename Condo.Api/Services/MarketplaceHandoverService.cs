using System.Text;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Cambio de propietario principal con operaciones del marketplace abiertas. Cuando una unidad deja de tener a su propietario
/// principal, las publicaciones sin reservar se suspenden solas; las reservas ya hechas conservan al propietario con el que se
/// crearon (a el se le acredita y es quien las cancela o reclama). Para que nadie se pierda, se genera una nota interna que explica
/// que paso y cual era la situacion, y se avisa a quienes administran el edificio. La nota se abre y se descarga desde la web.
/// </summary>
public class MarketplaceHandoverService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    PushDispatcher push,
    MarketplaceListingService listings)
{
    private const int MaxReservations = 100;
    private const int MaxLinesInText = 15;
    private const int ContentMaxLength = 4000;
    private const int ListSize = 100;
    // Una nota que quedo esperando al nuevo principal se completa solo si el alta llega dentro de este plazo.
    private static readonly TimeSpan CompletionWindow = TimeSpan.FromDays(60);

    // ── Ganchos (los llama el alta y la baja de propietarios de una unidad) ──

    /// <summary>
    /// Un propietario principal dejo de serlo (se lo desvinculo de la unidad). Suspende las publicaciones que ya no corresponden y,
    /// si quedaron reservas abiertas a su nombre, crea la nota interna y avisa al personal. Devuelve la nota, o nulo si no hacia falta.
    /// </summary>
    public async Task<MarketplaceHandoverNote?> OnPrimaryRemovedAsync(
        Guid unitId, Guid removedOwnerId, Guid? currentPrimaryId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var unit = await db.Units.AsNoTracking().Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId, ct);
        if (unit is null)
        {
            return null;
        }

        // Las publicaciones de quien ya no es el principal se suspenden en el momento, no cuando alguien consulte.
        var suspended = await listings.SyncOwnershipAsync(unit.BuildingId, ct);

        if (currentPrimaryId == removedOwnerId)
        {
            return null;
        }

        db.ChangeTracker.Clear();
        var affected = await db.MarketplaceReservations.AsNoTracking()
            .Include(x => x.Listing)
            .Where(x => !x.IsDeleted && x.UnitId == unitId && x.OwnerId == removedOwnerId
                        && (x.Status == MarketplaceReservationStatus.InReview
                            || x.Status == MarketplaceReservationStatus.Confirmed
                            || (x.Status == MarketplaceReservationStatus.Completed
                                && (x.CreditStatus == MarketplaceCreditStatus.Pending || x.CreditStatus == MarketplaceCreditStatus.Held))
                            || db.MarketplaceRefunds.Any(r => !r.IsDeleted && r.ReservationId == x.Id && r.Status == MarketplaceRefundStatus.Pending)
                            || db.MarketplaceClaims.Any(c => !c.IsDeleted && c.ReservationId == x.Id && c.Status == MarketplaceClaimStatus.Open)))
            .OrderBy(x => x.StartsAtUtc)
            .Take(MaxReservations)
            .ToListAsync(ct);
        if (affected.Count == 0)
        {
            return null;
        }

        var names = await NamesAsync([removedOwnerId, .. currentPrimaryId.HasValue ? new[] { currentPrimaryId.Value } : []], ct);
        var previousName = names.GetValueOrDefault(removedOwnerId, "el propietario anterior");
        var newName = currentPrimaryId.HasValue ? names.GetValueOrDefault(currentPrimaryId.Value) : null;

        var note = new MarketplaceHandoverNote
        {
            CompanyId = unit.CompanyId,
            BuildingId = unit.BuildingId,
            UnitId = unit.Id,
            PreviousOwnerId = removedOwnerId,
            NewOwnerId = currentPrimaryId,
            Trigger = currentPrimaryId.HasValue ? MarketplaceHandoverTrigger.PrimaryReplaced : MarketplaceHandoverTrigger.PrimaryRemoved,
            ReservationIds = string.Join(',', affected.Select(x => x.Id)),
            ReservationCount = affected.Count,
            Content = ComposeText(unit, previousName, newName, suspended, affected)
        };
        db.MarketplaceHandoverNotes.Add(note);

        audit.Record(unit.CompanyId, unit.BuildingId, nameof(MarketplaceHandoverNote), note.Id,
            MarketplaceEventActions.HandoverCreated, null, note.Trigger.ToString(),
            new { UnitCode = unit.Code, PreviousOwnerId = removedOwnerId, NewOwnerId = currentPrimaryId, Reservations = affected.Count, SuspendedListings = suspended });

        var pushes = new List<MarketplacePushItem>();
        var heading = "Cambió el propietario principal de una unidad";
        var body = $"Unidad {unit.Code}: hay {affected.Count} {(affected.Count == 1 ? "operación abierta" : "operaciones abiertas")} del Marketplace a nombre de " +
                   $"{previousName}. Abrí la nota para ver qué pasó y qué hay que resolver.";
        foreach (var reviewerId in await MarketplaceNotices.ReviewerIdsAsync(db, unit.CompanyId, unit.BuildingId, ct))
        {
            MarketplaceNotices.Add(db, unit.CompanyId, reviewerId, NotificationType.MarketplaceHandoverNote,
                heading, body, nameof(MarketplaceHandoverNote), note.Id, pushes);
        }

        await db.SaveChangesAsync(ct);
        await MarketplaceNotices.DispatchAsync(push, pushes);
        return note;
    }

    /// <summary>
    /// Se asigno un propietario principal a la unidad. Si la ultima nota de la unidad quedo esperando al nuevo principal (primero se
    /// dio de baja al anterior), se completa sola con quien llego.
    /// </summary>
    public async Task<bool> OnPrimaryAssignedAsync(Guid unitId, Guid newPrimaryId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var since = DateTime.UtcNow - CompletionWindow;
        var note = await db.MarketplaceHandoverNotes
            .Where(x => !x.IsDeleted && x.UnitId == unitId && x.NewOwnerId == null && x.CreatedAtUtc >= since)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (note is null)
        {
            return false;
        }

        var name = (await NamesAsync([newPrimaryId], ct)).GetValueOrDefault(newPrimaryId, "un nuevo propietario");
        note.NewOwnerId = newPrimaryId;
        note.Trigger = MarketplaceHandoverTrigger.PrimaryReplaced;
        var addition = newPrimaryId == note.PreviousOwnerId
            ? $"\n\nActualización ({Today()}): {name} volvió a ser el propietario principal de la unidad."
            : $"\n\nActualización ({Today()}): se asignó a {name} como nuevo propietario principal de la unidad. " +
              "Las reservas de esta nota siguen a nombre del propietario anterior.";
        note.Content = Truncate(note.Content + addition);

        audit.Record(note.CompanyId, note.BuildingId, nameof(MarketplaceHandoverNote), note.Id,
            MarketplaceEventActions.HandoverCompleted, MarketplaceHandoverTrigger.PrimaryRemoved.ToString(), note.Trigger.ToString(),
            new { NewOwnerId = newPrimaryId });

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Consulta (personal del edificio) ─────────────────────────────────────

    /// <summary>Notas del edificio: las no leidas primero y, si se pide, tambien las ya leidas.</summary>
    public async Task<MarketplaceResult<List<MarketplaceHandoverNoteDto>>> GetNotesAsync(Guid buildingId, bool includeRead, CancellationToken ct)
    {
        var staff = await RequireStaffAsync(buildingId, ct);
        if (!staff.Ok)
        {
            return MarketplaceResult<List<MarketplaceHandoverNoteDto>>.Fail(staff.Error!);
        }

        db.ChangeTracker.Clear();
        var query = db.MarketplaceHandoverNotes.AsNoTracking().Where(x => !x.IsDeleted && x.BuildingId == buildingId);
        if (!includeRead)
        {
            query = query.Where(x => x.ReadAtUtc == null);
        }

        var notes = await query
            .OrderBy(x => x.ReadAtUtc != null).ThenByDescending(x => x.CreatedAtUtc)
            .Take(ListSize)
            .ToListAsync(ct);

        var dtos = new List<MarketplaceHandoverNoteDto>();
        foreach (var note in notes)
        {
            dtos.Add(await ToDtoAsync(note, withOperations: false, ct));
        }

        return MarketplaceResult<List<MarketplaceHandoverNoteDto>>.Success(dtos);
    }

    /// <summary>
    /// Abre una nota con la situacion ACTUAL de cada operacion. Abrirla por primera vez la marca como leida (queda quien y cuando).
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceHandoverNoteDto>> GetNoteAsync(Guid id, CancellationToken ct)
    {
        var buildingId = await db.MarketplaceHandoverNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id).Select(x => (Guid?)x.BuildingId).FirstOrDefaultAsync(ct);
        if (buildingId is null)
        {
            return MarketplaceResult<MarketplaceHandoverNoteDto>.Fail(MarketplaceError.NotFound());
        }

        var staff = await RequireStaffAsync(buildingId.Value, ct);
        if (!staff.Ok)
        {
            return MarketplaceResult<MarketplaceHandoverNoteDto>.Fail(staff.Error!);
        }

        db.ChangeTracker.Clear();
        var note = await db.MarketplaceHandoverNotes.FirstAsync(x => x.Id == id, ct);
        if (note.ReadAtUtc is null)
        {
            note.ReadAtUtc = DateTime.UtcNow;
            note.ReadByUserId = tenant.UserId;
            audit.Record(note.CompanyId, note.BuildingId, nameof(MarketplaceHandoverNote), note.Id,
                MarketplaceEventActions.HandoverRead, null, null, new { note.UnitId });
            await db.SaveChangesAsync(ct);
        }

        return MarketplaceResult<MarketplaceHandoverNoteDto>.Success(await ToDtoAsync(note, withOperations: true, ct));
    }

    // ── Texto ────────────────────────────────────────────────────────────────

    private static string ComposeText(
        Unit unit, string previousName, string? newName, int suspendedListings, List<MarketplaceReservation> affected)
    {
        var text = new StringBuilder();
        text.AppendLine($"El {Today()} {previousName} dejó de ser el propietario principal de la unidad {unit.Code} ({unit.Building?.Name}).");
        text.AppendLine(newName is null
            ? "Por ahora la unidad no tiene otro propietario principal: cuando se asigne uno, esta nota se actualiza sola."
            : $"El nuevo propietario principal es {newName}.");
        text.AppendLine();
        text.AppendLine("Qué pasó con el Marketplace:");
        text.AppendLine($"- Las publicaciones de {previousName} que no tenían reservas se suspendieron solas" +
                        (suspendedListings > 0 ? $" ({suspendedListings} en el edificio en este momento)." : "."));
        text.AppendLine($"- Las reservas ya hechas conservan a {previousName}: a él se le acredita la ganancia cuando corresponda " +
                        "y es quien puede cancelarlas o reportar un problema. La ganancia no pasa al nuevo propietario.");
        text.AppendLine($"- Si {previousName} ya no pertenece al edificio, no va a poder usar la app para esas reservas: " +
                        "cualquier reclamo o devolución lo resuelve la administración.");
        text.AppendLine();
        text.AppendLine($"Operaciones abiertas al momento del cambio ({affected.Count}):");
        foreach (var r in affected.Take(MaxLinesInText))
        {
            text.AppendLine($"- {r.Reference} · {r.Listing?.Title} · {MarketplaceNotices.FormatRange(r.StartsAtUtc, r.EndsAtUtc)} · " +
                            $"{StatusLabel(r)} · ganancia del propietario {MarketplaceNotices.Gs(r.OwnerNetAmount)}");
        }

        if (affected.Count > MaxLinesInText)
        {
            text.AppendLine($"- … y {affected.Count - MaxLinesInText} más (ver la lista completa en la nota).");
        }

        return Truncate(text.ToString().TrimEnd());
    }

    private static string StatusLabel(MarketplaceReservation r) => r.Status switch
    {
        MarketplaceReservationStatus.InReview => "pago en revisión",
        MarketplaceReservationStatus.Confirmed => "confirmada",
        MarketplaceReservationStatus.Completed when r.CreditStatus == MarketplaceCreditStatus.Held => "finalizada con reclamo (acreditación retenida)",
        MarketplaceReservationStatus.Completed => "finalizada, acreditación pendiente",
        _ => "con un reembolso pendiente"
    };

    private static string Today()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).ToString("dd/MM/yyyy");
    }

    private static string Truncate(string text) => text.Length <= ContentMaxLength ? text : text[..(ContentMaxLength - 1)] + "…";

    // ── Auxiliares ───────────────────────────────────────────────────────────

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

    private async Task<Dictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        await db.ApplicationUsers.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

    private async Task<MarketplaceHandoverNoteDto> ToDtoAsync(MarketplaceHandoverNote note, bool withOperations, CancellationToken ct)
    {
        var ids = new List<Guid> { note.PreviousOwnerId };
        if (note.NewOwnerId.HasValue) ids.Add(note.NewOwnerId.Value);
        if (note.ReadByUserId.HasValue) ids.Add(note.ReadByUserId.Value);
        var names = await NamesAsync(ids, ct);
        var unit = await db.Units.AsNoTracking().Where(x => x.Id == note.UnitId).Select(x => new { x.Code }).FirstAsync(ct);
        var building = await db.Buildings.AsNoTracking().Where(x => x.Id == note.BuildingId).Select(x => x.Name).FirstAsync(ct);

        var dto = new MarketplaceHandoverNoteDto
        {
            Id = note.Id,
            BuildingId = note.BuildingId,
            BuildingName = building,
            UnitId = note.UnitId,
            UnitCode = unit.Code,
            PreviousOwnerName = names.GetValueOrDefault(note.PreviousOwnerId, string.Empty),
            NewOwnerName = note.NewOwnerId.HasValue ? names.GetValueOrDefault(note.NewOwnerId.Value) : null,
            Trigger = note.Trigger.ToString(),
            Content = note.Content,
            ReservationCount = note.ReservationCount,
            CreatedAtUtc = MarketplaceReservationViews.AsUtc(note.CreatedAtUtc),
            ReadAtUtc = note.ReadAtUtc.HasValue ? MarketplaceReservationViews.AsUtc(note.ReadAtUtc.Value) : null,
            ReadByName = note.ReadByUserId.HasValue ? names.GetValueOrDefault(note.ReadByUserId.Value) : null
        };

        if (!withOperations)
        {
            return dto;
        }

        var reservationIds = note.ReservationIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
            .Where(x => x != Guid.Empty)
            .ToList();

        var rows = await db.MarketplaceReservations.AsNoTracking()
            .Where(x => reservationIds.Contains(x.Id))
            .OrderBy(x => x.StartsAtUtc)
            .Select(x => new
            {
                x.Id, x.Reference, x.Status, x.CreditStatus, x.StartsAtUtc, x.EndsAtUtc, x.OwnerNetAmount,
                Title = x.Listing != null ? x.Listing.Title : string.Empty
            })
            .ToListAsync(ct);
        var openClaims = (await db.MarketplaceClaims.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Status == MarketplaceClaimStatus.Open && reservationIds.Contains(x.ReservationId))
                .Select(x => x.ReservationId).ToListAsync(ct)).ToHashSet();
        var refunds = await db.MarketplaceRefunds.AsNoTracking()
            .Where(x => !x.IsDeleted && reservationIds.Contains(x.ReservationId))
            .ToDictionaryAsync(x => x.ReservationId, x => x.Status, ct);

        dto.Operations = rows.Select(x => new MarketplaceHandoverOperationDto
        {
            ReservationId = x.Id,
            Reference = x.Reference,
            Title = x.Title,
            Status = x.Status.ToString(),
            CreditStatus = x.CreditStatus.ToString(),
            StartsAtUtc = MarketplaceReservationViews.AsUtc(x.StartsAtUtc),
            EndsAtUtc = MarketplaceReservationViews.AsUtc(x.EndsAtUtc),
            OwnerNetAmount = x.OwnerNetAmount,
            HasOpenClaim = openClaims.Contains(x.Id),
            RefundStatus = refunds.TryGetValue(x.Id, out var status) ? status.ToString() : null
        }).ToList();
        return dto;
    }
}
