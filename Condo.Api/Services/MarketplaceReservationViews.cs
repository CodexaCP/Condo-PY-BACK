using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Arma las reservas tal como las ve cada parte: el comprador (con lo que puede hacer, su reclamo y su reembolso) y el propietario
/// (quien reservo y lo que va a recibir). Todas las banderas ("se puede cancelar", "se puede reportar un problema") las decide el
/// servidor con las mismas reglas que despues validan los endpoints.
/// </summary>
internal static class MarketplaceReservationViews
{
    public static async Task<List<MarketplaceReservationDto>> LoadBuyerViewAsync(
        CondoDbContext db, IQueryable<MarketplaceReservation> query, CancellationToken ct)
    {
        var rows = await query
            .OrderByDescending(x => x.StartsAtUtc)
            .Select(x => new
            {
                x.Id, x.Reference, x.ListingId, x.BuildingId,
                Title = x.Listing != null ? x.Listing.Title : string.Empty,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                x.StartsAtUtc, x.EndsAtUtc, x.Hours, x.HourlyPrice, x.BaseAmount, x.CommissionPercent,
                x.CommissionAmount, x.TotalAmount, x.Status, x.CreditStatus, x.ExpiresAtUtc, x.CancelReason, x.CreatedAtUtc,
                x.StartNoticeSentAtUtc, x.StartResponse
            })
            .ToListAsync(ct);

        var ids = rows.Select(x => x.Id).ToList();
        var claims = await LatestClaimsAsync(db, ids, ct);
        var refunds = ids.Count == 0
            ? new Dictionary<Guid, MarketplaceRefund>()
            : await db.MarketplaceRefunds.AsNoTracking()
                .Where(x => !x.IsDeleted && ids.Contains(x.ReservationId))
                .ToDictionaryAsync(x => x.ReservationId, ct);
        var now = DateTime.UtcNow;

        return rows.Select(x =>
        {
            claims.TryGetValue(x.Id, out var claim);
            refunds.TryGetValue(x.Id, out var refund);
            var hasOpenClaim = claim?.Status == MarketplaceClaimStatus.Open;

            return new MarketplaceReservationDto
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
                CreatedAtUtc = AsUtc(x.CreatedAtUtc),
                CanCancel = x.Status == MarketplaceReservationStatus.PendingPayment
                            || MarketplaceCancellationRules.CanCancelPaid(x.Status, x.StartsAtUtc, now),
                CanReportProblem = MarketplaceCancellationRules.CanOpenClaim(
                    x.Status, x.CreditStatus, x.StartsAtUtc, x.EndsAtUtc, hasOpenClaim, now),
                NeedsStartResponse = MarketplaceCancellationRules.NeedsStartResponse(
                    x.Status, x.StartNoticeSentAtUtc, x.StartResponse, x.EndsAtUtc, now),
                StartResponse = x.StartResponse?.ToString(),
                ClaimStatus = claim?.Status.ToString(),
                ClaimResolution = claim?.Resolution?.ToString(),
                ClaimResolutionNote = claim?.ResolutionNote,
                RefundAmount = refund?.Amount,
                RefundStatus = refund?.Status.ToString(),
                RefundDueAtUtc = refund is null ? null : AsUtc(refund.CreatedAtUtc) + MarketplaceCancellationRules.RefundMaxTime
            };
        }).ToList();
    }

    public static async Task<List<MarketplaceOwnerReservationDto>> LoadOwnerViewAsync(
        CondoDbContext db, IQueryable<MarketplaceReservation> query, CancellationToken ct)
    {
        var rows = await query
            .OrderByDescending(x => x.StartsAtUtc)
            .Select(x => new
            {
                x.Id, x.Reference, x.ListingId, x.BuildingId, x.BuyerUserId,
                Title = x.Listing != null ? x.Listing.Title : string.Empty,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                BuyerName = x.Buyer != null ? x.Buyer.FullName : string.Empty,
                x.StartsAtUtc, x.EndsAtUtc, x.Hours, x.OwnerNetAmount, x.Status, x.CreditStatus, x.CreditedAtUtc,
                x.CancelReason, x.CreatedAtUtc
            })
            .ToListAsync(ct);

        var ids = rows.Select(x => x.Id).ToList();
        var claims = await LatestClaimsAsync(db, ids, ct);
        var unitsByBuyer = await BuyerUnitsAsync(db, rows.Select(x => x.BuyerUserId).Distinct().ToList(),
            rows.Select(x => x.BuildingId).Distinct().ToList(), ct);
        var now = DateTime.UtcNow;

        return rows.Select(x =>
        {
            claims.TryGetValue(x.Id, out var claim);
            unitsByBuyer.TryGetValue((x.BuyerUserId, x.BuildingId), out var units);
            return new MarketplaceOwnerReservationDto
            {
                Id = x.Id,
                Reference = x.Reference,
                ListingId = x.ListingId,
                BuildingId = x.BuildingId,
                Title = x.Title,
                UnitCode = x.UnitCode,
                BuyerName = x.BuyerName,
                BuyerUnits = units is null ? string.Empty : string.Join(", ", units.Distinct().OrderBy(c => c)),
                StartsAtUtc = AsUtc(x.StartsAtUtc),
                EndsAtUtc = AsUtc(x.EndsAtUtc),
                Hours = x.Hours,
                OwnerNetAmount = x.OwnerNetAmount,
                Status = x.Status.ToString(),
                CreditStatus = x.CreditStatus.ToString(),
                CreditedAtUtc = x.CreditedAtUtc.HasValue ? AsUtc(x.CreditedAtUtc.Value) : null,
                CancelReason = x.CancelReason,
                CanCancel = MarketplaceCancellationRules.CanCancelPaid(x.Status, x.StartsAtUtc, now),
                CanReportProblem = MarketplaceCancellationRules.CanOpenClaim(
                    x.Status, x.CreditStatus, x.StartsAtUtc, x.EndsAtUtc, claim?.Status == MarketplaceClaimStatus.Open, now),
                ClaimStatus = claim?.Status.ToString(),
                ClaimResolution = claim?.Resolution?.ToString(),
                ClaimResolutionNote = claim?.ResolutionNote,
                CreatedAtUtc = AsUtc(x.CreatedAtUtc)
            };
        }).ToList();
    }

    // El reclamo mas reciente de cada reserva (si hay uno abierto es siempre el mas reciente).
    private static async Task<Dictionary<Guid, MarketplaceClaim>> LatestClaimsAsync(
        CondoDbContext db, IReadOnlyCollection<Guid> reservationIds, CancellationToken ct)
    {
        if (reservationIds.Count == 0)
        {
            return [];
        }

        var claims = await db.MarketplaceClaims.AsNoTracking()
            .Where(x => !x.IsDeleted && reservationIds.Contains(x.ReservationId))
            .ToListAsync(ct);
        return claims.GroupBy(x => x.ReservationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAtUtc).First());
    }

    // Unidades de cada comprador en el edificio (propietario o residente vigente): "Reservado por Tony, unidad 5".
    internal static async Task<Dictionary<(Guid UserId, Guid BuildingId), List<string>>> BuyerUnitsAsync(
        CondoDbContext db, IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> buildingIds, CancellationToken ct)
    {
        var result = new Dictionary<(Guid UserId, Guid BuildingId), List<string>>();
        if (userIds.Count == 0 || buildingIds.Count == 0)
        {
            return result;
        }

        var owned = await db.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && userIds.Contains(x.OwnerId) && x.Unit != null && !x.Unit.IsDeleted
                        && buildingIds.Contains(x.Unit.BuildingId))
            .Select(x => new { UserId = x.OwnerId, x.Unit!.BuildingId, x.Unit.Code })
            .ToListAsync(ct);
        foreach (var row in owned)
        {
            Add(result, row.UserId, row.BuildingId, row.Code);
        }

        var resided = await db.UnitResidents.AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                        && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId != null
                        && userIds.Contains(x.Resident.ApplicationUserId.Value)
                        && x.Unit != null && !x.Unit.IsDeleted && buildingIds.Contains(x.Unit.BuildingId))
            .Select(x => new { UserId = x.Resident!.ApplicationUserId!.Value, x.Unit!.BuildingId, x.Unit.Code })
            .ToListAsync(ct);
        foreach (var row in resided)
        {
            Add(result, row.UserId, row.BuildingId, row.Code);
        }

        return result;
    }

    private static void Add(Dictionary<(Guid UserId, Guid BuildingId), List<string>> map, Guid userId, Guid buildingId, string code)
    {
        if (!map.TryGetValue((userId, buildingId), out var list))
        {
            map[(userId, buildingId)] = list = [];
        }

        list.Add(code);
    }

    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
