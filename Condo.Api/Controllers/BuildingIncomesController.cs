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
[Route("api/building-incomes")]
public class BuildingIncomesController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingIncomeDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? expensePeriodId,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.BuildingIncomes
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
            .OrderByDescending(x => x.IncomeDate)
            .ThenByDescending(x => x.ExpensePeriod!.Year)
            .ThenByDescending(x => x.ExpensePeriod!.Month)
            .ThenBy(x => x.Building!.Name)
            .ThenBy(x => x.Description)
            .Select(x => new BuildingIncomeDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                Category = x.Category,
                Description = x.Description,
                IncomeDate = x.IncomeDate,
                Amount = x.Amount,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildingIncomeDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new BuildingIncomeDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                Category = x.Category,
                Description = x.Description,
                IncomeDate = x.IncomeDate,
                Amount = x.Amount,
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
    public async Task<ActionResult<BuildingIncomeDto>> Create([FromBody] BuildingIncomeUpsertRequest request, CancellationToken cancellationToken)
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
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar ingresos.");
        }

        var entity = new BuildingIncome
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            ExpensePeriodId = request.ExpensePeriodId,
            Category = request.Category,
            Description = request.Description.Trim(),
            IncomeDate = request.IncomeDate,
            Amount = request.Amount,
            Notes = request.Notes.Trim()
        };

        dbContext.BuildingIncomes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, context.Building!, context.Period!));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BuildingIncomeDto>> Update(Guid id, [FromBody] BuildingIncomeUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.BuildingIncomes
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
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar ingresos.");
        }

        entity.CompanyId = effectiveCompanyId.Value;
        entity.BuildingId = request.BuildingId;
        entity.ExpensePeriodId = request.ExpensePeriodId;
        entity.Category = request.Category;
        entity.Description = request.Description.Trim();
        entity.IncomeDate = request.IncomeDate;
        entity.Amount = request.Amount;
        entity.Notes = request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity, context.Building!, context.Period!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.BuildingIncomes
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
            return BadRequest("Expense period not found.");
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Building incomes can only be deleted while the expense period is in draft status.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("rollover")]
    public async Task<ActionResult<RolloverIncomeResultDto>> Rollover([FromBody] RolloverIncomeRequest request, CancellationToken cancellationToken)
    {
        if (request.BuildingId == Guid.Empty || request.SourcePeriodId == Guid.Empty || request.TargetPeriodId == Guid.Empty)
        {
            return BadRequest("BuildingId, SourcePeriodId y TargetPeriodId son obligatorios.");
        }

        if (!await accessScope.CanAccessBuildingAsync(request.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var source = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.SourcePeriodId && x.BuildingId == request.BuildingId, cancellationToken);

        if (source is null)
        {
            return BadRequest("El periodo origen no existe o no pertenece al edificio indicado.");
        }

        var target = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.TargetPeriodId && x.BuildingId == request.BuildingId, cancellationToken);

        if (target is null)
        {
            return BadRequest("El periodo destino no existe o no pertenece al edificio indicado.");
        }

        if (target.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("El periodo destino debe estar en estado Borrador para recibir el saldo.");
        }

        if (request.SourcePeriodId == request.TargetPeriodId)
        {
            return BadRequest("El periodo origen y destino deben ser diferentes.");
        }

        var alreadyExists = await dbContext.BuildingIncomes
            .AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == request.TargetPeriodId
                && x.BuildingId == request.BuildingId
                && x.Category == BuildingIncomeCategory.AccumulatedBalance, cancellationToken);

        if (alreadyExists)
        {
            return Conflict("El periodo destino ya tiene un ingreso de tipo 'Saldo acumulado'. Elimínalo antes de aplicar el rollover.");
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return BadRequest("Edificio no encontrado.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada.");
        }

        var totalIngresos = await dbContext.BuildingIncomes
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == request.SourcePeriodId && x.BuildingId == request.BuildingId)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var totalGastos = await dbContext.BuildingExpenses
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == request.SourcePeriodId && x.BuildingId == request.BuildingId)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var saldo = totalIngresos - totalGastos;

        var result = new RolloverIncomeResultDto
        {
            SourcePeriodName = source.Name,
            TargetPeriodName = target.Name,
            TotalIngresos = totalIngresos,
            TotalGastos = totalGastos,
            Saldo = saldo,
            RolloverCreated = false
        };

        if (saldo <= 0)
        {
            return Ok(result);
        }

        var entity = new BuildingIncome
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            ExpensePeriodId = request.TargetPeriodId,
            Category = BuildingIncomeCategory.AccumulatedBalance,
            Description = $"Saldo anterior período {source.Name}",
            IncomeDate = target.StartDate,
            Amount = saldo,
            Notes = $"Rollover automático: ingresos {totalIngresos:N0} - gastos {totalGastos:N0}"
        };

        dbContext.BuildingIncomes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        result.RolloverCreated = true;
        result.CreatedIncome = ToDto(entity, building, target);

        return Ok(result);
    }

    private async Task<(ActionResult? Error, Building? Building, ExpensePeriod? Period)> ValidateContextAsync(
        BuildingIncomeUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var building = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.BuildingId, cancellationToken);

        if (building is null)
        {
            return (BadRequest("Building not found."), null, null);
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return (BadRequest("Expense period not found."), null, null);
        }

        if (period.BuildingId != request.BuildingId)
        {
            return (BadRequest("The expense period must belong to the selected building."), null, null);
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return (BadRequest("Building incomes can only be managed while the expense period is in draft status."), null, null);
        }

        return (null, building, period);
    }

    private static bool IsValidRequest(BuildingIncomeUpsertRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "BuildingId is required.";
            return false;
        }

        if (request.ExpensePeriodId == Guid.Empty)
        {
            error = "ExpensePeriodId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            error = "Description is required.";
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

    private static BuildingIncomeDto ToDto(BuildingIncome entity, Building building, ExpensePeriod period) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            BuildingId = entity.BuildingId,
            BuildingName = building.Name,
            ExpensePeriodId = entity.ExpensePeriodId,
            ExpensePeriodName = period.Name,
            Category = entity.Category,
            Description = entity.Description,
            IncomeDate = entity.IncomeDate,
            Amount = entity.Amount,
            Notes = entity.Notes
        };
}
