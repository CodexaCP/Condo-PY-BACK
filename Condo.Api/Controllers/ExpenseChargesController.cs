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
[Route("api/expense-charges")]
public class ExpenseChargesController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpenseChargeDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.ExpenseCharges
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

        var charges = await query
            .OrderByDescending(x => x.ExpensePeriod!.Year)
            .ThenByDescending(x => x.ExpensePeriod!.Month)
            .ThenBy(x => x.Unit!.Code)
            .ThenBy(x => x.Concept)
            .Select(x => new ExpenseChargeDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
                BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
                UnitId = x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                ChargeType = x.ChargeType,
                SourceBuildingExpenseId = x.SourceBuildingExpenseId,
                SourceSettlementId = x.SourceSettlementId,
                IsLateFee = x.IsLateFee,
                Concept = x.Concept,
                Amount = x.Amount,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(charges);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseChargeDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var charge = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new ExpenseChargeDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                BuildingId = x.Unit != null ? x.Unit.BuildingId : Guid.Empty,
                BuildingName = x.Unit != null && x.Unit.Building != null ? x.Unit.Building.Name : string.Empty,
                UnitId = x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                ChargeType = x.ChargeType,
                SourceBuildingExpenseId = x.SourceBuildingExpenseId,
                SourceSettlementId = x.SourceSettlementId,
                IsLateFee = x.IsLateFee,
                Concept = x.Concept,
                Amount = x.Amount,
                Notes = x.Notes
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (charge is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(charge.BuildingId, cancellationToken) ? Ok(charge) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseChargeDto>> Create([FromBody] ExpenseChargeUpsertRequest request, CancellationToken cancellationToken)
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

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Charges can only be created while the expense period is in draft status.");
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

        var entity = new ExpenseCharge
        {
            CompanyId = unit.CompanyId,
            ExpensePeriodId = request.ExpensePeriodId,
            UnitId = request.UnitId,
            ChargeType = request.ChargeType,
            IsLateFee = request.IsLateFee,
            Concept = request.Concept.Trim(),
            Amount = request.Amount,
            Notes = request.Notes.Trim()
        };

        dbContext.ExpenseCharges.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, period.Name, unit));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpenseChargeDto>> Update(Guid id, [FromBody] ExpenseChargeUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.ExpenseCharges
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

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Charges can only be modified while the expense period is in draft status.");
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
        entity.ChargeType = request.ChargeType;
        entity.IsLateFee = request.IsLateFee;
        entity.Concept = request.Concept.Trim();
        entity.Amount = request.Amount;
        entity.Notes = request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity, period.Name, unit));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.ExpenseCharges
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("Expense period not found.");
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Charges can only be deleted while the expense period is in draft status.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static bool IsValidRequest(ExpenseChargeUpsertRequest request, out string error)
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

        if (string.IsNullOrWhiteSpace(request.Concept))
        {
            error = "Concept is required.";
            return false;
        }

        if (request.Amount <= 0)
        {
            error = "Amount must be greater than zero.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static ExpenseChargeDto ToDto(ExpenseCharge entity, string expensePeriodName, Unit unit) =>
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
            ChargeType = entity.ChargeType,
            SourceBuildingExpenseId = entity.SourceBuildingExpenseId,
            SourceSettlementId = entity.SourceSettlementId,
            IsLateFee = entity.IsLateFee,
            Concept = entity.Concept,
            Amount = entity.Amount,
            Notes = entity.Notes
        };
}
