using Condo.Api.Documents;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/expense-periods")]
public class ExpensePeriodsController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    IExpenseSettlementDistributionService distributionService) : ControllerBase
{
    private const decimal CoefficientDistributionExpectedTotal = 1.00m;
    private const decimal CoefficientDistributionTolerance = 0.0001m;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpensePeriodDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.ExpensePeriods
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

        var periods = await query
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ThenBy(x => x.Building != null ? x.Building.Name : string.Empty)
            .Select(x => new ExpensePeriodDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Year = x.Year,
                Month = x.Month,
                Name = x.Name,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                DueDate = x.DueDate,
                LateFeeDate = x.LateFeeDate,
                Status = x.Status,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(periods);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpensePeriodDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new ExpensePeriodDto
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                BuildingId = x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                Year = x.Year,
                Month = x.Month,
                Name = x.Name,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                DueDate = x.DueDate,
                LateFeeDate = x.LateFeeDate,
                Status = x.Status,
                Notes = x.Notes
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        return await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken) ? Ok(period) : Forbid();
    }

    [HttpPost]
    public async Task<ActionResult<ExpensePeriodDto>> Create([FromBody] ExpensePeriodUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
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

        var exists = await dbContext.ExpensePeriods.AnyAsync(
            x => !x.IsDeleted &&
                x.BuildingId == request.BuildingId &&
                x.Year == request.Year &&
                x.Month == request.Month,
            cancellationToken);

        if (exists)
        {
            return Conflict("Ya existe un periodo de expensas para ese edificio, anio y mes.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar periodos.");
        }

        var entity = new ExpensePeriod
        {
            CompanyId = effectiveCompanyId.Value,
            BuildingId = request.BuildingId,
            Year = request.Year,
            Month = request.Month,
            Name = ResolveName(request),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DueDate = request.DueDate,
            LateFeeDate = request.LateFeeDate,
            Status = ExpensePeriodStatus.Draft,
            Notes = request.Notes.Trim()
        };

        dbContext.ExpensePeriods.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity, building.Name));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpensePeriodDto>> Update(Guid id, [FromBody] ExpensePeriodUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationError))
        {
            return BadRequest(validationError);
        }

        var entity = await dbContext.ExpensePeriods
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (entity.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Solo se pueden modificar periodos en borrador.");
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

        var duplicateExists = await dbContext.ExpensePeriods.AnyAsync(
            x => !x.IsDeleted &&
                x.Id != id &&
                x.BuildingId == request.BuildingId &&
                x.Year == request.Year &&
                x.Month == request.Month,
            cancellationToken);

        if (duplicateExists)
        {
            return Conflict("Ya existe un periodo de expensas para ese edificio, anio y mes.");
        }

        var isStructuralChange =
            entity.BuildingId != request.BuildingId ||
            entity.Year != request.Year ||
            entity.Month != request.Month;

        if (isStructuralChange && await HasOperationalDependenciesAsync(id, cancellationToken))
        {
            return BadRequest("No se puede cambiar edificio, anio o mes porque el periodo ya tiene movimientos asociados.");
        }

        var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!effectiveCompanyId.HasValue)
        {
            return BadRequest("El edificio no tiene empresa asignada. Asigne una empresa o condominio antes de gestionar periodos.");
        }

        entity.CompanyId = effectiveCompanyId.Value;
        entity.BuildingId = request.BuildingId;
        entity.Year = request.Year;
        entity.Month = request.Month;
        entity.Name = ResolveName(request);
        entity.StartDate = request.StartDate;
        entity.EndDate = request.EndDate;
        entity.DueDate = request.DueDate;
        entity.LateFeeDate = request.LateFeeDate;
        entity.Status = ExpensePeriodStatus.Draft;
        entity.Notes = request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity, building.Name));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.ExpensePeriods
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(entity.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (entity.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Solo se pueden eliminar periodos en borrador.");
        }

        if (await HasOperationalDependenciesAsync(id, cancellationToken))
        {
            return BadRequest("No se puede eliminar el periodo porque ya tiene movimientos asociados.");
        }

        entity.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/generate-charges")]
    public async Task<ActionResult<GenerateExpenseChargesResultDto>> GenerateCharges(
        Guid id,
        [FromBody] GenerateExpenseChargesRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidGenerationRequest(request, out var generationError))
        {
            return BadRequest(generationError);
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Los cargos solo se pueden generar mientras el periodo este en borrador.");
        }

        var existingCharges = await dbContext.ExpenseCharges
            .AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (existingCharges)
        {
            return BadRequest("Este periodo ya tiene cargos. Debes eliminarlos antes de ejecutar una generacion masiva.");
        }

        var units = await dbContext.Units
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.BuildingId == period.BuildingId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        if (units.Count == 0)
        {
            return BadRequest("No hay unidades activas disponibles para este edificio.");
        }

        if (request.Mode.Equals("ByCoefficient", StringComparison.OrdinalIgnoreCase))
        {
            var totalCoefficient = units.Sum(x => x.Coefficient);
            if (!HasValidCoefficientBase(totalCoefficient))
            {
                return BadRequest($"No se puede generar por coeficiente porque la suma de coeficientes del edificio debe ser {CoefficientDistributionExpectedTotal:0.####} y actualmente es {totalCoefficient:0.####}.");
            }
        }

        var charges = request.Mode.Equals("ByCoefficient", StringComparison.OrdinalIgnoreCase)
            ? GenerateByCoefficient(period.CompanyId, id, units, request)
            : GenerateFixedAmount(period.CompanyId, id, units, request);

        dbContext.ExpenseCharges.AddRange(charges);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new GenerateExpenseChargesResultDto
        {
            ExpensePeriodId = period.Id,
            ExpensePeriodName = period.Name,
            UnitsAffected = charges.Count,
            TotalGeneratedAmount = charges.Sum(x => x.Amount),
            Mode = request.Mode
        });
    }

    [HttpGet("{id:guid}/settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> GetSettlement(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpPost("{id:guid}/calculate-settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> CalculateSettlement(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("La liquidacion solo se puede calcular mientras el periodo este en borrador.");
        }

        var existingSettlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (existingSettlement is not null &&
            existingSettlement.Status is ExpenseSettlementStatus.Approved or ExpenseSettlementStatus.Applied)
        {
            return BadRequest("No se puede recalcular una liquidacion aprobada o aplicada.");
        }

        var summary = await BuildLiveSettlementSummaryAsync(period, cancellationToken);

        if (existingSettlement is null)
        {
            existingSettlement = new ExpenseSettlement
            {
                CompanyId = period.CompanyId,
                ExpensePeriodId = period.Id,
                BuildingId = period.BuildingId
            };

            dbContext.ExpenseSettlements.Add(existingSettlement);
        }

        existingSettlement.CompanyId = period.CompanyId;
        existingSettlement.ExpensePeriodId = period.Id;
        existingSettlement.BuildingId = period.BuildingId;
        existingSettlement.TotalBuildingExpenses = summary.TotalBuildingExpenses;
        existingSettlement.TotalBuildingIncomes = summary.TotalBuildingIncomes;
        existingSettlement.ReserveFundAmount = summary.ReserveFundAmount;
        existingSettlement.ExtraordinaryAmount = summary.ExtraordinaryAmount;
        existingSettlement.NetCommonAmount = summary.NetCommonAmount;
        existingSettlement.GeneratedAtUtc = DateTime.UtcNow;
        existingSettlement.GeneratedByUserId = tenantContext.UserId;
        existingSettlement.Status = ExpenseSettlementStatus.Calculated;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpGet("{id:guid}/settlement-charge-preview")]
    public async Task<ActionResult<ExpenseSettlementChargePreviewDto>> GetSettlementChargePreview(Guid id, CancellationToken cancellationToken)
    {
        var context = await ValidateSettlementDistributionContextAsync(id, requireGenerationReady: false, cancellationToken);
        if (context.Error is not null)
        {
            return context.Error;
        }

        try
        {
            return Ok(await distributionService.PreviewAsync(context.Period!, context.Settlement!, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost("{id:guid}/generate-settlement-charges")]
    public Task<ActionResult<ExpenseSettlementChargePreviewDto>> GenerateSettlementCharges(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<ActionResult<ExpenseSettlementChargePreviewDto>>(BadRequest("La emision de cargos ahora ocurre al aprobar la liquidacion."));

    [HttpPost("{id:guid}/approve-settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> ApproveSettlement(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null)
        {
            return BadRequest("El periodo todavia no tiene una liquidacion calculada.");
        }

        if (settlement.Status == ExpenseSettlementStatus.Applied)
        {
            return BadRequest("La liquidacion ya fue aplicada y no necesita una nueva aprobacion.");
        }

        if (settlement.Status != ExpenseSettlementStatus.Calculated)
        {
            return BadRequest("Solo se pueden aprobar liquidaciones calculadas.");
        }

        if (period.Status != ExpensePeriodStatus.Draft)
        {
            return BadRequest("Solo se puede aprobar una liquidacion mientras el periodo este en borrador.");
        }

        var existingCharges = await dbContext.ExpenseCharges
            .AnyAsync(
                x => !x.IsDeleted &&
                    x.ExpensePeriodId == id &&
                    x.SourceSettlementId != null,
                cancellationToken);

        if (existingCharges)
        {
            return BadRequest("Este periodo ya tiene cargos emitidos desde una liquidacion previa. No se puede aprobar nuevamente.");
        }

        ExpenseSettlementChargePreviewDto preview;
        try
        {
            preview = await distributionService.PreviewAsync(period, settlement, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }

        if (preview.ChargeCount == 0)
        {
            return BadRequest("La liquidacion no tiene cargos distribuibles para aprobar.");
        }

        var charges = preview.Items.Select(item => new ExpenseCharge
        {
            CompanyId = period.CompanyId,
            ExpensePeriodId = period.Id,
            UnitId = item.UnitId,
            ChargeType = item.ChargeType,
            SourceBuildingExpenseId = item.SourceBuildingExpenseId,
            SourceSettlementId = item.SourceSettlementId,
            Concept = item.Concept,
            Amount = item.Amount,
            Notes = item.Notes
        }).ToList();

        dbContext.ExpenseCharges.AddRange(charges);

        settlement.Status = ExpenseSettlementStatus.Approved;
        settlement.ApprovedAtUtc = DateTime.UtcNow;
        settlement.ApprovedByUserId = tenantContext.UserId;
        period.Status = ExpensePeriodStatus.Closed;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> Publish(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null)
        {
            return BadRequest("El periodo todavia no tiene una liquidacion calculada.");
        }

        if (period.Status == ExpensePeriodStatus.Published)
        {
            return BadRequest("Este periodo ya fue publicado.");
        }

        if (settlement.Status is not ExpenseSettlementStatus.Approved and not ExpenseSettlementStatus.Applied)
        {
            return BadRequest("Solo se pueden publicar liquidaciones aprobadas con cargos emitidos.");
        }

        var generatedCharges = await dbContext.ExpenseCharges
            .CountAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (generatedCharges == 0)
        {
            return BadRequest("Este periodo no tiene cargos generados para publicar.");
        }

        period.Status = ExpensePeriodStatus.Published;
        settlement.PublishedAtUtc = DateTime.UtcNow;
        settlement.PublishedByUserId = tenantContext.UserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyBuildingUsersAsync(period, cancellationToken);
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpPost("{id:guid}/apply-late-fees")]
    public async Task<ActionResult<ApplyLateFeesResultDto>> ApplyLateFees(
        Guid id,
        [FromBody] ApplyLateFeesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RatePercentage <= 0m)
        {
            return BadRequest("El porcentaje de recargo debe ser mayor que cero.");
        }

        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var referenceDate = request.ReferenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var lateFeeThreshold = period.LateFeeDate ?? period.DueDate;
        if (lateFeeThreshold >= referenceDate)
        {
            return BadRequest("Los recargos por mora solo se pueden aplicar despues de la fecha de corte de mora.");
        }

        if (period.Status != ExpensePeriodStatus.Published)
        {
            return BadRequest("Los recargos por mora solo se pueden aplicar cuando el periodo ya fue publicado.");
        }

        var existingLateFees = await dbContext.ExpenseCharges
            .AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == id && x.IsLateFee, cancellationToken);

        if (existingLateFees)
        {
            return BadRequest("Los recargos por mora ya fueron registrados para este periodo.");
        }

        var balances = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id)
            .GroupBy(x => new { x.UnitId, x.CompanyId })
            .Select(group => new
            {
                group.Key.UnitId,
                group.Key.CompanyId,
                TotalCharges = group.Sum(x => x.Amount)
            })
            .ToListAsync(cancellationToken);

        var paymentMap = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id)
            .GroupBy(x => x.UnitId)
            .ToDictionaryAsync(group => group.Key, group => group.Sum(x => x.Amount), cancellationToken);

        var charges = balances
            .Select(balance =>
            {
                var pendingAmount = balance.TotalCharges - paymentMap.GetValueOrDefault(balance.UnitId, 0m);
                var lateFeeAmount = pendingAmount <= 0m
                    ? 0m
                    : decimal.Round(pendingAmount * (request.RatePercentage / 100m), 2, MidpointRounding.AwayFromZero);

                return new ExpenseCharge
                {
                    CompanyId = balance.CompanyId,
                    ExpensePeriodId = id,
                    UnitId = balance.UnitId,
                    ChargeType = ExpenseChargeType.Adjustment,
                    IsLateFee = true,
                    SourceSettlementId = null,
                    SourceBuildingExpenseId = null,
                    Concept = string.IsNullOrWhiteSpace(request.Concept) ? $"Recargo por mora {period.Name}" : request.Concept.Trim(),
                    Amount = lateFeeAmount,
                    Notes = string.IsNullOrWhiteSpace(request.Notes)
                        ? $"Recargo aplicado el {referenceDate:yyyy-MM-dd} sobre saldo vencido."
                        : request.Notes.Trim()
                };
            })
            .Where(x => x.Amount > 0m)
            .ToList();

        if (charges.Count == 0)
        {
            return BadRequest("No hay saldos vencidos disponibles para aplicar recargos por mora.");
        }

        dbContext.ExpenseCharges.AddRange(charges);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ApplyLateFeesResultDto
        {
            ExpensePeriodId = period.Id,
            ExpensePeriodName = period.Name,
            ReferenceDate = referenceDate,
            RatePercentage = request.RatePercentage,
            ChargesCreated = charges.Count,
            UnitsAffected = charges.Select(x => x.UnitId).Distinct().Count(),
            TotalLateFeeAmount = charges.Sum(x => x.Amount)
        });
    }

    [HttpPost("{id:guid}/void-settlement")]
    public async Task<ActionResult<VoidSettlementResultDto>> VoidSettlement(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        if (period.Status == ExpensePeriodStatus.Published)
        {
            return BadRequest("No se puede anular una liquidación ya publicada.");
        }

        if (period.Status != ExpensePeriodStatus.Closed)
        {
            return BadRequest("Solo se puede anular la liquidación de un período en estado Cerrado.");
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null)
        {
            return BadRequest("El período no tiene una liquidación para anular.");
        }

        if (settlement.Status != ExpenseSettlementStatus.Approved)
        {
            return BadRequest("Solo se puede anular una liquidación en estado Aprobada.");
        }

        var settlementCharges = await dbContext.ExpenseCharges
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id && x.SourceSettlementId != null)
            .ToListAsync(cancellationToken);

        foreach (var charge in settlementCharges)
        {
            charge.IsDeleted = true;
        }

        settlement.Status = ExpenseSettlementStatus.Calculated;
        settlement.ApprovedAtUtc = null;
        settlement.ApprovedByUserId = null;
        period.Status = ExpensePeriodStatus.Draft;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new VoidSettlementResultDto
        {
            ExpensePeriodName = period.Name,
            DeletedChargeCount = settlementCharges.Count
        });
    }

    [HttpGet("{id:guid}/settlement-pdf")]
    public async Task<IActionResult> DownloadSettlementPdf(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken)
            && !await UserHasUnitInBuildingAsync(period.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var summary = await BuildSettlementSummaryAsync(period, cancellationToken);

        if (!summary.IsCalculated)
        {
            return BadRequest("El período todavía no tiene una liquidación calculada para exportar.");
        }

        var document = new SettlementPdfDocument(
            summary,
            period.StartDate.ToString("dd/MM/yyyy"),
            period.EndDate.ToString("dd/MM/yyyy"),
            period.DueDate.ToString("dd/MM/yyyy"));

        var pdfBytes = document.GeneratePdf();
        var fileName = $"liquidacion_{summary.ExpensePeriodName.Replace(" ", "_")}_{summary.BuildingName.Replace(" ", "_")}.pdf";

        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpGet("operational-alerts")]
    public async Task<ActionResult<ExpensePeriodOperationalAlertsDto>> GetOperationalAlerts(
        [FromQuery] int daysAhead = 7,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        daysAhead = Math.Clamp(daysAhead, 1, 30);
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var periodsQuery = dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            if (accessScope.IsCompanyAdmin && accessScope.CompanyId.HasValue)
            {
                periodsQuery = periodsQuery.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                periodsQuery = periodsQuery.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
            }
        }

        var periods = await periodsQuery
            .OrderBy(x => x.DueDate)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.BuildingId,
                BuildingName = x.Building != null ? x.Building.Name : string.Empty,
                x.DueDate,
                x.Status
            })
            .ToListAsync(cancellationToken);

        var periodIds = periods.Select(x => x.Id).ToList();
        var chargeMap = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && periodIds.Contains(x.ExpensePeriodId))
            .GroupBy(x => x.ExpensePeriodId)
            .ToDictionaryAsync(group => group.Key, group => group.Sum(x => x.Amount), cancellationToken);
        var paymentMap = await dbContext.Payments
            .AsNoTracking()
            .Where(x => !x.IsDeleted && periodIds.Contains(x.ExpensePeriodId))
            .GroupBy(x => x.ExpensePeriodId)
            .ToDictionaryAsync(group => group.Key, group => group.Sum(x => x.Amount), cancellationToken);
        var settlementMap = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .Where(x => !x.IsDeleted && periodIds.Contains(x.ExpensePeriodId))
            .ToDictionaryAsync(x => x.ExpensePeriodId, cancellationToken);

        var items = periods
            .SelectMany(period =>
            {
                var pendingAmount = chargeMap.GetValueOrDefault(period.Id, 0m) - paymentMap.GetValueOrDefault(period.Id, 0m);
                var daysUntilDue = period.DueDate.DayNumber - today.DayNumber;
                var settlement = settlementMap.GetValueOrDefault(period.Id);
                var alerts = new List<ExpensePeriodOperationalAlertItemDto>();

                if (daysUntilDue >= 0 && daysUntilDue <= daysAhead)
                {
                    alerts.Add(new ExpensePeriodOperationalAlertItemDto
                    {
                        ExpensePeriodId = period.Id,
                        ExpensePeriodName = period.Name,
                        BuildingId = period.BuildingId,
                        BuildingName = period.BuildingName,
                        DueDate = period.DueDate,
                        DaysUntilDue = daysUntilDue,
                        AlertType = "DueSoon",
                        Message = daysUntilDue == 0
                            ? "El periodo vence hoy."
                            : $"El periodo vence en {daysUntilDue} dia(s).",
                        PeriodStatus = period.Status,
                        SettlementStatus = settlement?.Status,
                        PendingAmount = decimal.Max(pendingAmount, 0m)
                    });
                }

                if (daysUntilDue < 0 && pendingAmount > 0m)
                {
                    alerts.Add(new ExpensePeriodOperationalAlertItemDto
                    {
                        ExpensePeriodId = period.Id,
                        ExpensePeriodName = period.Name,
                        BuildingId = period.BuildingId,
                        BuildingName = period.BuildingName,
                        DueDate = period.DueDate,
                        DaysUntilDue = daysUntilDue,
                        AlertType = "OverdueBalance",
                        Message = $"El periodo tiene saldo vencido por {Math.Abs(daysUntilDue)} dia(s).",
                        PeriodStatus = period.Status,
                        SettlementStatus = settlement?.Status,
                        PendingAmount = pendingAmount
                    });
                }

                if (settlement is not null &&
                    settlement.Status is ExpenseSettlementStatus.Approved or ExpenseSettlementStatus.Applied &&
                    period.Status != ExpensePeriodStatus.Published)
                {
                    alerts.Add(new ExpensePeriodOperationalAlertItemDto
                    {
                        ExpensePeriodId = period.Id,
                        ExpensePeriodName = period.Name,
                        BuildingId = period.BuildingId,
                        BuildingName = period.BuildingName,
                        DueDate = period.DueDate,
                        DaysUntilDue = daysUntilDue,
                        AlertType = "ReadyToPublish",
                        Message = "La liquidacion ya genero cargos y esta lista para publicar comprobantes.",
                        PeriodStatus = period.Status,
                        SettlementStatus = settlement.Status,
                        PendingAmount = decimal.Max(pendingAmount, 0m)
                    });
                }

                return alerts;
            })
            .OrderBy(x => x.DaysUntilDue)
            .ThenBy(x => x.BuildingName)
            .ThenBy(x => x.ExpensePeriodName)
            .ToList();

        return Ok(new ExpensePeriodOperationalAlertsDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Items = items
        });
    }

    private static bool IsValidRequest(ExpensePeriodUpsertRequest request, out string error)
    {
        if (request.BuildingId == Guid.Empty)
        {
            error = "El edificio es obligatorio.";
            return false;
        }

        if (request.Year < 2000 || request.Year > 2100)
        {
            error = "El anio debe estar entre 2000 y 2100.";
            return false;
        }

        if (request.Month < 1 || request.Month > 12)
        {
            error = "El mes debe estar entre 1 y 12.";
            return false;
        }

        if (request.Status != ExpensePeriodStatus.Draft)
        {
            error = "El estado inicial del periodo siempre debe ser Draft.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            error = "El nombre del periodo es obligatorio.";
            return false;
        }

        if (request.Name.Trim().Length > 120)
        {
            error = "El nombre del periodo no puede superar los 120 caracteres.";
            return false;
        }

        if (request.Notes.Trim().Length > 500)
        {
            error = "Las notas no pueden superar los 500 caracteres.";
            return false;
        }

        if (request.StartDate > request.EndDate)
        {
            error = "La fecha de inicio no puede ser posterior a la fecha de fin.";
            return false;
        }

        if (request.DueDate < request.EndDate)
        {
            error = "La fecha de vencimiento no puede ser anterior a la fecha de fin del periodo.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidGenerationRequest(GenerateExpenseChargesRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.Mode))
        {
            error = "El modo de generacion es obligatorio.";
            return false;
        }

        if (!request.Mode.Equals("FixedAmount", StringComparison.OrdinalIgnoreCase) &&
            !request.Mode.Equals("ByCoefficient", StringComparison.OrdinalIgnoreCase))
        {
            error = "El modo debe ser FixedAmount o ByCoefficient.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Concept))
        {
            error = "El concepto es obligatorio.";
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

    private async Task<ExpenseSettlementSummaryDto> BuildSettlementSummaryAsync(ExpensePeriod period, CancellationToken cancellationToken)
    {
        var settlement = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == period.Id, cancellationToken);

        if (settlement is null)
        {
            return await BuildLiveSettlementSummaryAsync(period, cancellationToken);
        }

        var userIds = new[] { settlement.GeneratedByUserId, settlement.ApprovedByUserId, settlement.PublishedByUserId }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        var userNames = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
        var generatedChargeCount = await dbContext.ExpenseCharges
            .AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.ExpensePeriodId == period.Id, cancellationToken);

        return new ExpenseSettlementSummaryDto
        {
            Id = settlement.Id,
            ExpensePeriodId = settlement.ExpensePeriodId,
            BuildingId = settlement.BuildingId,
            ExpensePeriodName = period.Name,
            BuildingName = period.Building?.Name ?? string.Empty,
            TotalBuildingExpenses = settlement.TotalBuildingExpenses,
            TotalBuildingIncomes = settlement.TotalBuildingIncomes,
            ReserveFundAmount = settlement.ReserveFundAmount,
            ExtraordinaryAmount = settlement.ExtraordinaryAmount,
            NetCommonAmount = settlement.NetCommonAmount,
            GeneratedAtUtc = settlement.GeneratedAtUtc,
            GeneratedByUserId = settlement.GeneratedByUserId,
            GeneratedByUserName = userNames.GetValueOrDefault(settlement.GeneratedByUserId, string.Empty),
            ApprovedAtUtc = settlement.ApprovedAtUtc,
            ApprovedByUserId = settlement.ApprovedByUserId,
            ApprovedByUserName = settlement.ApprovedByUserId.HasValue
                ? userNames.GetValueOrDefault(settlement.ApprovedByUserId.Value, string.Empty)
                : string.Empty,
            PublishedAtUtc = settlement.PublishedAtUtc,
            PublishedByUserId = settlement.PublishedByUserId,
            PublishedByUserName = settlement.PublishedByUserId.HasValue
                ? userNames.GetValueOrDefault(settlement.PublishedByUserId.Value, string.Empty)
                : string.Empty,
            Status = settlement.Status,
            PeriodStatus = period.Status,
            GeneratedChargeCount = generatedChargeCount,
            IsCalculated = true,
            CategoryTotals = await BuildCategoryTotalsAsync(period.Id, cancellationToken)
        };
    }

    private async Task<List<SettlementCategoryTotalDto>> BuildCategoryTotalsAsync(Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                     && x.ExpensePeriodId == expensePeriodId
                     && (x.DistributionType == BuildingExpenseDistributionType.ByCoefficient
                      || x.DistributionType == BuildingExpenseDistributionType.FixedPerUnit))
            .Select(x => new { x.Category, x.Amount, x.Description })
            .ToListAsync(cancellationToken);

        return BuildCategoryTotals(rows.Select(x => (x.Category, x.Amount, x.Description)));
    }

    private static List<SettlementCategoryTotalDto> BuildCategoryTotals(IEnumerable<(BuildingExpenseCategory Category, decimal Amount, string Description)> rows) =>
        rows.GroupBy(x => x.Category)
            .Select(g => new SettlementCategoryTotalDto
            {
                Category = g.Key.ToString(),
                ExpenseCount = g.Count(),
                Amount = g.Sum(x => x.Amount),
                Items = g.Select(x => new SettlementCategoryItemDto { Description = x.Description, Amount = x.Amount })
                         .OrderByDescending(x => x.Amount)
                         .ToList()
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

    private async Task<ExpenseSettlementSummaryDto> BuildLiveSettlementSummaryAsync(ExpensePeriod period, CancellationToken cancellationToken)
    {
        var expenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
            .ToListAsync(cancellationToken);

        var incomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id)
            .ToListAsync(cancellationToken);

        var totalBuildingExpenses = expenses.Sum(x => x.Amount);
        var totalBuildingIncomes = incomes.Sum(x => x.Amount);
        var reserveFundAmount = expenses
            .Where(x => x.Category == BuildingExpenseCategory.ReserveFund)
            .Sum(x => x.Amount);
        var extraordinaryAmount = expenses
            .Where(x => x.Category == BuildingExpenseCategory.Extraordinary)
            .Sum(x => x.Amount);

        return new ExpenseSettlementSummaryDto
        {
            Id = null,
            ExpensePeriodId = period.Id,
            BuildingId = period.BuildingId,
            ExpensePeriodName = period.Name,
            BuildingName = period.Building?.Name ?? string.Empty,
            TotalBuildingExpenses = totalBuildingExpenses,
            TotalBuildingIncomes = totalBuildingIncomes,
            ReserveFundAmount = reserveFundAmount,
            ExtraordinaryAmount = extraordinaryAmount,
            NetCommonAmount = totalBuildingExpenses - totalBuildingIncomes,
            GeneratedAtUtc = null,
            GeneratedByUserId = null,
            GeneratedByUserName = string.Empty,
            ApprovedAtUtc = null,
            ApprovedByUserId = null,
            ApprovedByUserName = string.Empty,
            PublishedAtUtc = null,
            PublishedByUserId = null,
            PublishedByUserName = string.Empty,
            Status = null,
            PeriodStatus = period.Status,
            GeneratedChargeCount = 0,
            IsCalculated = false,
            CategoryTotals = BuildCategoryTotals(expenses
                .Where(x => x.DistributionType is BuildingExpenseDistributionType.ByCoefficient
                                                or BuildingExpenseDistributionType.FixedPerUnit)
                .Select(x => (x.Category, x.Amount, x.Description)))
        };
    }

    private async Task<(ActionResult? Error, ExpensePeriod? Period, ExpenseSettlement? Settlement)> ValidateSettlementDistributionContextAsync(
        Guid expensePeriodId,
        bool requireGenerationReady,
        CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expensePeriodId, cancellationToken);

        if (period is null)
        {
            return (NotFound(), null, null);
        }

        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return (Forbid(), null, null);
        }

        var settlement = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken);

        if (settlement is null)
        {
            return (BadRequest("El periodo todavia no tiene una liquidacion calculada."), null, null);
        }

        if (!requireGenerationReady)
        {
            return (null, period, settlement);
        }

        if (settlement.Status is not ExpenseSettlementStatus.Calculated and not ExpenseSettlementStatus.Approved)
        {
            return (BadRequest("Los cargos de liquidacion solo se pueden generar desde liquidaciones calculadas o aprobadas."), null, null);
        }

        var existingCharges = await dbContext.ExpenseCharges
            .AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken);

        if (existingCharges)
        {
            return (BadRequest("Este periodo ya tiene cargos. Debes eliminarlos antes de generar cargos de liquidacion."), null, null);
        }

        return (null, period, settlement);
    }

    private async Task<bool> HasOperationalDependenciesAsync(Guid expensePeriodId, CancellationToken cancellationToken)
    {
        return await dbContext.BuildingExpenses.AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken)
            || await dbContext.BuildingIncomes.AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken)
            || await dbContext.ExpenseCharges.AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken)
            || await dbContext.Payments.AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken)
            || await dbContext.ExpenseSettlements.AnyAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken);
    }

    private static List<ExpenseCharge> GenerateFixedAmount(
        Guid companyId,
        Guid expensePeriodId,
        IReadOnlyList<Unit> units,
        GenerateExpenseChargesRequest request) =>
        units.Select(unit => new ExpenseCharge
            {
                CompanyId = companyId,
                ExpensePeriodId = expensePeriodId,
                UnitId = unit.Id,
                ChargeType = ExpenseChargeType.Ordinary,
                Concept = request.Concept.Trim(),
                Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
                Notes = request.Notes.Trim()
        }).ToList();

    private static List<ExpenseCharge> GenerateByCoefficient(
        Guid companyId,
        Guid expensePeriodId,
        IReadOnlyList<Unit> units,
        GenerateExpenseChargesRequest request)
    {
        var generated = new List<ExpenseCharge>(units.Count);
        var roundedTotal = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var distributed = 0m;

        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var amount = index == units.Count - 1
                ? roundedTotal - distributed
                : decimal.Round(roundedTotal * unit.Coefficient, 2, MidpointRounding.AwayFromZero);

            distributed += amount;

            generated.Add(new ExpenseCharge
            {
                CompanyId = companyId,
                ExpensePeriodId = expensePeriodId,
                UnitId = unit.Id,
                ChargeType = ExpenseChargeType.Ordinary,
                Concept = request.Concept.Trim(),
                Amount = amount,
                Notes = request.Notes.Trim()
            });
        }

        return generated;
    }

    private static bool HasValidCoefficientBase(decimal totalCoefficient) =>
        decimal.Abs(totalCoefficient - CoefficientDistributionExpectedTotal) <= CoefficientDistributionTolerance;

    private static string ResolveName(ExpensePeriodUpsertRequest request) =>
        string.IsNullOrWhiteSpace(request.Name)
            ? ResolveName(request.Year, request.Month)
            : request.Name.Trim();

    [HttpPost("bulk-create")]
    public async Task<ActionResult<BulkCreateExpensePeriodsResultDto>> BulkCreate(
        [FromBody] BulkCreateExpensePeriodsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Year < 2000 || request.Year > 2100)
        {
            return BadRequest("El anio debe estar entre 2000 y 2100.");
        }

        if (request.Month < 1 || request.Month > 12)
        {
            return BadRequest("El mes debe estar entre 1 y 12.");
        }

        if (request.StartDate > request.EndDate)
        {
            return BadRequest("La fecha de inicio no puede ser posterior a la fecha de fin.");
        }

        if (request.DueDate < request.EndDate)
        {
            return BadRequest("La fecha de vencimiento no puede ser anterior a la fecha de fin del periodo.");
        }

        if (request.LateFeeDate.HasValue && request.LateFeeDate.Value < request.DueDate)
        {
            return BadRequest("La fecha de corte de mora no puede ser anterior al vencimiento.");
        }

        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        List<Guid> targetIds = request.BuildingIds.Count > 0
            ? request.BuildingIds.Where(id => accessibleBuildingIds.Contains(id)).ToList()
            : accessibleBuildingIds.ToList();

        if (targetIds.Count == 0)
        {
            return BadRequest("No hay edificios accesibles para crear periodos.");
        }

        var buildings = await dbContext.Buildings
            .AsNoTracking()
            .Include(x => x.Condominium)
            .Where(x => !x.IsDeleted && targetIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var existingPeriodBuildingIds = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                targetIds.Contains(x.BuildingId) &&
                x.Year == request.Year &&
                x.Month == request.Month)
            .Select(x => x.BuildingId)
            .ToListAsync(cancellationToken);

        var existingSet = existingPeriodBuildingIds.ToHashSet();
        var result = new BulkCreateExpensePeriodsResultDto();
        var newPeriods = new List<ExpensePeriod>();
        var name = string.IsNullOrWhiteSpace(request.Name)
            ? ResolveName(request.Year, request.Month)
            : request.Name.Trim();

        foreach (var building in buildings)
        {
            if (existingSet.Contains(building.Id))
            {
                result.SkippedBuildings.Add(building.Name);
                continue;
            }

            var effectiveCompanyId = building.CompanyId ?? building.Condominium?.CompanyId;
            if (!effectiveCompanyId.HasValue)
            {
                result.SkippedBuildings.Add(building.Name);
                continue;
            }

            newPeriods.Add(new ExpensePeriod
            {
                CompanyId = effectiveCompanyId.Value,
                BuildingId = building.Id,
                Year = request.Year,
                Month = request.Month,
                Name = name,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                DueDate = request.DueDate,
                LateFeeDate = request.LateFeeDate,
                Status = ExpensePeriodStatus.Draft,
                Notes = request.Notes.Trim()
            });
            result.CreatedBuildings.Add(building.Name);
        }

        if (newPeriods.Count > 0)
        {
            dbContext.ExpensePeriods.AddRange(newPeriods);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        result.Created = newPeriods.Count;
        result.Skipped = result.SkippedBuildings.Count;
        return Ok(result);
    }

    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<CloneExpensePeriodResultDto>> Clone(Guid id, CancellationToken cancellationToken)
    {
        var source = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (source is null)
        {
            return NotFound();
        }

        if (!await accessScope.CanAccessBuildingAsync(source.BuildingId, cancellationToken))
        {
            return Forbid();
        }

        var (targetYear, targetMonth) = source.Month == 12
            ? (source.Year + 1, 1)
            : (source.Year, source.Month + 1);

        var exists = await dbContext.ExpensePeriods.AnyAsync(
            x => !x.IsDeleted &&
                x.BuildingId == source.BuildingId &&
                x.Year == targetYear &&
                x.Month == targetMonth,
            cancellationToken);

        if (exists)
        {
            return Conflict($"Ya existe un periodo para {source.Building?.Name ?? "ese edificio"} en {targetYear}-{targetMonth:D2}.");
        }

        var monthDiff = source.Month == 12 ? 12 : 0;
        var yearDiff = source.Month == 12 ? 1 : 0;
        var startDate = source.StartDate.AddMonths(1);
        var endDate = source.EndDate.AddMonths(1);
        var dueDate = source.DueDate.AddMonths(1);
        var lateFeeDate = source.LateFeeDate?.AddMonths(1);

        var cloned = new ExpensePeriod
        {
            CompanyId = source.CompanyId,
            BuildingId = source.BuildingId,
            Year = targetYear,
            Month = targetMonth,
            Name = ResolveName(targetYear, targetMonth),
            StartDate = startDate,
            EndDate = endDate,
            DueDate = dueDate,
            LateFeeDate = lateFeeDate,
            Status = ExpensePeriodStatus.Draft,
            Notes = string.Empty
        };

        dbContext.ExpensePeriods.Add(cloned);

        var sourceExpenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == source.Id)
            .ToListAsync(cancellationToken);

        var copiedExpenses = sourceExpenses.Select(e => new BuildingExpense
        {
            CompanyId = e.CompanyId,
            BuildingId = e.BuildingId,
            ExpensePeriodId = cloned.Id,
            Category = e.Category,
            SupplierName = e.SupplierName,
            Description = e.Description,
            ExpenseDate = startDate,
            Amount = e.Amount,
            DistributionType = e.DistributionType,
            TargetUnitId = e.TargetUnitId,
            Notes = e.Notes
        }).ToList();

        if (copiedExpenses.Count > 0)
        {
            dbContext.BuildingExpenses.AddRange(copiedExpenses);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CloneExpensePeriodResultDto
        {
            Period = ToDto(cloned, source.Building?.Name),
            CopiedExpenses = copiedExpenses.Count
        });
    }

    private static ExpensePeriodDto ToDto(ExpensePeriod entity, string? buildingName = null) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            BuildingId = entity.BuildingId,
            BuildingName = buildingName ?? entity.Building?.Name ?? string.Empty,
            Year = entity.Year,
            Month = entity.Month,
            Name = entity.Name,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            DueDate = entity.DueDate,
            LateFeeDate = entity.LateFeeDate,
            Status = entity.Status,
            Notes = entity.Notes
        };

    private static string ResolveName(int year, int month) => $"{year:D4}-{month:D2}";

    private async Task<bool> UserHasUnitInBuildingAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var uid = tenantContext.UserId;

        if (await dbContext.UnitOwners
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.Unit != null && x.Unit.BuildingId == buildingId && x.OwnerId == uid, cancellationToken))
            return true;

        var email = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == uid && !x.IsDeleted)
            .Select(x => x.Email)
            .FirstOrDefaultAsync(cancellationToken);

        if (email is null) return false;

        return await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.EndDate == null && x.Unit != null && x.Unit.BuildingId == buildingId
                && x.Resident != null && !x.Resident.IsDeleted && x.Resident.Email == email, cancellationToken);
    }

    private async Task NotifyBuildingUsersAsync(ExpensePeriod period, CancellationToken ct)
    {
        var recipientIds = await GetBuildingUserIdsAsync(period.BuildingId, ct);
        if (recipientIds.Count == 0) return;

        foreach (var rid in recipientIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = period.CompanyId,
                RecipientId = rid,
                Type = NotificationType.ExpensePeriodPublished,
                Title = "Nuevo periodo de expensas publicado",
                Body = period.Name,
                EntityType = "ExpensePeriod",
                EntityId = period.Id
            });
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>> GetBuildingUserIdsAsync(Guid buildingId, CancellationToken ct)
    {
        var ownerIds = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId)
            .Select(x => x.OwnerId)
            .ToListAsync(ct);

        var residentEmails = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                     && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId
                     && x.Resident != null && !x.Resident.IsDeleted)
            .Select(x => x.Resident!.Email)
            .Distinct()
            .ToListAsync(ct);

        var residentUserIds = residentEmails.Count > 0
            ? await dbContext.ApplicationUsers
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive && residentEmails.Contains(x.Email))
                .Select(x => x.Id)
                .ToListAsync(ct)
            : [];

        return ownerIds.Concat(residentUserIds).Distinct().ToList();
    }
}
