using System.Data;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Condo.Api.Services;

/// <summary>Aviso a un usuario (push) que se manda DESPUES de guardar, fuera de la transaccion.</summary>
public sealed record MarketplacePushItem(Guid RecipientId, string Title, string Body, Guid EntityId, string EntityType = "MarketplaceReservation");

/// <summary>
/// Explorar y reservar. La reserva es una operacion comercial: congela los importes al crearse y ocupa bloques de 30 minutos.
/// Que no haya doble reserva lo garantiza la base (indice unico por publicacion y bloque) mas una transaccion Serializable;
/// la disponibilidad, el precio, la comision y el plazo para pagar se calculan SIEMPRE en el servidor.
/// </summary>
public class MarketplaceReservationService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit,
    MarketplaceListingService listings,
    PushDispatcher push,
    IOptions<MarketplaceOptions> options)
{
    private const int ReasonMaxLength = 500;

    private static readonly MarketplaceReservationStatus[] LiveStatuses = MarketplaceStatusSets.LiveReservations;

    // ── Explorar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Publicaciones de OTROS vecinos del edificio con al menos una hora libre. Solo del edificio indicado y solo para
    /// quienes viven o son propietarios en el: nunca de otro edificio ni de otra empresa.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceExploreItemDto>>> ExploreAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<List<MarketplaceExploreItemDto>>.Fail(MarketplaceError.FromAccess(access));
        }

        if (!access.Context!.IsMember)
        {
            return MarketplaceResult<List<MarketplaceExploreItemDto>>.Fail(
                MarketplaceError.Forbidden("Solo los vecinos del edificio ven las publicaciones."));
        }

        await listings.SyncOwnershipAsync(buildingId, ct);

        var now = DateTime.UtcNow;
        var commission = await CommissionPercentAsync(buildingId, ct);

        var rows = await db.MarketplaceListings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId
                        && x.Status == MarketplaceListingStatus.Active
                        && x.WindowEndUtc > now
                        && x.OwnerId != tenant.UserId)
            .OrderBy(x => x.WindowStartUtc)
            .Select(x => new
            {
                x.Id,
                x.Title,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                x.WindowStartUtc,
                x.WindowEndUtc,
                x.HourlyPrice
            })
            .ToListAsync(ct);

        var occupiedByListing = await OccupiedSlotsAsync(rows.Select(x => x.Id).ToList(), now, ct);

        var result = new List<MarketplaceExploreItemDto>();
        foreach (var row in rows)
        {
            occupiedByListing.TryGetValue(row.Id, out var occupied);
            occupied ??= [];

            if (!HasFreeHour(row.WindowStartUtc, row.WindowEndUtc, occupied, now))
            {
                continue;
            }

            result.Add(new MarketplaceExploreItemDto
            {
                ListingId = row.Id,
                Title = row.Title,
                UnitCode = row.UnitCode,
                WindowStartUtc = AsUtc(row.WindowStartUtc),
                WindowEndUtc = AsUtc(row.WindowEndUtc),
                HourlyPrice = row.HourlyPrice,
                CommissionPercent = commission,
                Occupied = MergeIntervals(occupied)
            });
        }

        return MarketplaceResult<List<MarketplaceExploreItemDto>>.Success(result);
    }

    // ── Cotizar y reservar ───────────────────────────────────────────────────

    /// <summary>Calcula el desglose exacto de un horario sin reservar nada (el precio lo pone siempre el servidor).</summary>
    public async Task<MarketplaceResult<MarketplaceQuoteDto>> QuoteAsync(MarketplaceQuoteRequest request, CancellationToken ct)
    {
        var prepared = await PrepareAsync(request, ct);
        if (!prepared.Ok)
        {
            return MarketplaceResult<MarketplaceQuoteDto>.Fail(prepared.Error!);
        }

        var p = prepared.Value!;
        var now = DateTime.UtcNow;

        var occupied = (await OccupiedSlotsAsync([p.Listing.Id], now, ct)).GetValueOrDefault(p.Listing.Id) ?? [];
        var slots = MarketplaceWindowRules.Slots(p.Start, p.End);
        if (slots.Any(occupied.Contains))
        {
            return MarketplaceResult<MarketplaceQuoteDto>.Fail(MarketplaceError.Conflict(TakenMessage));
        }

        return MarketplaceResult<MarketplaceQuoteDto>.Success(ToQuoteDto(p));
    }

    public async Task<MarketplaceResult<MarketplaceReservationDto>> ReserveAsync(MarketplaceQuoteRequest request, CancellationToken ct)
    {
        var prepared = await PrepareAsync(request, ct);
        if (!prepared.Ok)
        {
            return MarketplaceResult<MarketplaceReservationDto>.Fail(prepared.Error!);
        }

        var p = prepared.Value!;
        var buyerId = tenant.UserId;
        var slotStarts = MarketplaceWindowRules.Slots(p.Start, p.End);
        var pushes = new List<MarketplacePushItem>();
        MarketplaceResult<Guid>? outcome = null;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                pushes.Clear();
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var now = DateTime.UtcNow;

                // Lo vencido (de esta publicacion o de este comprador) se libera primero: si el proceso de fondo todavia no
                // paso, igual no puede bloquear el horario ni el cupo de "una reserva esperando pago".
                pushes.AddRange(await ExpireStaleCoreAsync(now, p.Listing.Id, buyerId, ct));
                await db.SaveChangesAsync(ct);

                if (await db.MarketplaceReservations.AnyAsync(x => !x.IsDeleted && x.BuyerUserId == buyerId
                        && x.Status == MarketplaceReservationStatus.PendingPayment, ct))
                {
                    await transaction.RollbackAsync(ct);
                    outcome = MarketplaceResult<Guid>.Fail(MarketplaceError.Conflict(PendingMessage));
                    return;
                }

                if (await db.MarketplaceReservationSlots.AnyAsync(x => !x.IsDeleted && x.ListingId == p.Listing.Id
                        && slotStarts.Contains(x.SlotStartUtc), ct))
                {
                    await transaction.RollbackAsync(ct);
                    outcome = MarketplaceResult<Guid>.Fail(MarketplaceError.Conflict(TakenMessage));
                    return;
                }

                var reservation = new MarketplaceReservation
                {
                    CompanyId = p.CompanyId,
                    ListingId = p.Listing.Id,
                    BuildingId = p.Listing.BuildingId,
                    UnitId = p.Listing.UnitId,
                    BuyerUserId = buyerId,
                    // Congelado: a este propietario se le acredita, aunque despues cambie el principal.
                    OwnerId = p.Listing.OwnerId,
                    Reference = await NextReferenceAsync(p.CompanyId, ct),
                    StartsAtUtc = p.Start,
                    EndsAtUtc = p.End,
                    Hours = p.Quote.Hours,
                    HourlyPrice = p.Quote.HourlyPrice,
                    BaseAmount = p.Quote.BaseAmount,
                    CommissionPercent = p.Quote.CommissionPercent,
                    CommissionAmount = p.Quote.CommissionAmount,
                    TotalAmount = p.Quote.TotalAmount,
                    OwnerNetAmount = p.Quote.OwnerNetAmount,
                    Status = MarketplaceReservationStatus.PendingPayment,
                    ExpiresAtUtc = now.AddMinutes(options.Value.EffectivePaymentTimeoutMinutes),
                    CreditStatus = MarketplaceCreditStatus.None
                };
                db.MarketplaceReservations.Add(reservation);

                foreach (var slot in slotStarts)
                {
                    db.MarketplaceReservationSlots.Add(new MarketplaceReservationSlot
                    {
                        CompanyId = p.CompanyId,
                        ReservationId = reservation.Id,
                        ListingId = p.Listing.Id,
                        SlotStartUtc = slot
                    });
                }

                audit.Record(p.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                    MarketplaceEventActions.ReservationCreated, null, reservation.Status.ToString(),
                    new
                    {
                        reservation.Reference, reservation.ListingId, reservation.StartsAtUtc, reservation.EndsAtUtc,
                        reservation.Hours, reservation.HourlyPrice, reservation.BaseAmount, reservation.CommissionPercent,
                        reservation.CommissionAmount, reservation.TotalAmount, reservation.OwnerNetAmount,
                        reservation.ExpiresAtUtc
                    });

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                outcome = MarketplaceResult<Guid>.Success(reservation.Id);
            });
        }
        catch (DbUpdateException)
        {
            // Dos pedidos a la vez: la base rechazo al segundo (indice unico). El usuario ve un mensaje claro.
            db.ChangeTracker.Clear();
            return MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.Conflict(
                "Ese horario se acaba de reservar. Elegí otro horario."));
        }

        await DispatchPushesAsync(pushes);

        return outcome!.Ok
            ? MarketplaceResult<MarketplaceReservationDto>.Success(await LoadDtoAsync(outcome.Value, ct))
            : MarketplaceResult<MarketplaceReservationDto>.Fail(outcome.Error!);
    }

    // ── Mis reservas ─────────────────────────────────────────────────────────

    public async Task<MarketplaceResult<List<MarketplaceReservationDto>>> GetMineAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<List<MarketplaceReservationDto>>.Fail(MarketplaceError.FromAccess(access));
        }

        // Mis reservas vencidas se muestran como vencidas aunque el proceso de fondo todavia no haya pasado.
        db.ChangeTracker.Clear();
        var pushes = await ExpireStaleCoreAsync(DateTime.UtcNow, null, tenant.UserId, ct);
        await db.SaveChangesAsync(ct);
        await DispatchPushesAsync(pushes);

        var query = db.MarketplaceReservations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.BuyerUserId == tenant.UserId);
        return MarketplaceResult<List<MarketplaceReservationDto>>.Success(await ToDtosAsync(query, ct));
    }

    /// <summary>
    /// El comprador cancela una reserva que todavia no pago: se libera el horario. (Cancelar una reserva ya pagada, con su
    /// politica de comision y reembolso, llega en una fase posterior.)
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceReservationDto>> CancelPendingAsync(Guid id, CancellationToken ct)
    {
        var current = await db.MarketplaceReservations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.BuyerUserId == tenant.UserId, ct);
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
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var reservation = await db.MarketplaceReservations.Include(x => x.Slots).FirstAsync(x => x.Id == id, ct);
            if (!MarketplaceStateMachine.CanTransition(reservation.Status, MarketplaceReservationStatus.Cancelled)
                || reservation.Status != MarketplaceReservationStatus.PendingPayment)
            {
                await transaction.RollbackAsync(ct);
                failure = MarketplaceResult<MarketplaceReservationDto>.Fail(MarketplaceError.Conflict(
                    reservation.Status == MarketplaceReservationStatus.Expired
                        ? "La reserva ya venció."
                        : "Esta reserva ya no se puede cancelar desde acá."));
                return;
            }

            var from = reservation.Status;
            reservation.Status = MarketplaceReservationStatus.Cancelled;
            reservation.ExpiresAtUtc = null;
            reservation.CancelledAtUtc = DateTime.UtcNow;
            reservation.CancelledByUserId = tenant.UserId;
            reservation.CancelledBy = MarketplaceCancellationActor.Buyer;
            foreach (var slot in reservation.Slots.Where(x => !x.IsDeleted))
            {
                slot.IsDeleted = true;
            }

            audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                MarketplaceEventActions.ReservationCancelled, from.ToString(), reservation.Status.ToString(),
                new { By = "Buyer", BeforePayment = true });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return failure ?? MarketplaceResult<MarketplaceReservationDto>.Success(await LoadDtoAsync(id, ct));
    }

    // ── Vencimiento ──────────────────────────────────────────────────────────

    /// <summary>
    /// Vence las reservas que no recibieron el comprobante a tiempo: pasan a Expired, liberan sus bloques y avisan al
    /// comprador. Lo corre el proceso de fondo cada pocos segundos; ademas, consultar o reservar vence lo que corresponda al
    /// momento, asi un horario nunca queda bloqueado por un proceso que no corrio.
    /// </summary>
    public async Task<int> ExpireStaleAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var pushes = await ExpireStaleCoreAsync(DateTime.UtcNow, null, null, ct);
        var changes = pushes.Count;
        await db.SaveChangesAsync(ct);
        await DispatchPushesAsync(pushes);
        return changes;
    }

    /// <summary>Marca como vencidas las reservas esperando pago cuyo plazo paso (opcionalmente solo de una publicacion o de un comprador).</summary>
    private async Task<List<MarketplacePushItem>> ExpireStaleCoreAsync(DateTime now, Guid? listingId, Guid? buyerId, CancellationToken ct)
    {
        var query = db.MarketplaceReservations
            .Include(x => x.Slots)
            .Include(x => x.Listing)
            .Where(x => !x.IsDeleted && x.Status == MarketplaceReservationStatus.PendingPayment
                        && x.ExpiresAtUtc != null && x.ExpiresAtUtc <= now);

        if (listingId.HasValue || buyerId.HasValue)
        {
            query = query.Where(x => (listingId != null && x.ListingId == listingId) || (buyerId != null && x.BuyerUserId == buyerId));
        }

        var stale = await query.Take(200).ToListAsync(ct);
        var pushes = new List<MarketplacePushItem>();

        foreach (var reservation in stale)
        {
            var from = reservation.Status;
            MarketplaceStateMachine.EnsureTransition(from, MarketplaceReservationStatus.Expired);
            reservation.Status = MarketplaceReservationStatus.Expired;
            foreach (var slot in reservation.Slots.Where(x => !x.IsDeleted))
            {
                slot.IsDeleted = true;
            }

            audit.Record(reservation.CompanyId, reservation.BuildingId, nameof(MarketplaceReservation), reservation.Id,
                MarketplaceEventActions.ReservationExpired, from.ToString(), reservation.Status.ToString(),
                new { reservation.ExpiresAtUtc, TimeoutMinutes = options.Value.EffectivePaymentTimeoutMinutes }, automatic: true);

            var title = reservation.Listing?.Title ?? "tu reserva";
            const string heading = "Reserva vencida";
            var body = $"Tu reserva de {title} venció porque no se recibió el pago a tiempo. El horario quedó libre; podés reservar de nuevo.";
            db.Notifications.Add(new Notification
            {
                CompanyId = reservation.CompanyId,
                RecipientId = reservation.BuyerUserId,
                Type = NotificationType.MarketplaceReservationExpired,
                Title = heading,
                Body = body,
                EntityType = nameof(MarketplaceReservation),
                EntityId = reservation.Id
            });
            pushes.Add(new MarketplacePushItem(reservation.BuyerUserId, heading, body, reservation.Id));
        }

        return pushes;
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private const string TakenMessage = "Ese horario ya no está disponible. Elegí otro.";
    private const string PendingMessage = "Ya tenés una reserva esperando pago. Pagala, cancelala o esperá a que venza para hacer otra.";

    // El push se manda despues de guardar. Si falla, la operacion no se rompe (el aviso en la app ya quedo guardado).
    private async Task DispatchPushesAsync(IReadOnlyList<MarketplacePushItem> pushes)
    {
        foreach (var item in pushes)
        {
            await push.NotifyUserAsync(item.RecipientId, item.Title, item.Body, item.EntityType, item.EntityId, CancellationToken.None);
        }
    }

    private sealed record Prepared(
        MarketplaceListing Listing, Guid CompanyId, DateTime Start, DateTime End, MarketplaceQuote Quote);

    // Todo lo que se valida antes de cotizar o reservar: acceso, titularidad, estado, horario y precio.
    private async Task<MarketplaceResult<Prepared>> PrepareAsync(MarketplaceQuoteRequest request, CancellationToken ct)
    {
        var listing = await db.MarketplaceListings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ListingId, ct);
        if (listing is null)
        {
            return MarketplaceResult<Prepared>.Fail(MarketplaceError.NotFound());
        }

        var access = await scope.ResolveBuildingAsync(listing.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<Prepared>.Fail(MarketplaceError.FromAccess(access));
        }

        // Solo vecinos del edificio: ser propietario o residente vigente. El personal no reserva por cuenta propia.
        if (!access.Context!.IsMember)
        {
            return MarketplaceResult<Prepared>.Fail(MarketplaceError.NotFound());
        }

        if (listing.OwnerId == tenant.UserId)
        {
            return MarketplaceResult<Prepared>.Fail(MarketplaceError.Forbidden("No podés reservar tu propia publicación."));
        }

        var now = DateTime.UtcNow;
        if (listing.Status != MarketplaceListingStatus.Active || AsUtc(listing.WindowEndUtc) <= now)
        {
            return MarketplaceResult<Prepared>.Fail(MarketplaceError.Conflict("Esta publicación ya no está disponible."));
        }

        // La publicacion solo vale mientras su dueño siga siendo el propietario principal de la unidad.
        if (!await scope.IsPrimaryOwnerOfUnitAsync(listing.OwnerId, listing.UnitId, ct))
        {
            return MarketplaceResult<Prepared>.Fail(MarketplaceError.Conflict("Esta publicación ya no está disponible."));
        }

        var start = AsUtc(request.StartsAtUtc);
        var end = AsUtc(request.EndsAtUtc);
        var intervalError = MarketplaceWindowRules.ValidateReservationInterval(
            AsUtc(listing.WindowStartUtc), AsUtc(listing.WindowEndUtc), start, end, now);
        if (intervalError is not null)
        {
            return MarketplaceResult<Prepared>.Fail(MarketplaceError.BadRequest(intervalError));
        }

        var commission = await CommissionPercentAsync(listing.BuildingId, ct);
        var quote = MarketplacePricing.Quote(listing.HourlyPrice, MarketplaceWindowRules.WholeHours(start, end), commission);
        return MarketplaceResult<Prepared>.Success(new Prepared(listing, access.Context.CompanyId, start, end, quote));
    }

    private static MarketplaceQuoteDto ToQuoteDto(Prepared p) => new()
    {
        ListingId = p.Listing.Id,
        StartsAtUtc = p.Start,
        EndsAtUtc = p.End,
        Hours = p.Quote.Hours,
        HourlyPrice = p.Quote.HourlyPrice,
        BaseAmount = p.Quote.BaseAmount,
        CommissionPercent = p.Quote.CommissionPercent,
        CommissionAmount = p.Quote.CommissionAmount,
        TotalAmount = p.Quote.TotalAmount
    };

    private Task<decimal> CommissionPercentAsync(Guid buildingId, CancellationToken ct) =>
        db.Buildings.AsNoTracking().Where(x => x.Id == buildingId).Select(x => x.MarketplaceCommissionPercent).FirstAsync(ct);

    // Bloques ocupados por reservas vivas. Una reserva esperando pago ya vencida NO ocupa, aunque el proceso de fondo
    // todavia no la haya marcado.
    private async Task<Dictionary<Guid, List<DateTime>>> OccupiedSlotsAsync(IReadOnlyCollection<Guid> listingIds, DateTime now, CancellationToken ct)
    {
        if (listingIds.Count == 0)
        {
            return [];
        }

        var rows = await db.MarketplaceReservationSlots.AsNoTracking()
            .Where(s => !s.IsDeleted && listingIds.Contains(s.ListingId)
                        && s.Reservation != null && !s.Reservation.IsDeleted
                        && LiveStatuses.Contains(s.Reservation.Status)
                        && !(s.Reservation.Status == MarketplaceReservationStatus.PendingPayment
                             && s.Reservation.ExpiresAtUtc != null && s.Reservation.ExpiresAtUtc <= now))
            .Select(s => new { s.ListingId, s.SlotStartUtc })
            .ToListAsync(ct);

        return rows.GroupBy(x => x.ListingId).ToDictionary(g => g.Key, g => g.Select(x => AsUtc(x.SlotStartUtc)).ToList());
    }

    // Hay alguna hora entera libre (dos bloques seguidos libres) que empiece en el futuro.
    private static bool HasFreeHour(DateTime windowStart, DateTime windowEnd, List<DateTime> occupied, DateTime now)
    {
        var taken = occupied.ToHashSet();
        var slots = MarketplaceWindowRules.Slots(windowStart, windowEnd);
        var firstAllowed = MarketplaceWindowRules.NextSlotAfter(now);

        for (var i = 0; i + 1 < slots.Count; i++)
        {
            if (slots[i] >= firstAllowed && !taken.Contains(slots[i]) && !taken.Contains(slots[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static List<MarketplaceIntervalDto> MergeIntervals(List<DateTime> occupied)
    {
        var merged = new List<MarketplaceIntervalDto>();
        foreach (var slot in occupied.Distinct().OrderBy(x => x))
        {
            var end = slot.AddMinutes(MarketplaceWindowRules.SlotMinutes);
            if (merged.Count > 0 && merged[^1].EndUtc == slot)
            {
                merged[^1].EndUtc = end;
            }
            else
            {
                merged.Add(new MarketplaceIntervalDto { StartUtc = slot, EndUtc = end });
            }
        }

        return merged;
    }

    // MP-00000125: numero de operacion correlativo por empresa. Corre dentro de la transaccion Serializable y el indice unico
    // (empresa, numero) es la red de seguridad.
    private async Task<string> NextReferenceAsync(Guid companyId, CancellationToken ct)
    {
        var last = await db.MarketplaceReservations.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Reference.StartsWith("MP-"))
            .OrderByDescending(x => x.Reference)
            .Select(x => x.Reference)
            .FirstOrDefaultAsync(ct);

        var next = last is not null && int.TryParse(last.AsSpan(3), out var value) ? value + 1 : 1;
        return $"MP-{next:D8}";
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private async Task<MarketplaceReservationDto> LoadDtoAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync(db.MarketplaceReservations.AsNoTracking().Where(x => x.Id == id), ct)).Single();

    private async Task<List<MarketplaceReservationDto>> ToDtosAsync(IQueryable<MarketplaceReservation> query, CancellationToken ct)
    {
        var rows = await query
            .OrderByDescending(x => x.StartsAtUtc)
            .Select(x => new
            {
                x.Id, x.Reference, x.ListingId, x.BuildingId,
                Title = x.Listing != null ? x.Listing.Title : string.Empty,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                x.StartsAtUtc, x.EndsAtUtc, x.Hours, x.HourlyPrice, x.BaseAmount, x.CommissionPercent,
                x.CommissionAmount, x.TotalAmount, x.Status, x.ExpiresAtUtc, x.CancelReason, x.CreatedAtUtc
            })
            .ToListAsync(ct);

        return rows.Select(x => new MarketplaceReservationDto
        {
            Id = x.Id,
            Reference = x.Reference,
            ListingId = x.ListingId,
            BuildingId = x.BuildingId,
            Title = x.Title,
            UnitCode = x.UnitCode,
            StartsAtUtc = AsUtc(x.StartsAtUtc),
            EndsAtUtc = AsUtc(x.EndsAtUtc),
            Hours = x.Hours,
            HourlyPrice = x.HourlyPrice,
            BaseAmount = x.BaseAmount,
            CommissionPercent = x.CommissionPercent,
            CommissionAmount = x.CommissionAmount,
            TotalAmount = x.TotalAmount,
            Status = x.Status.ToString(),
            ExpiresAtUtc = x.ExpiresAtUtc.HasValue ? AsUtc(x.ExpiresAtUtc.Value) : null,
            CancelReason = x.CancelReason,
            CreatedAtUtc = AsUtc(x.CreatedAtUtc)
        }).ToList();
    }
}
