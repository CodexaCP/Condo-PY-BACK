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
    public async Task<ActionResult<IReadOnlyList<ExpenseChargeDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? expensePeriodId,
        [FromQuery] Guid? unitId,
        CancellationToken cancellationToken)
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

        if (buildingId.HasValue)
        {
            query = query.Where(x => x.Unit!.BuildingId == buildingId.Value);
        }

        if (expensePeriodId.HasValue)
        {
            query = query.Where(x => x.ExpensePeriodId == expensePeriodId.Value);
        }

        if (unitId.HasValue)
        {
            query = query.Where(x => x.UnitId == unitId.Value);
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
                SourceBuildingExpenseDescription = x.SourceBuildingExpense != null ? x.SourceBuildingExpense.Description : string.Empty,
                SourceSettlementId = x.SourceSettlementId,
                SourceSettlementName = x.SourceSettlement != null ? x.SourceSettlement.ExpensePeriod!.Name : string.Empty,
                IsLateFee = x.IsLateFee,
                Concept = x.Concept,
                Amount = x.Amount,
                Notes = x.Notes,
                IsReversal = x.IsReversal,
                ReversalOfChargeId = x.ReversalOfChargeId,
                IsReversed = dbContext.ExpenseCharges.Any(r => !r.IsDeleted && r.ReversalOfChargeId == x.Id),
                TotalAllocated = dbContext.PaymentAllocations.Where(a => !a.IsDeleted && a.ExpenseChargeId == x.Id).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m,
                PendingAmount = x.Amount - (dbContext.PaymentAllocations.Where(a => !a.IsDeleted && a.ExpenseChargeId == x.Id).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m)
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
                SourceBuildingExpenseDescription = x.SourceBuildingExpense != null ? x.SourceBuildingExpense.Description : string.Empty,
                SourceSettlementId = x.SourceSettlementId,
                SourceSettlementName = x.SourceSettlement != null ? x.SourceSettlement.ExpensePeriod!.Name : string.Empty,
                IsLateFee = x.IsLateFee,
                Concept = x.Concept,
                Amount = x.Amount,
                Notes = x.Notes,
                IsReversal = x.IsReversal,
                ReversalOfChargeId = x.ReversalOfChargeId,
                IsReversed = dbContext.ExpenseCharges.Any(r => !r.IsDeleted && r.ReversalOfChargeId == x.Id),
                TotalAllocated = dbContext.PaymentAllocations.Where(a => !a.IsDeleted && a.ExpenseChargeId == x.Id).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m,
                PendingAmount = x.Amount - (dbContext.PaymentAllocations.Where(a => !a.IsDeleted && a.ExpenseChargeId == x.Id).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m)
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
            return BadRequest("El periodo de expensas no existe.");
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los cargos solo se pueden crear mientras el periodo esta en borrador.");
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);

        if (unit is null)
        {
            return BadRequest("La unidad no existe.");
        }

        if (unit.BuildingId != period.BuildingId)
        {
            return BadRequest("La unidad debe pertenecer al mismo edificio que el periodo.");
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

        if (entity.IsReversal)
        {
            return BadRequest("Un cargo de reversión no se puede modificar directamente.");
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("El periodo de expensas no existe.");
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los cargos solo se pueden modificar mientras el periodo esta en borrador.");
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);

        if (unit is null)
        {
            return BadRequest("La unidad no existe.");
        }

        if (unit.BuildingId != period.BuildingId)
        {
            return BadRequest("La unidad debe pertenecer al mismo edificio que el periodo.");
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

        if (entity.IsReversal)
        {
            return BadRequest("Un cargo de reversión no se puede eliminar directamente.");
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("El periodo de expensas no existe.");
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los cargos solo se pueden eliminar mientras el periodo esta en borrador.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reverse")]
    public async Task<ActionResult<ExpenseChargeDto>> Reverse(Guid id, CancellationToken cancellationToken)
    {
        var original = await dbContext.ExpenseCharges
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (original is null)
        {
            return NotFound();
        }

        if (original.IsReversal)
        {
            return BadRequest("No se puede revertir un cargo que ya es una reversión.");
        }

        var alreadyReversed = await dbContext.ExpenseCharges
            .AnyAsync(x => !x.IsDeleted && x.ReversalOfChargeId == id, cancellationToken);

        if (alreadyReversed)
        {
            return BadRequest("Este cargo ya fue revertido.");
        }

        if (!await accessScope.CanAccessBuildingAsync(original.Unit!.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == original.ExpensePeriodId, cancellationToken);

        var reversal = new ExpenseCharge
        {
            CompanyId = original.CompanyId,
            ExpensePeriodId = original.ExpensePeriodId,
            UnitId = original.UnitId,
            ChargeType = ExpenseChargeType.Adjustment,
            IsLateFee = false,
            Concept = $"Reversión: {original.Concept}",
            Amount = -original.Amount,
            Notes = $"Reversión del cargo #{original.Id}",
            IsReversal = true,
            ReversalOfChargeId = original.Id
        };

        dbContext.ExpenseCharges.Add(reversal);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(reversal, period?.Name ?? string.Empty, original.Unit!));
    }

    private static bool IsValidRequest(ExpenseChargeUpsertRequest request, out string error)
    {
        if (request.ExpensePeriodId == Guid.Empty)
        {
            error = "El periodo es obligatorio.";
            return false;
        }

        if (request.UnitId == Guid.Empty)
        {
            error = "La unidad es obligatoria.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Concept))
        {
            error = "El concepto es obligatorio.";
            return false;
        }

        if (request.Concept.Trim().Length > 200)
        {
            error = "El concepto no puede superar los 200 caracteres.";
            return false;
        }

        if (request.Amount <= 0)
        {
            error = "El monto debe ser mayor que cero.";
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
            SourceBuildingExpenseDescription = string.Empty,
            SourceSettlementId = entity.SourceSettlementId,
            SourceSettlementName = string.Empty,
            IsLateFee = entity.IsLateFee,
            Concept = entity.Concept,
            Amount = entity.Amount,
            Notes = entity.Notes,
            IsReversal = entity.IsReversal,
            ReversalOfChargeId = entity.ReversalOfChargeId,
            IsReversed = false
        };
}
