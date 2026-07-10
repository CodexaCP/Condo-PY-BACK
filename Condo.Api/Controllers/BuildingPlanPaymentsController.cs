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
[Route("api/building-plan-payments")]
public class BuildingPlanPaymentsController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext,
    IAccessScopeService accessScopeService) : ControllerBase
{
    // ─── GET ALL ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingPlanPaymentDto>>> GetAll(CancellationToken ct)
    {
        IQueryable<BuildingPlanPayment> query = dbContext.BuildingPlanPayments
            .AsNoTracking()
            .Include(x => x.BuildingPlan)
                .ThenInclude(bp => bp!.Building)
            .Include(x => x.BuildingPlan)
                .ThenInclude(bp => bp!.Plan)
            .Include(x => x.SubmittedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.Company)
            .Where(x => !x.IsDeleted);

        if (!tenantContext.IsSuperAdmin)
        {
            var accessibleIds = await accessScopeService.GetAccessibleBuildingIdsAsync(ct);
            if (accessibleIds.Count == 0) return Ok(Array.Empty<BuildingPlanPaymentDto>());
            query = query.Where(x => x.BuildingPlan != null && accessibleIds.Contains(x.BuildingPlan.BuildingId));
        }

        var payments = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(payments.Select(ToDto).ToList());
    }

    // ─── GET BY ID ───────────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildingPlanPaymentDto>> GetById(Guid id, CancellationToken ct)
    {
        var payment = await dbContext.BuildingPlanPayments
            .AsNoTracking()
            .Include(x => x.BuildingPlan)
                .ThenInclude(bp => bp!.Building)
            .Include(x => x.BuildingPlan)
                .ThenInclude(bp => bp!.Plan)
            .Include(x => x.SubmittedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (payment is null) return NotFound();

        if (!tenantContext.IsSuperAdmin)
        {
            var accessibleIds = await accessScopeService.GetAccessibleBuildingIdsAsync(ct);
            if (payment.BuildingPlan is null || !accessibleIds.Contains(payment.BuildingPlan.BuildingId))
                return Forbid();
        }

        return Ok(ToDto(payment));
    }

    // ─── SUBMIT PAYMENT ──────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<BuildingPlanPaymentDto>> Submit(
        [FromBody] BuildingPlanPaymentCreateRequest request, CancellationToken ct)
    {
        if (tenantContext.IsSuperAdmin)
            return BadRequest("Los pagos deben ser enviados por un CompanyAdmin o BuildingManager.");

        if (request.DeclaredAmount <= 0)
            return BadRequest("El monto declarado debe ser mayor a cero.");

        if (request.PaymentDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest("La fecha de pago no puede ser futura.");

        // Load BuildingPlan with building info
        var bp = await dbContext.BuildingPlans
            .AsNoTracking()
            .Include(x => x.Building)
                .ThenInclude(b => b!.Condominium)
            .Include(x => x.Plan)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingPlanId, ct);

        if (bp is null) return BadRequest("Asignación de plan no encontrada.");
        if (bp.IsArchived) return BadRequest("No se puede realizar un pago sobre un plan archivado.");
        if (!bp.IsActive) return BadRequest("No se puede realizar un pago sobre un plan suspendido.");

        // Check access
        var accessibleIds = await accessScopeService.GetAccessibleBuildingIdsAsync(ct);
        if (!accessibleIds.Contains(bp.BuildingId))
            return Forbid();

        // One-pending constraint
        var hasPending = await dbContext.BuildingPlanPayments
            .AnyAsync(x => !x.IsDeleted && x.BuildingPlanId == request.BuildingPlanId &&
                           x.Status == BuildingPlanPaymentStatus.Pending, ct);

        if (hasPending)
            return BadRequest("Ya existe un comprobante pendiente de revisión para este plan. Espere la aprobación o rechazo antes de enviar otro.");

        // Resolve CompanyId
        var companyId = bp.Building?.CompanyId ?? bp.Building?.Condominium?.CompanyId;
        if (companyId is null)
            return BadRequest("No se pudo determinar la empresa asociada al edificio.");

        var payment = new BuildingPlanPayment
        {
            BuildingPlanId = request.BuildingPlanId,
            AssignmentScope = bp.AssignmentScope,
            ScopeEntityId = bp.ScopeEntityId,
            CompanyId = companyId.Value,
            DeclaredAmount = request.DeclaredAmount,
            PaymentDate = request.PaymentDate,
            ComprobanteUrl = request.ComprobanteUrl?.Trim() ?? string.Empty,
            Reference = request.Reference?.Trim() ?? string.Empty,
            Status = BuildingPlanPaymentStatus.Pending,
            SubmittedById = tenantContext.UserId
        };

        dbContext.BuildingPlanPayments.Add(payment);
        await dbContext.SaveChangesAsync(ct);

        var result = await LoadPaymentDto(payment.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, result);
    }

    // ─── APPROVE ─────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}/approve")]
    public async Task<ActionResult<BuildingPlanPaymentDto>> Approve(Guid id, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var payment = await dbContext.BuildingPlanPayments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (payment is null) return NotFound();

        if (payment.Status != BuildingPlanPaymentStatus.Pending)
            return BadRequest("Solo se pueden aprobar pagos en estado Pendiente.");

        var bp = await dbContext.BuildingPlans
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == payment.BuildingPlanId, ct);

        if (bp is null) return BadRequest("Asignación de plan no encontrada.");

        var now = DateTime.UtcNow;

        // Approve payment
        payment.Status = BuildingPlanPaymentStatus.Approved;
        payment.ReviewedById = tenantContext.UserId;
        payment.ReviewedAt = now;

        // Mark plan as paid
        bp.IsPaid = true;
        bp.PaidAt = now;
        bp.PaidById = payment.SubmittedById;

        // If renewal dates are set, activate them (swap dates, clear renewal fields)
        if (bp.RenewalStartDate.HasValue && bp.RenewalEndDate.HasValue)
        {
            bp.StartDate = bp.RenewalStartDate.Value;
            bp.EndDate = bp.RenewalEndDate.Value;
            bp.RenewalStartDate = null;
            bp.RenewalEndDate = null;
        }

        await dbContext.SaveChangesAsync(ct);

        var result = await LoadPaymentDto(id, ct);
        return Ok(result);
    }

    // ─── REJECT ──────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}/reject")]
    public async Task<ActionResult<BuildingPlanPaymentDto>> Reject(
        Guid id, [FromBody] BuildingPlanPaymentRejectRequest request, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.RejectionReason))
            return BadRequest("El motivo de rechazo es obligatorio.");

        if (request.RejectionReason.Trim().Length > 500)
            return BadRequest("El motivo de rechazo no puede superar los 500 caracteres.");

        var payment = await dbContext.BuildingPlanPayments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (payment is null) return NotFound();

        if (payment.Status != BuildingPlanPaymentStatus.Pending)
            return BadRequest("Solo se pueden rechazar pagos en estado Pendiente.");

        payment.Status = BuildingPlanPaymentStatus.Rejected;
        payment.RejectionReason = request.RejectionReason.Trim();
        payment.ReviewedById = tenantContext.UserId;
        payment.ReviewedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);

        var result = await LoadPaymentDto(id, ct);
        return Ok(result);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private async Task<BuildingPlanPaymentDto?> LoadPaymentDto(Guid id, CancellationToken ct)
    {
        var p = await dbContext.BuildingPlanPayments
            .AsNoTracking()
            .Include(x => x.BuildingPlan)
                .ThenInclude(bp => bp!.Building)
            .Include(x => x.BuildingPlan)
                .ThenInclude(bp => bp!.Plan)
            .Include(x => x.SubmittedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return p is null ? null : ToDto(p);
    }

    private static BuildingPlanPaymentDto ToDto(BuildingPlanPayment p) => new()
    {
        Id = p.Id,
        BuildingPlanId = p.BuildingPlanId,
        AssignmentScope = p.AssignmentScope.ToString(),
        ScopeEntityId = p.ScopeEntityId,
        CompanyId = p.CompanyId,
        CompanyName = p.Company?.Name ?? string.Empty,
        BuildingName = p.BuildingPlan?.Building?.Name ?? string.Empty,
        PlanName = p.BuildingPlan?.Plan?.Name ?? string.Empty,
        DeclaredAmount = p.DeclaredAmount,
        PaymentDate = p.PaymentDate,
        ComprobanteUrl = p.ComprobanteUrl,
        Reference = p.Reference,
        Status = p.Status.ToString(),
        RejectionReason = p.RejectionReason,
        SubmittedByFullName = p.SubmittedBy?.FullName ?? string.Empty,
        CreatedAtUtc = p.CreatedAtUtc,
        ReviewedByFullName = p.ReviewedBy?.FullName,
        ReviewedAt = p.ReviewedAt
    };
}
