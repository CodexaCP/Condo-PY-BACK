using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Extracto de la cuenta aparte del marketplace (una por edificio). No es una cuenta bancaria y no toca la contabilidad ni las
/// Finanzas del edificio: solo refleja entradas y salidas por concepto. Lo ven el SuperAdmin, el Administrador de empresa y el
/// Encargado (con su alcance); el Operador no. Solo el SuperAdmin carga ajustes manuales.
/// </summary>
public class MarketplaceAccountService(
    CondoDbContext db,
    ITenantContext tenant,
    MarketplaceScope scope,
    MarketplaceAudit audit)
{
    private const int ConceptMaxLength = 300;
    private const decimal MaxAdjustment = 1_000_000_000_000m;

    // Hora local de Paraguay (UTC-3), la misma que usa el resto del sistema para decidir "hoy" y los periodos.
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-3);

    private static readonly string[] ViewerRoles = ["SuperAdmin", "CompanyAdmin", "BuildingManager"];

    private bool CanView => ViewerRoles.Contains(tenant.Role, StringComparer.OrdinalIgnoreCase);

    public static DateTime LocalDayStartUtc(DateOnly day) => day.ToDateTime(TimeOnly.MinValue) - LocalOffset;

    public static DateOnly TodayLocal() => DateOnly.FromDateTime(DateTime.UtcNow + LocalOffset);

    // ── Extracto ─────────────────────────────────────────────────────────────

    public async Task<MarketplaceResult<MarketplaceStatementDto>> GetStatementAsync(
        Guid buildingId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var access = await RequireViewerAsync(buildingId, ct);
        if (!access.Ok)
        {
            return MarketplaceResult<MarketplaceStatementDto>.Fail(access.Error!);
        }

        // Por defecto, el mes en curso.
        var today = TodayLocal();
        var fromDate = from ?? new DateOnly(today.Year, today.Month, 1);
        var toDate = to ?? today;
        if (toDate < fromDate)
        {
            return MarketplaceResult<MarketplaceStatementDto>.Fail(MarketplaceError.BadRequest("La fecha hasta no puede ser anterior a la fecha desde."));
        }

        var fromUtc = LocalDayStartUtc(fromDate);
        var toUtcExclusive = LocalDayStartUtc(toDate.AddDays(1));

        // Los importes se suman en memoria (los extractos de un edificio son chicos y asi no dependen del proveedor de base de datos).
        var all = await db.MarketplaceAccountMovements.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => new { x.Kind, x.Amount, x.OccurredAtUtc })
            .ToListAsync(ct);

        var opening = all.Where(x => AsUtc(x.OccurredAtUtc) < fromUtc).Sum(x => x.Amount);
        var inPeriod = all.Where(x => AsUtc(x.OccurredAtUtc) >= fromUtc && AsUtc(x.OccurredAtUtc) < toUtcExclusive).ToList();
        var currentBalance = all.Sum(x => x.Amount);

        var pendingToCredit = (await db.MarketplaceReservations.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId
                            && (x.Status == MarketplaceReservationStatus.Confirmed || x.Status == MarketplaceReservationStatus.Completed)
                            && (x.CreditStatus == MarketplaceCreditStatus.Pending || x.CreditStatus == MarketplaceCreditStatus.Held))
                .Select(x => x.OwnerNetAmount)
                .ToListAsync(ct))
            .Sum();

        var rows = await LoadRowsAsync(buildingId, fromUtc, toUtcExclusive, ct);

        var buildingName = await db.Buildings.AsNoTracking().Where(x => x.Id == buildingId).Select(x => x.Name).FirstAsync(ct);
        var closing = opening + inPeriod.Sum(x => x.Amount);

        return MarketplaceResult<MarketplaceStatementDto>.Success(new MarketplaceStatementDto
        {
            BuildingId = buildingId,
            BuildingName = buildingName,
            FromDate = fromDate,
            ToDate = toDate,
            CanEdit = tenant.IsSuperAdmin,
            Rows = rows,
            Summary = new MarketplaceAccountSummaryDto
            {
                OpeningBalance = opening,
                TotalIn = inPeriod.Where(x => x.Kind == MarketplaceAccountMovementKind.PaymentIn).Sum(x => x.Amount),
                TotalCredited = -inPeriod.Where(x => x.Kind == MarketplaceAccountMovementKind.OwnerCredit).Sum(x => x.Amount),
                TotalRefunds = -inPeriod.Where(x => x.Kind == MarketplaceAccountMovementKind.RefundOut).Sum(x => x.Amount),
                TotalAdjustments = inPeriod.Where(x => x.Kind == MarketplaceAccountMovementKind.Adjustment).Sum(x => x.Amount),
                ClosingBalance = closing,
                CurrentBalance = currentBalance,
                PendingToCredit = pendingToCredit,
                ManagementGain = currentBalance - pendingToCredit
            }
        });
    }

    private async Task<List<MarketplaceAccountRowDto>> LoadRowsAsync(Guid buildingId, DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct)
    {
        var movements = await db.MarketplaceAccountMovements.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc < toUtcExclusive)
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.OccurredAtUtc, x.Kind, x.Amount, x.Concept, x.ReservationId, x.CreatedByUserId,
                Reference = x.Reservation != null ? x.Reservation.Reference : null,
                CreditStatus = x.Reservation != null ? (MarketplaceCreditStatus?)x.Reservation.CreditStatus : null
            })
            .ToListAsync(ct);

        var userIds = movements.Where(x => x.CreatedByUserId.HasValue).Select(x => x.CreatedByUserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ApplicationUsers.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        // La acreditacion se puede revertir solo mientras el saldo del propietario siga intacto.
        var creditReservationIds = movements
            .Where(x => x.Kind == MarketplaceAccountMovementKind.OwnerCredit && x.ReservationId.HasValue && x.CreditStatus == MarketplaceCreditStatus.Credited)
            .Select(x => x.ReservationId!.Value).Distinct().ToList();
        var intact = new HashSet<Guid>();
        if (tenant.IsSuperAdmin && creditReservationIds.Count > 0)
        {
            var lots = await db.OwnerCreditMovements.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Kind == OwnerCreditMovementKind.Generated
                            && x.MarketplaceReservationId != null && creditReservationIds.Contains(x.MarketplaceReservationId.Value))
                .Select(x => new { Id = x.MarketplaceReservationId!.Value, x.Amount, x.RemainingAmount })
                .ToListAsync(ct);
            intact = lots.Where(x => x.RemainingAmount == x.Amount).Select(x => x.Id).ToHashSet();
        }

        return movements.Select(x => new MarketplaceAccountRowDto
        {
            Id = x.Id,
            OccurredAtUtc = AsUtc(x.OccurredAtUtc),
            Kind = x.Kind.ToString(),
            Amount = x.Amount,
            Concept = x.Concept,
            ReservationId = x.ReservationId,
            Reference = x.Reference,
            CreatedByName = x.CreatedByUserId.HasValue ? names.GetValueOrDefault(x.CreatedByUserId.Value) : null,
            CanReverse = x.Kind == MarketplaceAccountMovementKind.OwnerCredit && x.ReservationId.HasValue && intact.Contains(x.ReservationId.Value)
        }).ToList();
    }

    // ── Ajustes manuales (solo SuperAdmin) ───────────────────────────────────

    public async Task<MarketplaceResult<MarketplaceAccountRowDto>> AddAdjustmentAsync(MarketplaceAdjustmentRequest request, CancellationToken ct)
    {
        if (!tenant.IsSuperAdmin)
        {
            return MarketplaceResult<MarketplaceAccountRowDto>.Fail(
                MarketplaceError.Forbidden("Solo el SuperAdmin puede cargar movimientos manuales en la cuenta."));
        }

        var concept = (request.Concept ?? string.Empty).Trim();
        if (concept.Length == 0)
        {
            return MarketplaceResult<MarketplaceAccountRowDto>.Fail(MarketplaceError.BadRequest("Indicá el concepto del movimiento."));
        }

        if (concept.Length > ConceptMaxLength)
        {
            return MarketplaceResult<MarketplaceAccountRowDto>.Fail(
                MarketplaceError.BadRequest($"El concepto no puede superar los {ConceptMaxLength} caracteres."));
        }

        if (request.Amount == 0m)
        {
            return MarketplaceResult<MarketplaceAccountRowDto>.Fail(MarketplaceError.BadRequest("El importe no puede ser cero."));
        }

        if (decimal.Truncate(request.Amount) != request.Amount)
        {
            return MarketplaceResult<MarketplaceAccountRowDto>.Fail(MarketplaceError.BadRequest("El importe debe ser un monto entero en guaraníes."));
        }

        if (Math.Abs(request.Amount) > MaxAdjustment)
        {
            return MarketplaceResult<MarketplaceAccountRowDto>.Fail(MarketplaceError.BadRequest("El importe es demasiado alto."));
        }

        var access = await scope.ResolveBuildingAsync(request.BuildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<MarketplaceAccountRowDto>.Fail(MarketplaceError.FromAccess(access));
        }

        db.ChangeTracker.Clear();
        var movement = new MarketplaceAccountMovement
        {
            CompanyId = access.Context!.CompanyId,
            BuildingId = request.BuildingId,
            Kind = MarketplaceAccountMovementKind.Adjustment,
            Amount = request.Amount,
            ReservationId = null,
            Concept = concept,
            CreatedByUserId = tenant.UserId,
            OccurredAtUtc = DateTime.UtcNow
        };
        db.MarketplaceAccountMovements.Add(movement);

        audit.Record(movement.CompanyId, movement.BuildingId, nameof(MarketplaceAccountMovement), movement.Id,
            MarketplaceEventActions.AccountMovementRecorded, null, movement.Kind.ToString(),
            new { movement.Amount, movement.Concept, Manual = true });
        await db.SaveChangesAsync(ct);

        var name = await db.ApplicationUsers.AsNoTracking().Where(x => x.Id == tenant.UserId).Select(x => x.FullName).FirstOrDefaultAsync(ct);
        return MarketplaceResult<MarketplaceAccountRowDto>.Success(new MarketplaceAccountRowDto
        {
            Id = movement.Id,
            OccurredAtUtc = AsUtc(movement.OccurredAtUtc),
            Kind = movement.Kind.ToString(),
            Amount = movement.Amount,
            Concept = movement.Concept,
            CreatedByName = name
        });
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    // El extracto lo ven solo SuperAdmin, Administrador de empresa y Encargado, con el edificio en su alcance.
    private async Task<MarketplaceResult<Guid>> RequireViewerAsync(Guid buildingId, CancellationToken ct)
    {
        var access = await scope.ResolveBuildingAsync(buildingId, ct);
        if (!access.Allowed)
        {
            return MarketplaceResult<Guid>.Fail(MarketplaceError.FromAccess(access));
        }

        if (!access.Context!.IsStaff || !CanView)
        {
            return MarketplaceResult<Guid>.Fail(MarketplaceError.Forbidden("Tu rol no tiene acceso a la cuenta del Marketplace."));
        }

        return MarketplaceResult<Guid>.Success(buildingId);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
