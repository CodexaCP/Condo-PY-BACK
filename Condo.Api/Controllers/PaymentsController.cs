using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                query = query.Where(x => accessibleBuildingIds.Contains(x.Unit!.BuildingId));
            }
        }

        var payments = await query
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.ExpensePeriod!.Year)
            .ThenByDescending(x => x.ExpensePeriod!.Month)
            .ThenBy(x => x.Unit!.Code)
            .Select(x => new PaymentDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
                BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
                UnitId = x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                PaymentDate = x.PaymentDate,
                Amount = x.Amount,
                Method = x.Method,
                Reference = x.Reference,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(payments);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new PaymentDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
                BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
                UnitId = x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                PaymentDate = x.PaymentDate,
                Amount = x.Amount,
                Method = x.Method,
                Reference = x.Reference,
                Notes = x.Notes
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(payment.BuildingId, cancellationToken) ? Ok(payment) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<PaymentDto>> Create([FromBody] PaymentUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("Expense period not found.");
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);

        if (unit is null)
        {
            return BadRequest("Unit not found.");
        }

        if (unit.BuildingId != period.BuildingId)
        {
            return BadRequest("The unit must belong to the same building as the expense period.");
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var entity = new Payment
        {
            CompanyId = unit.CompanyId,
            ExpensePeriodId = request.ExpensePeriodId,
            UnitId = request.UnitId,
            PaymentDate = request.PaymentDate,
            Amount = request.Amount,
            Method = request.Method,
            Reference = request.Reference.Trim(),
            Notes = request.Notes.Trim()
        };

        dbContext.Payments.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, period.Name, unit));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PaymentDto>> Update(Guid id, [FromBody] PaymentUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.Payments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("Expense period not found.");
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);

        if (unit is null)
        {
            return BadRequest("Unit not found.");
        }

        if (unit.BuildingId != period.BuildingId)
        {
            return BadRequest("The unit must belong to the same building as the expense period.");
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        entity.CompanyId = unit.CompanyId;
        entity.ExpensePeriodId = request.ExpensePeriodId;
        entity.UnitId = request.UnitId;
        entity.PaymentDate = request.PaymentDate;
        entity.Amount = request.Amount;
        entity.Method = request.Method;
        entity.Reference = request.Reference.Trim();
        entity.Notes = request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity, period.Name, unit));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Payments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.UnitId, cancellationToken);

        if (unit is null)
        {
            return BadRequest("Unit not found.");
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static bool IsValidRequest(PaymentUpsertRequest request, out string error)
    {
        if (request.ExpensePeriodId == Guid.Empty)
        {
            error = "ExpensePeriodId is required.";
            return false;
        }

        if (request.UnitId == Guid.Empty)
        {
            error = "UnitId is required.";
            return false;
        }

        if (request.Amount <= 0)
        {
            error = "Amount must be greater than zero.";
            return false;
        }

        if (request.PaymentDate == default)
        {
            error = "PaymentDate is required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static PaymentDto ToDto(Payment entity, string expensePeriodName, Unit unit) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            ExpensePeriodId = entity.ExpensePeriodId,
            ExpensePeriodName = expensePeriodName,
            BuildingId = unit.BuildingId,
            BuildingName = unit.Building?.Name ?? string.Empty,
            UnitId = entity.UnitId,
            UnitCode = unit.Code,
            PaymentDate = entity.PaymentDate,
            Amount = entity.Amount,
            Method = entity.Method,
            Reference = entity.Reference,
            Notes = entity.Notes
        };
}
