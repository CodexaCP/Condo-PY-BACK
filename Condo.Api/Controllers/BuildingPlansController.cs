using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/building-plans")]
public class BuildingPlansController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext,
    IAccessScopeService accessScopeService) : ControllerBase
{
    private static readonly Guid DefaultPlanId = Guid.Parse("A0000000-0000-0000-0000-000000000001");

    // ─── GET ALL ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingPlanDto>>> GetAll(CancellationToken ct)
    {
        var accessibleIds = await accessScopeService.GetAccessibleBuildingIdsAsync(ct);
        if (!tenantContext.IsSuperAdmin && accessibleIds.Count == 0)
            return Ok(Array.Empty<BuildingPlanDto>());

        var today = DateTime.UtcNow.Date;

        var bps = await dbContext.BuildingPlans
            .AsNoTracking()
            .Include(x => x.Plan)
            .Include(x => x.Building)
                .ThenInclude(b => b!.Company)
            .Include(x => x.Building)
                .ThenInclude(b => b!.Condominium)
                    .ThenInclude(c => c!.Company)
            .Include(x => x.AssignedBy)
            .Include(x => x.PaidBy)
            .Where(x => !x.IsDeleted && accessibleIds.Contains(x.BuildingId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var bpIds = bps.Select(x => x.Id).ToList();

        var pendingList = await dbContext.BuildingPlanPayments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && bpIds.Contains(x.BuildingPlanId) && x.Status == BuildingPlanPaymentStatus.Pending)
            .Select(x => x.BuildingPlanId)
            .ToListAsync(ct);
        var pendingSet = pendingList.ToHashSet();

        return Ok(bps.Select(bp => ToDto(bp, pendingSet.Contains(bp.Id), today)).ToList());
    }

    // ─── GET BY ID ───────────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildingPlanDto>> GetById(Guid id, CancellationToken ct)
    {
        var accessibleIds = await accessScopeService.GetAccessibleBuildingIdsAsync(ct);

        var bp = await dbContext.BuildingPlans
            .AsNoTracking()
            .Include(x => x.Plan)
            .Include(x => x.Building)
                .ThenInclude(b => b!.Company)
            .Include(x => x.Building)
                .ThenInclude(b => b!.Condominium)
                    .ThenInclude(c => c!.Company)
            .Include(x => x.AssignedBy)
            .Include(x => x.PaidBy)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (bp is null) return NotFound();
        if (!accessibleIds.Contains(bp.BuildingId)) return Forbid();

        var hasPending = await dbContext.BuildingPlanPayments
            .AnyAsync(x => !x.IsDeleted && x.BuildingPlanId == id && x.Status == BuildingPlanPaymentStatus.Pending, ct);

        return Ok(ToDto(bp, hasPending, DateTime.UtcNow.Date));
    }

    // ─── MY PLAN ─────────────────────────────────────────────────────────────
    [HttpGet("my-plan")]
    public async Task<ActionResult<IReadOnlyList<BuildingPlanSummaryDto>>> MyPlan(CancellationToken ct)
    {
        if (tenantContext.IsSuperAdmin) return Forbid();

        var accessibleIds = await accessScopeService.GetAccessibleBuildingIdsAsync(ct);
        if (accessibleIds.Count == 0) return Ok(Array.Empty<BuildingPlanSummaryDto>());

        var today = DateTime.UtcNow.Date;

        var bps = await dbContext.BuildingPlans
            .AsNoTracking()
            .Include(x => x.Plan)
            .Include(x => x.Building)
            .Where(x => !x.IsDeleted && !x.IsArchived && accessibleIds.Contains(x.BuildingId))
            .OrderBy(x => x.Building!.Name)
            .ToListAsync(ct);

        return Ok(bps.Select(bp => new BuildingPlanSummaryDto
        {
            Id = bp.Id,
            BuildingId = bp.BuildingId,
            BuildingName = bp.Building?.Name ?? string.Empty,
            PlanName = bp.Plan?.Name ?? string.Empty,
            StartDate = bp.StartDate,
            EndDate = bp.EndDate,
            HasRenewal = bp.RenewalStartDate.HasValue,
            IsPaid = bp.IsPaid,
            IsActive = bp.IsActive,
            Status = ComputeStatus(bp, today),
            DaysUntilExpiry = Math.Max(0, (int)(bp.EndDate.Date - today).TotalDays)
        }).ToList());
    }

    // ─── ASSIGN SINGLE ───────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<BuildingPlanDto>> Assign([FromBody] BuildingPlanAssignRequest request, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        if (request.StartDate >= request.EndDate)
            return BadRequest("La fecha de inicio debe ser anterior a la fecha de fin.");

        var plan = await dbContext.Plans.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.PlanId, ct);
        if (plan is null) return BadRequest("Plan no encontrado.");

        var buildingExists = await dbContext.Buildings.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.Id == request.BuildingId, ct);
        if (!buildingExists) return BadRequest("Edificio no encontrado.");

        var validationError = await ValidateAssignment(request.PlanId, request.BuildingId, ct);
        if (validationError is not null) return BadRequest(validationError);

        await ArchiveActivePlans(request.BuildingId, ct);

        var bp = new BuildingPlan
        {
            PlanId = request.PlanId,
            BuildingId = request.BuildingId,
            AssignmentScope = PlanAssignmentScope.Building,
            ScopeEntityId = request.BuildingId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = true,
            IsArchived = false,
            AssignedById = tenantContext.UserId
        };

        dbContext.BuildingPlans.Add(bp);

        if (!plan.IsAssigned)
        {
            var planTracked = await dbContext.Plans.FindAsync([request.PlanId], ct);
            if (planTracked is not null) planTracked.IsAssigned = true;
        }

        await dbContext.SaveChangesAsync(ct);

        var result = await LoadDto(bp.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = bp.Id }, result);
    }

    // ─── ASSIGN BULK ─────────────────────────────────────────────────────────
    [HttpPost("bulk")]
    public async Task<IActionResult> AssignBulk([FromBody] BuildingPlanBulkAssignRequest request, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        if (request.StartDate >= request.EndDate)
            return BadRequest("La fecha de inicio debe ser anterior a la fecha de fin.");

        if (!Enum.TryParse<PlanAssignmentScope>(request.Scope, true, out var scope) || scope == PlanAssignmentScope.Building)
            return BadRequest("El scope para asignación masiva debe ser 'Company' o 'Condominium'.");

        var plan = await dbContext.Plans.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.PlanId, ct);
        if (plan is null) return BadRequest("Plan no encontrado.");

        var buildings = scope == PlanAssignmentScope.Company
            ? await dbContext.Buildings.AsNoTracking()
                .Where(x => !x.IsDeleted &&
                    (x.CompanyId == request.ScopeEntityId ||
                     (x.Condominium != null && x.Condominium.CompanyId == request.ScopeEntityId)))
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct)
            : await dbContext.Buildings.AsNoTracking()
                .Where(x => !x.IsDeleted && x.CondominiumId == request.ScopeEntityId)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);

        if (buildings.Count == 0)
            return BadRequest("No se encontraron edificios en el scope especificado.");

        // Validate all buildings first — reject entire bulk if any fails
        var errors = new List<object>();
        foreach (var b in buildings)
        {
            var err = await ValidateAssignment(request.PlanId, b.Id, ct);
            if (err is not null)
                errors.Add(new { buildingId = b.Id, buildingName = b.Name, error = err });
        }

        if (errors.Count > 0)
        {
            return BadRequest(new
            {
                message = "La asignación masiva fue rechazada porque algunos edificios no cumplen los requisitos.",
                failedBuildings = errors
            });
        }

        // All validated — archive and assign
        foreach (var b in buildings)
        {
            await ArchiveActivePlans(b.Id, ct);

            dbContext.BuildingPlans.Add(new BuildingPlan
            {
                PlanId = request.PlanId,
                BuildingId = b.Id,
                AssignmentScope = scope,
                ScopeEntityId = request.ScopeEntityId,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsActive = true,
                IsArchived = false,
                AssignedById = tenantContext.UserId
            });
        }

        if (!plan.IsAssigned)
        {
            var planTracked = await dbContext.Plans.FindAsync([request.PlanId], ct);
            if (planTracked is not null) planTracked.IsAssigned = true;
        }

        await dbContext.SaveChangesAsync(ct);

        return Ok(new { assignedCount = buildings.Count });
    }

    // ─── SET RENEWAL ─────────────────────────────────────────────────────────
    [HttpPut("{id:guid}/renewal")]
    public async Task<ActionResult<BuildingPlanDto>> SetRenewal(
        Guid id, [FromBody] BuildingPlanSetRenewalRequest request, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var bp = await dbContext.BuildingPlans
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (bp is null) return NotFound();

        if (bp.IsArchived)
            return BadRequest("No se puede establecer renovación en un plan archivado.");
        if (!bp.IsActive)
            return BadRequest("No se puede establecer renovación en un plan suspendido.");
        if (request.RenewalStartDate >= request.RenewalEndDate)
            return BadRequest("La fecha de inicio de renovación debe ser anterior a la fecha de fin.");
        if (request.RenewalStartDate < bp.EndDate)
            return BadRequest("La fecha de inicio de renovación no puede ser anterior a la fecha de vencimiento actual.");

        bp.RenewalStartDate = request.RenewalStartDate;
        bp.RenewalEndDate = request.RenewalEndDate;

        await dbContext.SaveChangesAsync(ct);

        var result = await LoadDto(id, ct);
        return Ok(result);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private async Task<string?> ValidateAssignment(Guid planId, Guid buildingId, CancellationToken ct)
    {
        var isDemo = planId == DefaultPlanId;

        if (isDemo)
        {
            var alreadyHadDemo = await dbContext.BuildingPlans
                .AnyAsync(x => !x.IsDeleted && x.BuildingId == buildingId && x.PlanId == DefaultPlanId, ct);

            if (alreadyHadDemo)
                return "El plan gratuito ya fue utilizado para este edificio y no puede asignarse nuevamente.";
        }
        else
        {
            var hadDemo = await dbContext.BuildingPlans
                .AnyAsync(x => !x.IsDeleted && x.BuildingId == buildingId && x.PlanId == DefaultPlanId, ct);

            if (!hadDemo)
                return "Este edificio aún no tiene un plan demo asignado. Debe asignar el plan gratuito primero antes de asignar un plan pago.";
        }

        return null;
    }

    private async Task ArchiveActivePlans(Guid buildingId, CancellationToken ct)
    {
        var active = await dbContext.BuildingPlans
            .Where(x => !x.IsDeleted && !x.IsArchived && x.BuildingId == buildingId)
            .ToListAsync(ct);

        foreach (var p in active)
            p.IsArchived = true;
    }

    private async Task<BuildingPlanDto?> LoadDto(Guid id, CancellationToken ct)
    {
        var bp = await dbContext.BuildingPlans
            .AsNoTracking()
            .Include(x => x.Plan)
            .Include(x => x.Building)
                .ThenInclude(b => b!.Company)
            .Include(x => x.Building)
                .ThenInclude(b => b!.Condominium)
                    .ThenInclude(c => c!.Company)
            .Include(x => x.AssignedBy)
            .Include(x => x.PaidBy)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (bp is null) return null;

        var hasPending = await dbContext.BuildingPlanPayments
            .AnyAsync(x => !x.IsDeleted && x.BuildingPlanId == id && x.Status == BuildingPlanPaymentStatus.Pending, ct);

        return ToDto(bp, hasPending, DateTime.UtcNow.Date);
    }

    private static string ComputeStatus(BuildingPlan bp, DateTime today)
    {
        if (bp.IsArchived) return "Archived";
        if (!bp.IsActive) return "Suspended";
        var days = (int)(bp.EndDate.Date - today).TotalDays;
        if (days < 0) return "Expired";
        if (days <= 7) return "ExpiringSoon";
        return "Active";
    }

    private static BuildingPlanDto ToDto(BuildingPlan bp, bool hasPendingPayment, DateTime today) => new()
    {
        Id = bp.Id,
        PlanId = bp.PlanId,
        PlanName = bp.Plan?.Name ?? string.Empty,
        PlanIsDefault = bp.Plan?.IsDefault ?? false,
        PlanPrice = bp.Plan?.Price ?? 0,
        PlanBillingCycle = bp.Plan?.BillingCycle.ToString() ?? string.Empty,
        PlanGracePeriodDays = bp.Plan?.GracePeriodDays ?? 0,
        BuildingId = bp.BuildingId,
        BuildingName = bp.Building?.Name ?? string.Empty,
        CompanyName = bp.Building?.Company?.Name ?? bp.Building?.Condominium?.Company?.Name ?? string.Empty,
        AssignmentScope = bp.AssignmentScope.ToString(),
        ScopeEntityId = bp.ScopeEntityId,
        StartDate = bp.StartDate,
        EndDate = bp.EndDate,
        RenewalStartDate = bp.RenewalStartDate,
        RenewalEndDate = bp.RenewalEndDate,
        IsPaid = bp.IsPaid,
        PaidAt = bp.PaidAt,
        PaidByFullName = bp.PaidBy?.FullName,
        IsActive = bp.IsActive,
        IsArchived = bp.IsArchived,
        AssignedByFullName = bp.AssignedBy?.FullName ?? string.Empty,
        CreatedAtUtc = bp.CreatedAtUtc,
        Status = ComputeStatus(bp, today),
        DaysUntilExpiry = Math.Max(0, (int)(bp.EndDate.Date - today).TotalDays),
        HasPendingPayment = hasPendingPayment
    };
}
