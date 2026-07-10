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
[Route("api/plans")]
public class PlansController(ICondoDbContext dbContext, ITenantContext tenantContext) : ControllerBase
{
    // ─── GET ALL ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> GetAll(CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var plans = await dbContext.Plans
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        var planIds = plans.Select(x => x.Id).ToList();

        var assignedCounts = await dbContext.BuildingPlans
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived && planIds.Contains(x.PlanId))
            .GroupBy(x => x.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, ct);

        return Ok(plans.Select(p => ToDto(p, assignedCounts.GetValueOrDefault(p.Id, 0))).ToList());
    }

    // ─── GET BY ID ───────────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlanDto>> GetById(Guid id, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var plan = await dbContext.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (plan is null) return NotFound();

        var count = await dbContext.BuildingPlans
            .AsNoTracking()
            .CountAsync(x => !x.IsDeleted && !x.IsArchived && x.PlanId == id, ct);

        return Ok(ToDto(plan, count));
    }

    // ─── CREATE ──────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<PlanDto>> Create([FromBody] PlanCreateRequest request, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var error = ValidateRequest(request.Name, request.Price, request.BillingCycle, request.GracePeriodDays);
        if (error is not null) return BadRequest(error);

        if (!Enum.TryParse<BillingCycle>(request.BillingCycle, true, out var cycle))
            return BadRequest($"BillingCycle inválido. Valores permitidos: {string.Join(", ", Enum.GetNames<BillingCycle>())}");

        var plan = new Plan
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            IsDefault = false,
            Price = request.Price,
            BillingCycle = cycle,
            GracePeriodDays = request.GracePeriodDays,
            IsActive = true,
            IsAssigned = false
        };

        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = plan.Id }, ToDto(plan, 0));
    }

    // ─── UPDATE ──────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlanDto>> Update(Guid id, [FromBody] PlanUpdateRequest request, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var plan = await dbContext.Plans
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (plan is null) return NotFound();

        // Regular assigned plans are immutable — only the default plan (system config) can always be edited
        if (plan.IsAssigned && !plan.IsDefault)
            return BadRequest("Este plan ya está asignado a edificios y no puede modificarse. Para realizar cambios, use la función Clonar plan.");

        var error = ValidateRequest(request.Name, request.Price, request.BillingCycle, request.GracePeriodDays);
        if (error is not null) return BadRequest(error);

        if (!Enum.TryParse<BillingCycle>(request.BillingCycle, true, out var cycle))
            return BadRequest($"BillingCycle inválido. Valores permitidos: {string.Join(", ", Enum.GetNames<BillingCycle>())}");

        plan.Name = request.Name.Trim();
        plan.Description = request.Description.Trim();
        plan.Price = request.Price;
        plan.BillingCycle = cycle;
        plan.GracePeriodDays = request.GracePeriodDays;
        plan.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync(ct);

        var count = await dbContext.BuildingPlans
            .AsNoTracking()
            .CountAsync(x => !x.IsDeleted && !x.IsArchived && x.PlanId == id, ct);

        return Ok(ToDto(plan, count));
    }

    // ─── DELETE ──────────────────────────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var plan = await dbContext.Plans
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (plan is null) return NotFound();

        if (plan.IsDefault)
            return BadRequest("El plan gratuito por defecto no puede eliminarse.");

        if (plan.IsAssigned)
            return BadRequest("Este plan está asignado a uno o más edificios y no puede eliminarse. Puede desactivarlo si ya no desea utilizarlo.");

        plan.IsDeleted = true;
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─── CLONE ───────────────────────────────────────────────────────────────
    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<PlanCloneResult>> Clone(Guid id, CancellationToken ct)
    {
        if (!tenantContext.IsSuperAdmin) return Forbid();

        var source = await dbContext.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

        if (source is null) return NotFound();

        var clone = new Plan
        {
            Name = $"{source.Name} - copia",
            Description = source.Description,
            IsDefault = false,
            Price = source.Price,
            BillingCycle = source.BillingCycle,
            GracePeriodDays = source.GracePeriodDays,
            IsActive = true,
            IsAssigned = false
        };

        dbContext.Plans.Add(clone);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new PlanCloneResult
        {
            NewPlanId = clone.Id,
            NewPlanName = clone.Name
        });
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private static string? ValidateRequest(string name, decimal price, string billingCycle, int gracePeriodDays)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "El nombre del plan es obligatorio.";
        if (name.Trim().Length > 200)
            return "El nombre no puede superar los 200 caracteres.";
        if (price < 0)
            return "El precio no puede ser negativo.";
        if (string.IsNullOrWhiteSpace(billingCycle))
            return "El ciclo de facturación es obligatorio.";
        if (gracePeriodDays < 0 || gracePeriodDays > 365)
            return "El período de gracia debe estar entre 0 y 365 días.";
        return null;
    }

    private static PlanDto ToDto(Plan p, int assignedCount) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        IsDefault = p.IsDefault,
        Price = p.Price,
        BillingCycle = p.BillingCycle.ToString(),
        GracePeriodDays = p.GracePeriodDays,
        IsActive = p.IsActive,
        IsAssigned = p.IsAssigned,
        CreatedAtUtc = p.CreatedAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc,
        AssignedBuildingsCount = assignedCount
    };
}
