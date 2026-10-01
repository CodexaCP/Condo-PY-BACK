using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>Endpoints de la seccion del Encargado en la app Android.</summary>
[ApiController]
[Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
[Route("api/manager")]
public class ManagerController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope) : ControllerBase
{
    private static readonly TimeZoneInfo Pyt = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");

    // ─── SUMMARY ─────────────────────────────────────────────────────────────
    // Todo acotado a UN edificio y calculado con consultas agregadas (el dashboard general carga tablas enteras).
    [HttpGet("summary")]
    public async Task<ActionResult<ManagerSummaryDto>> GetSummary([FromQuery] Guid buildingId, CancellationToken ct)
    {
        if (buildingId == Guid.Empty) return BadRequest("El edificio es obligatorio.");

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => new { x.Id, x.Name })
            .FirstOrDefaultAsync(ct);
        if (building is null) return NotFound();

        if (!await accessScope.CanAccessBuildingAsync(buildingId, ct)) return Forbid();

        var now = DateTime.UtcNow;
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, Pyt));

        // Pagos de propietarios que tocan una unidad del edificio.
        var paymentsByStatus = await dbContext.OwnerPayments
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                        && (x.Status == OwnerPaymentStatus.Pending || x.Status == OwnerPaymentStatus.UnderReview)
                        && x.Units.Any(u => !u.IsDeleted && u.Unit != null && u.Unit.BuildingId == buildingId))
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var claimsByStatus = await dbContext.Claims
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId
                        && (x.Status == "Pendiente" || x.Status == "EnProceso"))
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var pendingReservations = await dbContext.AmenityReservations
            .AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.BuildingId == buildingId
                             && (x.Status == AmenityReservationStatus.PendingPayment
                                 || x.Status == AmenityReservationStatus.PendingReview), ct);

        var summary = new ManagerSummaryDto
        {
            BuildingId = building.Id,
            BuildingName = building.Name,
            PendingOwnerPayments = paymentsByStatus.FirstOrDefault(x => x.Status == OwnerPaymentStatus.Pending)?.Count ?? 0,
            UnderReviewOwnerPayments = paymentsByStatus.FirstOrDefault(x => x.Status == OwnerPaymentStatus.UnderReview)?.Count ?? 0,
            PendingClaims = claimsByStatus.FirstOrDefault(x => x.Status == "Pendiente")?.Count ?? 0,
            InProgressClaims = claimsByStatus.FirstOrDefault(x => x.Status == "EnProceso")?.Count ?? 0,
            PendingReservations = pendingReservations
        };

        await FillCurrentPeriodAsync(summary, buildingId, todayLocal, ct);
        await FillOverdueAsync(summary, buildingId, todayLocal, ct);
        summary.Plan = await LoadPlanAsync(buildingId, now, ct);

        return Ok(summary);
    }

    // Periodo del mes actual; si no existe, el ultimo que ya no es borrador.
    private async Task FillCurrentPeriodAsync(ManagerSummaryDto summary, Guid buildingId, DateOnly today, CancellationToken ct)
    {
        var periods = dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId);

        var period = await periods
                         .Where(x => x.Year == today.Year && x.Month == today.Month)
                         .Select(x => new { x.Id, x.Name, x.Status, x.DueDate })
                         .FirstOrDefaultAsync(ct)
                     ?? await periods
                         .Where(x => x.Status != ExpensePeriodStatus.Draft)
                         .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                         .Select(x => new { x.Id, x.Name, x.Status, x.DueDate })
                         .FirstOrDefaultAsync(ct);

        if (period is null) return;

        summary.CurrentPeriod = new ManagerPeriodDto
        {
            Id = period.Id,
            Name = period.Name,
            Status = period.Status.ToString(),
            DueDate = period.DueDate
        };

        // Neto emitido (incluye las reversiones de cargos, que restan) y cobrado sin pagos revertidos.
        summary.CurrentPeriodCharged = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

        summary.CurrentPeriodCollected = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsReversed && x.ExpensePeriodId == period.Id)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

        summary.CollectionRatePercentage = summary.CurrentPeriodCharged <= 0m
            ? 0m
            : Math.Round(summary.CurrentPeriodCollected / summary.CurrentPeriodCharged * 100m, 2);
    }

    // Misma regla que el reporte de morosidad: periodos no borrador y vencidos; saldo por unidad y periodo =
    // cargado - pagado (nunca negativo). Los pagos revertidos no cuentan como pagados.
    private async Task FillOverdueAsync(ManagerSummaryDto summary, Guid buildingId, DateOnly today, CancellationToken ct)
    {
        var charged = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.Unit != null && x.Unit.BuildingId == buildingId
                        && x.ExpensePeriod != null
                        && x.ExpensePeriod.Status != ExpensePeriodStatus.Draft
                        && x.ExpensePeriod.DueDate < today)
            .GroupBy(x => new { x.UnitId, x.ExpensePeriodId })
            .Select(g => new { g.Key.UnitId, g.Key.ExpensePeriodId, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        if (charged.Count == 0) return;

        var paid = (await dbContext.Payments
                .AsNoTracking()
                .Where(x => !x.IsDeleted && !x.IsReversed
                            && x.Unit != null && x.Unit.BuildingId == buildingId
                            && x.ExpensePeriod != null
                            && x.ExpensePeriod.Status != ExpensePeriodStatus.Draft)
                .GroupBy(x => new { x.UnitId, x.ExpensePeriodId })
                .Select(g => new { g.Key.UnitId, g.Key.ExpensePeriodId, Total = g.Sum(x => x.Amount) })
                .ToListAsync(ct))
            .ToDictionary(x => (x.UnitId, x.ExpensePeriodId), x => x.Total);

        var openBalances = charged
            .Where(x => x.Total > 0m)
            .Select(x => new { x.UnitId, Balance = x.Total - Math.Min(paid.GetValueOrDefault((x.UnitId, x.ExpensePeriodId), 0m), x.Total) })
            .Where(x => x.Balance > 0m)
            .ToList();

        summary.OverdueBalance = openBalances.Sum(x => x.Balance);
        summary.UnitsInArrears = openBalances.Select(x => x.UnitId).Distinct().Count();
    }

    // Plan vigente del edificio (el no archivado). Si hubiera mas de uno, el de vencimiento mas lejano.
    private async Task<ManagerPlanDto?> LoadPlanAsync(Guid buildingId, DateTime now, CancellationToken ct)
    {
        var plan = await dbContext.BuildingPlans
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived && x.BuildingId == buildingId)
            .OrderByDescending(x => x.EndDate)
            .Select(x => new
            {
                Name = x.Plan != null ? x.Plan.Name : string.Empty,
                Grace = x.Plan != null ? x.Plan.GracePeriodDays : 5,
                x.EndDate
            })
            .FirstOrDefaultAsync(ct);

        if (plan is null) return null;

        var overdue = plan.EndDate.Date < now.Date;
        return new ManagerPlanDto
        {
            Name = plan.Name,
            Status = PlanAccessPolicy.GetStatusName(false, plan.EndDate, plan.Grace, now),
            EndDate = plan.EndDate,
            DaysUntilExpiry = Math.Max(0, (int)(plan.EndDate.Date - now.Date).TotalDays),
            DaysUntilBlocked = overdue ? PlanAccessPolicy.DaysUntilBlocked(plan.EndDate, plan.Grace, now) : null
        };
    }
}
