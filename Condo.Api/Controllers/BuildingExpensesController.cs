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
[Route("api/building-expenses")]
public class BuildingExpensesController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingExpenseDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? expensePeriodId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.BuildingExpenses
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
                query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
            }
        }

        if (buildingId.HasValue)
        {
            query = query.Where(x => x.BuildingId == buildingId.Value);
        }

        if (expensePeriodId.HasValue)
        {
            query = query.Where(x => x.ExpensePeriodId == expensePeriodId.Value);
        }

        var items = await query
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.ExpensePeriod!.Year)
            .ThenByDescending(x => x.ExpensePeriod!.Month)
            .ThenBy(x => x.Building!.Name)
            .ThenBy(x => x.Description)
            .Select(x => new BuildingExpenseDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                Category = x.Category,
                SupplierName = x.SupplierName,
                Description = x.Description,
                ExpenseDate = x.ExpenseDate,
                Amount = x.Amount,
                DistributionType = x.DistributionType,
                TargetUnitId = x.TargetUnitId,
                TargetUnitCode = x.TargetUnit != null ? x.TargetUnit.Code : string.Empty,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildingExpenseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new BuildingExpenseDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                Category = x.Category,
                SupplierName = x.SupplierName,
                Description = x.Description,
                ExpenseDate = x.ExpenseDate,
                Amount = x.Amount,
                DistributionType = x.DistributionType,
                TargetUnitId = x.TargetUnitId,
                TargetUnitCode = x.TargetUnit != null ? x.TargetUnit.Code : string.Empty,
                Notes = x.Notes
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(item.BuildingId, cancellationToken) ? Ok(item) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<BuildingExpenseDto>> Create([FromBody] BuildingExpenseUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var context = await ValidateContextAsync(request, cancellationToken);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!await accessScope.CanAccessBuildingAsync(request.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var effectiveCompanyId = context.Building!.CompanyId ?? context.Building!.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar gastos.");
        }

        var entity = new BuildingExpense
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            ExpensePeriodId = request.ExpensePeriodId,
            Category = request.Category,
            SupplierName = request.SupplierName.Trim(),
            Description = request.Description.Trim(),
            ExpenseDate = request.ExpenseDate,
            Amount = request.Amount,
            DistributionType = request.DistributionType,
            TargetUnitId = request.DistributionType == BuildingExpenseDistributionType.IndividualUnit ? request.TargetUnitId : null,
            Notes = request.Notes.Trim()
        };

        dbContext.BuildingExpenses.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, context.Building!, context.Period!, context.TargetUnit));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BuildingExpenseDto>> Update(Guid id, [FromBody] BuildingExpenseUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var context = await ValidateContextAsync(request, cancellationToken);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!await accessScope.CanAccessBuildingAsync(request.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var effectiveCompanyId = context.Building!.CompanyId ?? context.Building!.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar gastos.");
        }

        entity.CompanyId = effectiveCompanyId.Value;
        entity.BuildingId = request.BuildingId;
        entity.ExpensePeriodId = request.ExpensePeriodId;
        entity.Category = request.Category;
        entity.SupplierName = request.SupplierName.Trim();
        entity.Description = request.Description.Trim();
        entity.ExpenseDate = request.ExpenseDate;
        entity.Amount = request.Amount;
        entity.DistributionType = request.DistributionType;
        entity.TargetUnitId = request.DistributionType == BuildingExpenseDistributionType.IndividualUnit ? request.TargetUnitId : null;
        entity.Notes = request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity, context.Building!, context.Period!, context.TargetUnit));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return BadRequest("El periodo de expensas asociado no existe.");
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los gastos del edificio solo se pueden eliminar mientras el periodo este en borrador.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(ActionResult? Error, Building? Building, ExpensePeriod? Period, Unit? TargetUnit)> ValidateContextAsync(
        BuildingExpenseUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return (BadRequest("El edificio indicado no existe."), null, null, null);
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return (BadRequest("El periodo de expensas indicado no existe."), null, null, null);
        }

        if (period.BuildingId != request.BuildingId)
        {
            return (BadRequest("El periodo de expensas debe pertenecer al edificio seleccionado."), null, null, null);
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return (BadRequest("Los gastos del edificio solo se pueden gestionar mientras el periodo este en borrador."), null, null, null);
        }

        if (request.ExpenseDate < period.StartDate || request.ExpenseDate > period.EndDate)
        {
            return (BadRequest("La fecha del gasto debe estar dentro del rango del periodo seleccionado."), null, null, null);
        }

        Unit? targetUnit = null;
        if (request.TargetUnitId.HasValue)
        {
            targetUnit = await dbContext.Units
                .AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Id == request.TargetUnitId.Value, cancellationToken);

            if (targetUnit is null)
            {
                return (BadRequest("La unidad destino no existe o no esta activa."), null, null, null);
            }

            if (targetUnit.BuildingId != request.BuildingId)
            {
                return (BadRequest("La unidad destino debe pertenecer al edificio seleccionado."), null, null, null);
            }
        }

        return (null, building, period, targetUnit);
    }

    private static bool IsValidRequest(BuildingExpenseUpsertRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "El edificio es obligatorio.";
            return false;
        }

        if (request.ExpensePeriodId == Guid.Empty)
        {
            error = "El periodo es obligatorio.";
            return false;
        }

        if (request.DistributionType == BuildingExpenseDistributionType.ManualGroup)
        {
            error = "La distribucion ManualGroup todavia no esta disponible como flujo operativo.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            error = "La descripcion es obligatoria.";
            return false;
        }

        if (request.Description.Trim().Length > 200)
        {
            error = "La descripcion no puede superar los 200 caracteres.";
            return false;
        }

        if (request.SupplierName.Trim().Length > 160)
        {
            error = "El proveedor no puede superar los 160 caracteres.";
            return false;
        }

        if (request.Notes.Trim().Length > 500)
        {
            error = "Las notas no pueden superar los 500 caracteres.";
            return false;
        }

        if (request.Amount <= 0)
        {
            error = "El monto debe ser mayor que cero.";
            return false;
        }

        if (request.DistributionType == BuildingExpenseDistributionType.IndividualUnit && !request.TargetUnitId.HasValue)
        {
            error = "La unidad destino es obligatoria cuando la distribucion es por unidad individual.";
            return false;
        }

        if (request.DistributionType != BuildingExpenseDistributionType.IndividualUnit && request.TargetUnitId.HasValue)
        {
            error = "La unidad destino solo se permite cuando la distribucion es por unidad individual.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static BuildingExpenseDto ToDto(BuildingExpense entity, Building building, ExpensePeriod period, Unit? targetUnit) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            BuildingId = entity.BuildingId,
            BuildingName = building.Name,
            ExpensePeriodId = entity.ExpensePeriodId,
            ExpensePeriodName = period.Name,
            Category = entity.Category,
            SupplierName = entity.SupplierName,
            Description = entity.Description,
            ExpenseDate = entity.ExpenseDate,
            Amount = entity.Amount,
            DistributionType = entity.DistributionType,
            TargetUnitId = entity.TargetUnitId,
            TargetUnitCode = targetUnit?.Code ?? string.Empty,
            Notes = entity.Notes
        };
}
