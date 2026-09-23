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
    private const string GeneralBuildingName = "Todos los edificios";

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
                // Ademas de sus edificios, un BuildingManager tiene que ver las plantillas generales
                // de su empresa (BuildingId null), que tambien le aplican.
                var companyId = accessScope.CompanyId;
                query = query.Where(x =>
                    accessibleBuildingIds.Contains(x.BuildingId!.Value) ||
                    (x.BuildingId == null && companyId.HasValue && x.CompanyId == companyId.Value));
            }
        }

        if (buildingId.HasValue)
        {
            // Filtrar "por este edificio" muestra tambien las generales, porque esas tambien le aplican.
            var bId = buildingId.Value;
            query = query.Where(x => x.BuildingId == bId || x.BuildingId == null);
        }

        var items = await query
            .OrderBy(x => x.Building != null ? x.Building.Name : string.Empty)
            .ThenBy(x => x.Description)
            .Select(x => new RecurringBuildingExpenseDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : GeneralBuildingName,
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
                BuildingName = x.Building != null ? x.Building.Name : GeneralBuildingName,
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

        return await CanAccessAsync(item.BuildingId, item.CompanyId, cancellationToken) ? Ok(item) : Forbid();
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

        Building? building = null;
        Guid effectiveCompanyId;

        if (request.BuildingId.HasValue)
        {
            building = await dbContext.Buildings
                .AsNoTracking()
                .Include(x => x.Condominium)
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId.Value, cancellationToken);

            if (building is null)
            {
                return BadRequest("El edificio indicado no existe.");
            }

            if (!await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
            {
                return Forbid();
            }

            var ec = building.CompanyId ?? building.Condominium?.CompanyId;
            if (!ec.HasValue)
            {
                return BadRequest("El edificio no tiene empresa asignada.");
            }
            effectiveCompanyId = ec.Value;
        }
        else
        {
            // Plantilla general (sin edificio): se guarda contra la empresa de quien la crea.
            if (!accessScope.CompanyId.HasValue)
            {
                return BadRequest("No se pudo determinar la empresa.");
            }
            effectiveCompanyId = accessScope.CompanyId.Value;
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
            CompanyId = effectiveCompanyId,
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

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, building?.Name ?? GeneralBuildingName, targetUnit));
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

        if (!await CanAccessAsync(entity.BuildingId, entity.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        Building? building = null;
        Guid effectiveCompanyId;

        if (request.BuildingId.HasValue)
        {
            building = await dbContext.Buildings
                .AsNoTracking()
                .Include(x => x.Condominium)
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId.Value, cancellationToken);

            if (building is null)
            {
                return BadRequest("El edificio indicado no existe.");
            }

            if (!await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
            {
                return Forbid();
            }

            var ec = building.CompanyId ?? building.Condominium?.CompanyId;
            if (!ec.HasValue)
            {
                return BadRequest("El edificio no tiene empresa asignada.");
            }
            effectiveCompanyId = ec.Value;
        }
        else
        {
            if (!accessScope.CompanyId.HasValue && !accessScope.IsSuperAdmin)
            {
                return BadRequest("No se pudo determinar la empresa.");
            }
            effectiveCompanyId = accessScope.CompanyId ?? entity.CompanyId;
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

        entity.CompanyId = effectiveCompanyId;
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
        return Ok(ToDto(entity, building?.Name ?? GeneralBuildingName, targetUnit));
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

        if (!await CanAccessAsync(entity.BuildingId, entity.CompanyId, cancellationToken))
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

        // Se aplican las plantillas de ese edificio puntual y tambien las generales (sin edificio) de la empresa.
        var templates = await dbContext.RecurringBuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive
                        && (x.BuildingId == period.BuildingId || (x.BuildingId == null && x.CompanyId == period.CompanyId)))
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

    // Una plantilla sin edificio (general) no tiene un edificio puntual contra el cual chequear acceso;
    // se autoriza por empresa en su lugar (o SuperAdmin, que ve todo).
    private async Task<bool> CanAccessAsync(Guid? buildingId, Guid companyId, CancellationToken cancellationToken)
    {
        if (buildingId.HasValue)
        {
            return await accessScope.CanAccessBuildingAsync(buildingId.Value, cancellationToken);
        }

        return accessScope.IsSuperAdmin || accessScope.CompanyId == companyId;
    }

    private static bool IsValidRequest(RecurringBuildingExpenseUpsertRequest request, out string error)
    {
        if (request.DistributionType == BuildingExpenseDistributionType.ManualGroup)
        {
            error = "La distribucion ManualGroup todavia no esta disponible.";
            return false;
        }

        if (request.DistributionType == BuildingExpenseDistributionType.IndividualUnit && !request.BuildingId.HasValue)
        {
            error = "La distribucion por unidad individual no esta disponible para una plantilla general: elegi un edificio puntual.";
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
