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
[Route("api/recurring-building-expenses")]
public class RecurringBuildingExpensesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecurringBuildingExpenseDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.RecurringBuildingExpenses
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

        var items = await query
            .OrderBy(x => x.Building!.Name)
            .ThenBy(x => x.Description)
            .Select(x => new RecurringBuildingExpenseDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Category = x.Category,
                SupplierName = x.SupplierName,
                Description = x.Description,
                Amount = x.Amount,
                DistributionType = x.DistributionType,
                TargetUnitId = x.TargetUnitId,
                TargetUnitCode = x.TargetUnit != null ? x.TargetUnit.Code : string.Empty,
                Notes = x.Notes,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RecurringBuildingExpenseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.RecurringBuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new RecurringBuildingExpenseDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Category = x.Category,
                SupplierName = x.SupplierName,
                Description = x.Description,
                Amount = x.Amount,
                DistributionType = x.DistributionType,
                TargetUnitId = x.TargetUnitId,
                TargetUnitCode = x.TargetUnit != null ? x.TargetUnit.Code : string.Empty,
                Notes = x.Notes,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(item.BuildingId, cancellationToken) ? Ok(item) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<RecurringBuildingExpenseDto>> Create(
        [FromBody] RecurringBuildingExpenseUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var error))
        {
            return BadRequest(error);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return BadRequest("El edificio indicado no existe.");
        }

        if (!await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
        {
            return Forbid();
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada.");
        }

        Unit? targetUnit = null;
        if (request.TargetUnitId.HasValue)
        {
            targetUnit = await dbContext.Units
                .AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Id == request.TargetUnitId.Value, cancellationToken);

            if (targetUnit is null || targetUnit.BuildingId != request.BuildingId)
            {
                return BadRequest("La unidad destino no existe, no esta activa o no pertenece al edificio.");
            }
        }

        var entity = new RecurringBuildingExpense
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            Category = request.Category,
            SupplierName = request.SupplierName.Trim(),
            Description = request.Description.Trim(),
            Amount = request.Amount,
            DistributionType = request.DistributionType,
            TargetUnitId = request.DistributionType == BuildingExpenseDistributionType.IndividualUnit ? request.TargetUnitId : null,
            Notes = request.Notes.Trim(),
            IsActive = request.IsActive
        };

        dbContext.RecurringBuildingExpenses.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, building.Name, targetUnit));
    }

    // Crea la misma plantilla para los edificios elegidos (o todos los accesibles, si no se elige ninguno
    // puntual), una fila por edificio. No disponible para distribucion por unidad individual (la unidad es
    // especifica de un solo edificio).
    [HttpPost("create-for-all")]
    public async Task<ActionResult<IReadOnlyList<RecurringBuildingExpenseDto>>> CreateForAll(
        [FromBody] RecurringBuildingExpenseCreateForAllRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DistributionType == BuildingExpenseDistributionType.IndividualUnit)
        {
            return BadRequest("La distribucion por unidad individual no esta disponible para varios edificios: elegi un edificio puntual.");
        }

        if (!IsValidCreateForAllRequest(request, out var error))
        {
            return BadRequest(error);
        }

        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
        var targetIds = request.BuildingIds.Count > 0
            ? request.BuildingIds.Where(id => accessibleBuildingIds.Contains(id)).ToList()
            : accessibleBuildingIds.ToList();

        if (targetIds.Count == 0)
        {
            return BadRequest("No hay edificios accesibles para crear la plantilla.");
        }

        var buildings = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .Where(x => !x.IsDeleted && targetIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (buildings.Count == 0)
        {
            return BadRequest("No hay edificios accesibles para crear la plantilla.");
        }

        var created = new List<RecurringBuildingExpenseDto>();
        foreach (var building in buildings)
        {
            var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
            if (!effectiveCompanyId.HasValue) continue;

            var entity = new RecurringBuildingExpense
            {
                CompanyId = effectiveCompanyId.Value,
                BuildingId = building.Id,
                Category = request.Category,
                SupplierName = request.SupplierName.Trim(),
                Description = request.Description.Trim(),
                Amount = request.Amount,
                DistributionType = request.DistributionType,
                TargetUnitId = null,
                Notes = request.Notes.Trim(),
                IsActive = request.IsActive
            };
            dbContext.RecurringBuildingExpenses.Add(entity);
            created.Add(ToDto(entity, building.Name, null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RecurringBuildingExpenseDto>> Update(
        Guid id,
        [FromBody] RecurringBuildingExpenseUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var error))
        {
            return BadRequest(error);
        }

        var entity = await dbContext.RecurringBuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return BadRequest("El edificio indicado no existe.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada.");
        }

        Unit? targetUnit = null;
        if (request.TargetUnitId.HasValue)
        {
            targetUnit = await dbContext.Units
                .AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Id == request.TargetUnitId.Value, cancellationToken);

            if (targetUnit is null || targetUnit.BuildingId != request.BuildingId)
            {
                return BadRequest("La unidad destino no existe, no esta activa o no pertenece al edificio.");
            }
        }

        entity.CompanyId = effectiveCompanyId.Value;
        entity.BuildingId = request.BuildingId;
        entity.Category = request.Category;
        entity.SupplierName = request.SupplierName.Trim();
        entity.Description = request.Description.Trim();
        entity.Amount = request.Amount;
        entity.DistributionType = request.DistributionType;
        entity.TargetUnitId = request.DistributionType == BuildingExpenseDistributionType.IndividualUnit ? request.TargetUnitId : null;
        entity.Notes = request.Notes.Trim();
        entity.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity, building.Name, targetUnit));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.RecurringBuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("apply")]
    public async Task<ActionResult<ApplyRecurringExpensesResultDto>> Apply(
        [FromBody] ApplyRecurringExpensesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExpensePeriodId == Guid.Empty)
        {
            return BadRequest("El periodo es obligatorio.");
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return NotFound("El periodo indicado no existe.");
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los gastos recurrentes solo se pueden aplicar mientras el periodo este en borrador.");
        }

        var templates = await dbContext.RecurringBuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.BuildingId == period.BuildingId)
            .ToListAsync(cancellationToken);

        if (templates.Count == 0)
        {
            return BadRequest("Este edificio no tiene gastos recurrentes activos configurados.");
        }

        var result = new ApplyRecurringExpensesResultDto
        {
            ExpensePeriodName = period.Name
        };

        var expenseDate = period.StartDate;
        var newExpenses = new List<BuildingExpense>();

        foreach (var template in templates)
        {
            newExpenses.Add(new BuildingExpense
            {
                CompanyId = period.CompanyId,
                BuildingId = period.BuildingId,
                ExpensePeriodId = period.Id,
                Category = template.Category,
                SupplierName = template.SupplierName,
                Description = template.Description,
                ExpenseDate = expenseDate,
                Amount = template.Amount,
                DistributionType = template.DistributionType,
                TargetUnitId = template.TargetUnitId,
                Notes = template.Notes
            });
            result.AppliedDescriptions.Add(template.Description);
        }

        dbContext.BuildingExpenses.AddRange(newExpenses);
        await dbContext.SaveChangesAsync(cancellationToken);

        result.Applied = newExpenses.Count;
        return Ok(result);
    }

    private static bool IsValidRequest(RecurringBuildingExpenseUpsertRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "El edificio es obligatorio.";
            return false;
        }

        if (request.DistributionType == BuildingExpenseDistributionType.ManualGroup)
        {
            error = "La distribucion ManualGroup todavia no esta disponible.";
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

        error = string.Empty;
        return true;
    }

    private static bool IsValidCreateForAllRequest(RecurringBuildingExpenseCreateForAllRequest request, out string error)
    {
        if (request.DistributionType == BuildingExpenseDistributionType.ManualGroup)
        {
            error = "La distribucion ManualGroup todavia no esta disponible.";
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

        error = string.Empty;
        return true;
    }

    private static RecurringBuildingExpenseDto ToDto(
        RecurringBuildingExpense entity,
        string buildingName,
        Unit? targetUnit) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            BuildingId = entity.BuildingId,
            BuildingName = buildingName,
            Category = entity.Category,
            SupplierName = entity.SupplierName,
            Description = entity.Description,
            Amount = entity.Amount,
            DistributionType = entity.DistributionType,
            TargetUnitId = entity.TargetUnitId,
            TargetUnitCode = targetUnit?.Code ?? string.Empty,
            Notes = entity.Notes,
            IsActive = entity.IsActive
        };
}
