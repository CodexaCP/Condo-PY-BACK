using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Services;
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
    IExpenseSettlementDistributionService distributionService,
    IWebHostEnvironment env,
    OwnerCreditService ownerCredits,
    PushDispatcher pushDispatcher,
    ILogger<ExpensePeriodsController> logger) : ControllerBase
{
    // Permisos de liquidacion: CompanyOperator solo calcula; BuildingManager aprueba; CompanyAdmin
    // (presidente) publica, y solo una liquidacion ya aprobada por el Encargado de edificio.
    private bool CanApproveSettlement() =>
        tenantContext.IsSuperAdmin ||
        string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    // Anular una liquidacion aprobada y aplicar recargos por mora: encargado, presidente o superadmin.
    private bool CanManageSettlement() =>
        tenantContext.IsSuperAdmin ||
        string.Equals(tenantContext.Role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tenantContext.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    private ObjectResult Prohibited(string message) => StatusCode(StatusCodes.Status403Forbidden, message);

    private bool CanPublishSettlement() =>
        tenantContext.IsSuperAdmin ||
        string.Equals(tenantContext.Role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpensePeriodDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var accessibleBuildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var query = dbContext.ExpensePeriods
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!accessScope.IsSuperAdmin)
        {
            // Solo el Administrador de empresa sin acotar ve toda la empresa; un administrador acotado a un
            // condominio, el Operador y el Encargado ven los periodos de los edificios de su alcance.
            if (accessScope.HasFullCompanyScope && accessScope.CompanyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
            }
            else
            {
                query = query.Where(x => accessibleBuildingIds.Contains(x.BuildingId));
            }
        }

        // Filtros opcionales (los usa la app movil). Fuera de alcance no devuelve nada, sin revelar si existe.
        if (buildingId.HasValue)
        {
            query = query.Where(x => x.BuildingId == buildingId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ExpensePeriodStatus>(status, true, out var parsedStatus))
                return BadRequest("Estado de periodo inválido.");

            query = query.Where(x => x.Status == parsedStatus);
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
        // El operador no elimina periodos.
        if (string.Equals(tenantContext.Role, "CompanyOperator", StringComparison.OrdinalIgnoreCase)) return Forbid();

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

    // Vista previa de los cargos que saldrian de los gastos, ingresos y aportes actuales del periodo. No exige que la
    // liquidacion este calculada: en borrador es lo que el usuario ve en la seccion Cargos.
    [HttpGet("{id:guid}/charges-preview")]
    public async Task<ActionResult<ExpenseSettlementChargePreviewDto>> GetChargesPreview(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken)) return Forbid();

        try
        {
            return Ok(await distributionService.PreviewAsync(period, await ResolveSettlementForPreviewAsync(id, cancellationToken), cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    // Conciliacion del periodo: gastos + aportes - ingresos acreditados = cargos. Compara lo que deberia repartirse
    // hoy con lo que realmente se emitio, y resume la cobranza.
    [HttpGet("{id:guid}/reconciliation")]
    public async Task<ActionResult<ExpensePeriodReconciliationDto>> GetReconciliation(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken)) return Forbid();

        var settlement = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        var expenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id)
            .Select(x => new { x.Amount, x.DistributionType, x.PaidByReserveFund })
            .ToListAsync(cancellationToken);

        var live = await BuildLiveSettlementSummaryAsync(period, cancellationToken);
        var incomeTreatment = await dbContext.Buildings.AsNoTracking()
            .Where(x => x.Id == period.BuildingId)
            .Select(x => (IncomeTreatment?)x.IncomeTreatment)
            .FirstOrDefaultAsync(cancellationToken);
        var incomesCredited = incomeTreatment == IncomeTreatment.ToReserveFund ? 0m : live.TotalBuildingIncomes;

        var charges = await dbContext.ExpenseCharges
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id)
            .Select(x => new
            {
                x.Amount, x.IsLateFee, x.IsReversal, x.SourceSettlementId, x.SourceCreditNoteId,
                Allocated = x.Allocations.Where(a => !a.IsDeleted && a.Payment != null && !a.Payment.IsReversed)
                    .Sum(a => (decimal?)a.AllocatedAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var manual = charges.Where(c => c.SourceSettlementId == null && !c.IsLateFee && !c.IsReversal && c.SourceCreditNoteId == null).ToList();
        var issued = charges.Where(c => c.SourceSettlementId != null).Sum(c => c.Amount);
        var totalCharged = charges.Sum(c => c.Amount);
        var collected = charges.Sum(c => c.Allocated);

        var dto = new ExpensePeriodReconciliationDto
        {
            ExpensePeriodId = period.Id,
            ExpensePeriodName = period.Name,
            PeriodStatus = period.Status.ToString(),
            SettlementStatus = settlement?.Status.ToString(),
            TotalExpenses = live.TotalBuildingExpenses,
            NonDistributedExpenses = expenses
                .Where(x => x.DistributionType == BuildingExpenseDistributionType.NonDistributed && !x.PaidByReserveFund)
                .Sum(x => x.Amount),
            PaidByReserveFundExpenses = expenses.Where(x => x.PaidByReserveFund).Sum(x => x.Amount),
            IncomesCredited = incomesCredited,
            IssuedCharges = issued,
            ManualChargeCount = manual.Count,
            ManualChargeAmount = manual.Sum(c => c.Amount),
            LateFeeAmount = charges.Where(c => c.IsLateFee).Sum(c => c.Amount),
            TotalCharged = totalCharged,
            Collected = collected,
            Pending = totalCharged - collected
        };

        try
        {
            var preview = await distributionService.PreviewAsync(period, await ResolveSettlementForPreviewAsync(id, cancellationToken), cancellationToken);
            dto.ExpectedCharges = preview.TotalGeneratedAmount;
            dto.ReserveContribution = preview.Items
                .Where(i => i.ChargeType == ExpenseChargeType.ReserveFund && i.SourceBuildingExpenseId == null).Sum(i => i.Amount);
            dto.ExtraordinaryContribution = preview.Items
                .Where(i => i.ChargeType == ExpenseChargeType.Extraordinary && i.SourceBuildingExpenseId == null).Sum(i => i.Amount);
            dto.Difference = dto.ExpectedCharges - dto.IssuedCharges;

            if (issued == 0m && period.Status == ExpensePeriodStatus.Draft)
            {
                dto.State = "Preview";
                dto.Message = "Todavía no se emitieron cargos: lo de abajo es lo que saldría al aprobar la liquidación.";
            }
            else if (decimal.Abs(dto.Difference) <= 0.5m)
            {
                dto.State = "Reconciled";
            }
            else
            {
                dto.State = "Difference";
                dto.Message = "Los cargos emitidos no coinciden con lo que resulta de los gastos, ingresos y aportes actuales.";
            }
        }
        catch (InvalidOperationException exception)
        {
            dto.State = "Error";
            dto.Message = exception.Message;
        }

        return Ok(dto);
    }

    // La vista previa solo usa el Id de la liquidacion para marcar el origen de cada cargo: con liquidacion se usa la
    // real, sin ella una transitoria (no se guarda).
    private async Task<ExpenseSettlement> ResolveSettlementForPreviewAsync(Guid expensePeriodId, CancellationToken cancellationToken) =>
        await dbContext.ExpenseSettlements.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId, cancellationToken)
        ?? new ExpenseSettlement { Id = Guid.Empty, ExpensePeriodId = expensePeriodId };

    [HttpPost("{id:guid}/generate-settlement-charges")]
    public Task<ActionResult<ExpenseSettlementChargePreviewDto>> GenerateSettlementCharges(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<ActionResult<ExpenseSettlementChargePreviewDto>>(BadRequest("La emision de cargos ahora ocurre al aprobar la liquidacion."));

    [HttpPost("{id:guid}/approve-settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> ApproveSettlement(Guid id, CancellationToken cancellationToken)
    {
        if (!CanApproveSettlement())
            return Prohibited("Solo el Encargado de edificio (Building Manager) puede aprobar la liquidación. Una vez aprobada por él, el Administrador de empresa podrá publicarla.");

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

        // Los cargos nacen solo de la liquidacion. Un cargo manual anterior (legacy) se sumaria encima y cobraria dos veces.
        var legacyManualCharges = await dbContext.ExpenseCharges.CountAsync(
            x => !x.IsDeleted && x.ExpensePeriodId == id && x.SourceSettlementId == null
                 && !x.IsLateFee && !x.IsReversal && x.SourceCreditNoteId == null, cancellationToken);

        if (legacyManualCharges > 0)
        {
            return BadRequest($"Este periodo tiene {legacyManualCharges} cargo(s) manual(es) anterior(es) (legacy) que no vienen de la liquidación. Elimínelos desde Gastos y cargos › Cargos antes de aprobar: los cargos ahora se generan solo desde los gastos.");
        }

        if (period.Building?.PresidentUserId is null)
        {
            return BadRequest("Este edificio no tiene un presidente de consorcio asignado. Asigná uno desde la ficha del propietario correspondiente antes de aprobar la liquidación.");
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
        settlement.RejectionReason = string.Empty;
        settlement.RejectedAtUtc = null;
        settlement.RejectedByUserId = null;
        settlement.PresidentApprovedAtUtc = null;
        settlement.PresidentApprovedByUserId = null;
        settlement.PresidentRejectionReason = string.Empty;
        settlement.PresidentRejectedAtUtc = null;
        settlement.PresidentRejectedByUserId = null;
        period.Status = ExpensePeriodStatus.Closed;

        var pendingReviewTitle = "Liquidación pendiente de tu revisión";
        var pendingReviewBody = $"Se emitió un nuevo período de expensas ({period.Name}) para {period.Building.Name} y se encuentra pendiente de tu revisión como presidente del consorcio.";

        dbContext.Notifications.Add(new Notification
        {
            CompanyId = period.CompanyId,
            RecipientId = period.Building.PresidentUserId.Value,
            Type = NotificationType.SettlementPendingPresidentReview,
            Title = pendingReviewTitle,
            Body = pendingReviewBody,
            EntityType = "ExpensePeriod",
            EntityId = period.Id
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await pushDispatcher.NotifyUserAsync(period.Building.PresidentUserId.Value, pendingReviewTitle, pendingReviewBody, "ExpensePeriod", period.Id, cancellationToken, nameof(NotificationType.SettlementPendingPresidentReview));
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpGet("{id:guid}/president-review")]
    public async Task<ActionResult<PresidentSettlementReviewDto>> GetPresidentReview(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (period.Building?.PresidentUserId != tenantContext.UserId)
        {
            return Forbid();
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null || settlement.Status != ExpenseSettlementStatus.Approved
            || settlement.PresidentApprovedByUserId.HasValue || settlement.PresidentRejectedByUserId.HasValue)
        {
            return Forbid();
        }

        var expenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id)
            .OrderBy(x => x.Category).ThenBy(x => x.ExpenseDate)
            .Select(x => new PresidentSettlementExpenseItemDto
            {
                Id = x.Id,
                Category = x.Category.ToString(),
                SupplierName = x.SupplierName,
                Description = x.Description,
                ExpenseDate = x.ExpenseDate,
                Amount = x.Amount,
                HasReceipt = x.ReceiptStoredName != null,
                ReceiptFileName = x.ReceiptFileName
            })
            .ToListAsync(cancellationToken);

        return Ok(new PresidentSettlementReviewDto
        {
            Settlement = await BuildSettlementSummaryAsync(period, cancellationToken),
            Expenses = expenses
        });
    }

    [HttpPost("{id:guid}/president-approve-settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> PresidentApproveSettlement(Guid id, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (period.Building?.PresidentUserId != tenantContext.UserId)
        {
            return Forbid();
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null || settlement.Status != ExpenseSettlementStatus.Approved
            || settlement.PresidentApprovedByUserId.HasValue || settlement.PresidentRejectedByUserId.HasValue)
        {
            return Forbid();
        }

        settlement.PresidentApprovedAtUtc = DateTime.UtcNow;
        settlement.PresidentApprovedByUserId = tenantContext.UserId;

        var companyAdminIds = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.CompanyId == period.CompanyId && x.Role == UserRole.CompanyAdmin)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var presidentApprovedTitle = "Liquidación aprobada por el presidente";
        var presidentApprovedBody = $"El presidente del consorcio aprobó la liquidación del período {period.Name} de {period.Building.Name}. Ya se puede publicar.";

        foreach (var adminId in companyAdminIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = period.CompanyId,
                RecipientId = adminId,
                Type = NotificationType.SettlementApprovedByPresident,
                Title = presidentApprovedTitle,
                Body = presidentApprovedBody,
                EntityType = "ExpensePeriod",
                EntityId = period.Id
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await pushDispatcher.NotifyUsersAsync(companyAdminIds, presidentApprovedTitle, presidentApprovedBody, "ExpensePeriod", period.Id, cancellationToken);
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpPost("{id:guid}/president-reject-settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> PresidentRejectSettlement(
        Guid id, [FromBody] RejectSettlementRequest request, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (period.Building?.PresidentUserId != tenantContext.UserId)
        {
            return Forbid();
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null || settlement.Status != ExpenseSettlementStatus.Approved
            || settlement.PresidentApprovedByUserId.HasValue || settlement.PresidentRejectedByUserId.HasValue)
        {
            return Forbid();
        }

        var reason = request.RejectionReason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BadRequest("El motivo de rechazo es obligatorio.");
        }

        if (reason.Length > 500)
        {
            return BadRequest("El motivo no puede superar los 500 caracteres.");
        }

        var approvedByUserId = settlement.ApprovedByUserId;

        var settlementCharges = await dbContext.ExpenseCharges
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id && x.SourceSettlementId != null)
            .ToListAsync(cancellationToken);

        foreach (var charge in settlementCharges)
        {
            charge.IsDeleted = true;
        }

        settlement.Status = ExpenseSettlementStatus.Rejected;
        settlement.PresidentRejectionReason = reason;
        settlement.PresidentRejectedAtUtc = DateTime.UtcNow;
        settlement.PresidentRejectedByUserId = tenantContext.UserId;
        settlement.ApprovedAtUtc = null;
        settlement.ApprovedByUserId = null;
        period.Status = ExpensePeriodStatus.Draft;

        var rejectedByPresidentTitle = "El presidente rechazó la liquidación";
        var rejectedByPresidentBody = $"La liquidación del periodo {period.Name} de {period.Building.Name} fue rechazada por el presidente del consorcio. Motivo: {reason}.";

        if (approvedByUserId.HasValue)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = period.CompanyId,
                RecipientId = approvedByUserId.Value,
                Type = NotificationType.SettlementRejectedByPresident,
                Title = rejectedByPresidentTitle,
                Body = rejectedByPresidentBody,
                EntityType = "ExpensePeriod",
                EntityId = period.Id
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (approvedByUserId.HasValue)
            await pushDispatcher.NotifyUserAsync(approvedByUserId.Value, rejectedByPresidentTitle, rejectedByPresidentBody, "ExpensePeriod", period.Id, cancellationToken);
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> Publish(Guid id, CancellationToken cancellationToken)
    {
        if (!CanPublishSettlement())
            return Prohibited("Solo el Administrador de empresa puede publicar la liquidación.");

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

        // La publicacion exige que la liquidacion haya sido aprobada primero por el Encargado de edificio.
        var approvedByManager = settlement.ApprovedByUserId.HasValue
            && await dbContext.ApplicationUsers.AsNoTracking().AnyAsync(
                u => u.Id == settlement.ApprovedByUserId.Value
                     && (u.Role == UserRole.BuildingManager || u.Role == UserRole.SuperAdmin),
                cancellationToken);

        if (!approvedByManager)
        {
            return Prohibited("No se puede publicar: la liquidación debe ser aprobada primero por el Encargado de edificio (Building Manager). " +
                              "Cuando él la apruebe, el Administrador de empresa podrá publicarla.");
        }

        if (!settlement.PresidentApprovedByUserId.HasValue)
        {
            return Prohibited("No se puede publicar: la liquidación todavía no fue aprobada por el presidente del consorcio.");
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
        await ApplyOwnerCreditsAsync(period, cancellationToken);
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    // Deshacer una publicacion hecha por error. Solo SuperAdmin, y solo si todavia no hay ningun
    // pago real (staff o propietario) ni recargo por mora aplicado contra los cargos del periodo —
    // en ese caso se bloquea, para no tener que revertir plata ya movida. Si pasa, vuelve todo el
    // ciclo de aprobacion al principio (Calculada), como si nunca se hubiera aprobado ni publicado.
    [HttpPost("{id:guid}/unpublish-settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> UnpublishSettlement(
        Guid id, [FromBody] RejectSettlementRequest request, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
            return Prohibited("Solo el superadministrador puede deshacer la publicación de un período.");

        var period = await dbContext.ExpensePeriods
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (period is null)
        {
            return NotFound();
        }

        if (period.Status != ExpensePeriodStatus.Published)
        {
            return BadRequest("Este período no está publicado.");
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null)
        {
            return BadRequest("El período no tiene una liquidación para deshacer.");
        }

        var reason = request.RejectionReason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BadRequest("El motivo es obligatorio.");
        }

        if (reason.Length > 500)
        {
            return BadRequest("El motivo no puede superar los 500 caracteres.");
        }

        var charges = await dbContext.ExpenseCharges
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id)
            .ToListAsync(cancellationToken);

        if (charges.Any(x => x.IsLateFee))
        {
            return BadRequest("No se puede deshacer: ya se aplicaron recargos por mora sobre este período.");
        }

        var chargeIds = charges.Select(x => x.Id).ToHashSet();
        var hasRealPayments = await dbContext.PaymentAllocations
            .AsNoTracking()
            .AnyAsync(
                x => !x.IsDeleted && chargeIds.Contains(x.ExpenseChargeId)
                     && x.Payment != null && !x.Payment.IsReversed,
                cancellationToken);

        if (hasRealPayments)
        {
            return BadRequest("No se puede deshacer: ya hay pagos reales aplicados a los cargos de este período. Revertí esos pagos primero si corresponde.");
        }

        foreach (var charge in charges)
        {
            charge.IsDeleted = true;
        }

        period.Status = ExpensePeriodStatus.Draft;
        settlement.Status = ExpenseSettlementStatus.Calculated;
        settlement.ApprovedAtUtc = null;
        settlement.ApprovedByUserId = null;
        settlement.PresidentApprovedAtUtc = null;
        settlement.PresidentApprovedByUserId = null;
        settlement.PresidentRejectionReason = string.Empty;
        settlement.PresidentRejectedAtUtc = null;
        settlement.PresidentRejectedByUserId = null;
        settlement.PublishedAtUtc = null;
        settlement.PublishedByUserId = null;
        settlement.RejectionReason = string.Empty;
        settlement.RejectedAtUtc = null;
        settlement.RejectedByUserId = null;
        settlement.UnpublishReason = reason;
        settlement.UnpublishedAtUtc = DateTime.UtcNow;
        settlement.UnpublishedByUserId = tenantContext.UserId;

        var recipientIds = await GetBuildingUserIdsAsync(period.BuildingId, cancellationToken);
        var unpublishedTitle = "Se retiró la publicación de un período de expensas";
        var unpublishedBody = $"El período {period.Name} de {period.Building!.Name} fue retirado por un error administrativo y vuelve a estar en preparación. Motivo: {reason}.";

        foreach (var rid in recipientIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = period.CompanyId,
                RecipientId = rid,
                Type = NotificationType.ExpensePeriodUnpublished,
                Title = unpublishedTitle,
                Body = unpublishedBody,
                EntityType = "ExpensePeriod",
                EntityId = period.Id
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await pushDispatcher.NotifyUsersAsync(recipientIds, unpublishedTitle, unpublishedBody, "ExpensePeriod", period.Id, cancellationToken);
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpPost("{id:guid}/reject-settlement")]
    public async Task<ActionResult<ExpenseSettlementSummaryDto>> RejectSettlement(
        Guid id, [FromBody] RejectSettlementRequest request, CancellationToken cancellationToken)
    {
        if (!CanPublishSettlement())
            return Prohibited("Solo el Administrador de empresa puede rechazar la liquidación.");

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

        if (period.Status == ExpensePeriodStatus.Published)
        {
            return BadRequest("No se puede rechazar una liquidación ya publicada.");
        }

        var settlement = await dbContext.ExpenseSettlements
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == id, cancellationToken);

        if (settlement is null)
        {
            return BadRequest("El periodo todavia no tiene una liquidacion calculada.");
        }

        if (settlement.Status != ExpenseSettlementStatus.Approved)
        {
            return BadRequest("Solo se puede rechazar una liquidación que ya fue aprobada por el Encargado de edificio.");
        }

        var reason = request.RejectionReason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BadRequest("El motivo de rechazo es obligatorio.");
        }

        if (reason.Length > 500)
        {
            return BadRequest("El motivo no puede superar los 500 caracteres.");
        }

        var approvedByUserId = settlement.ApprovedByUserId;

        var settlementCharges = await dbContext.ExpenseCharges
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == id && x.SourceSettlementId != null)
            .ToListAsync(cancellationToken);

        foreach (var charge in settlementCharges)
        {
            charge.IsDeleted = true;
        }

        settlement.Status = ExpenseSettlementStatus.Rejected;
        settlement.RejectionReason = reason;
        settlement.RejectedAtUtc = DateTime.UtcNow;
        settlement.RejectedByUserId = tenantContext.UserId;
        settlement.ApprovedAtUtc = null;
        settlement.ApprovedByUserId = null;
        settlement.PresidentApprovedAtUtc = null;
        settlement.PresidentApprovedByUserId = null;
        settlement.PresidentRejectionReason = string.Empty;
        settlement.PresidentRejectedAtUtc = null;
        settlement.PresidentRejectedByUserId = null;
        period.Status = ExpensePeriodStatus.Draft;

        var settlementRejectedTitle = "La liquidación fue rechazada";
        var settlementRejectedBody = $"La liquidación del periodo {period.Name} fue rechazada por el Administrador de empresa. Motivo: {reason}.";

        if (approvedByUserId.HasValue)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = period.CompanyId,
                RecipientId = approvedByUserId.Value,
                Type = NotificationType.SettlementRejected,
                Title = settlementRejectedTitle,
                Body = settlementRejectedBody,
                EntityType = "ExpensePeriod",
                EntityId = period.Id
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (approvedByUserId.HasValue)
            await pushDispatcher.NotifyUserAsync(approvedByUserId.Value, settlementRejectedTitle, settlementRejectedBody, "ExpensePeriod", period.Id, cancellationToken);
        return Ok(await BuildSettlementSummaryAsync(period, cancellationToken));
    }

    [HttpPost("{id:guid}/apply-late-fees")]
    public async Task<ActionResult<ApplyLateFeesResultDto>> ApplyLateFees(
        Guid id,
        [FromBody] ApplyLateFeesRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanManageSettlement())
            return Prohibited("Solo el Encargado de edificio o el Administrador de empresa pueden realizar esta acción.");

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
        if (!CanManageSettlement())
            return Prohibited("Solo el Encargado de edificio o el Administrador de empresa pueden realizar esta acción.");

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
        settlement.PresidentApprovedAtUtc = null;
        settlement.PresidentApprovedByUserId = null;
        settlement.PresidentRejectionReason = string.Empty;
        settlement.PresidentRejectedAtUtc = null;
        settlement.PresidentRejectedByUserId = null;
        period.Status = ExpensePeriodStatus.Draft;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new VoidSettlementResultDto
        {
            ExpensePeriodName = period.Name,
            DeletedChargeCount = settlementCharges.Count
        });
    }

    // Firma al pie: izquierda quien aprobo la liquidacion, derecha quien la publico (presidente).
    // Se usa la firma precargada del usuario; si publica el mismo que aprobo, se muestra una sola vez.
    private async Task<(SettlementSignature? Approver, SettlementSignature? President, SettlementSignature? Publisher)> LoadSettlementSignaturesAsync(
        Guid periodId, CancellationToken cancellationToken)
    {
        var settlement = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == periodId, cancellationToken);

        if (settlement is null) return (null, null, null);

        var userIds = new List<Guid>();
        if (settlement.ApprovedByUserId.HasValue) userIds.Add(settlement.ApprovedByUserId.Value);
        if (settlement.PresidentApprovedByUserId.HasValue) userIds.Add(settlement.PresidentApprovedByUserId.Value);
        if (settlement.PublishedByUserId.HasValue) userIds.Add(settlement.PublishedByUserId.Value);

        var users = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        SettlementSignature? Build(Guid? userId, string? titleOverride = null)
        {
            if (!userId.HasValue || !users.TryGetValue(userId.Value, out var user)) return null;

            var name = string.IsNullOrWhiteSpace(user.FullName)
                ? $"{user.FirstName} {user.LastName}".Trim()
                : user.FullName;
            var title = titleOverride ?? user.Role switch
            {
                UserRole.CompanyAdmin => "Administrador de la empresa",
                UserRole.BuildingManager => "Encargado de edificio",
                UserRole.CompanyOperator => "Operador de empresa",
                _ => "Administrador"
            };

            return new SettlementSignature(name, title, ReadSignatureImage(user.SignatureUrl));
        }

        var approver = Build(settlement.ApprovedByUserId);
        var president = Build(settlement.PresidentApprovedByUserId, "Presidente del consorcio");
        var publisher = Build(settlement.PublishedByUserId);
        return (approver, president, publisher);
    }

    private byte[]? ReadSignatureImage(string? signatureUrl)
    {
        // Las firmas se suben a /uploads (URL relativa o absoluta): se lee de la carpeta local, png/jpg/webp.
        return TemplateImageReader.Read(env.WebRootPath, signatureUrl);
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

        // Quien solo accede por ser propietario/inquilino de una unidad no ve la liquidacion
        // hasta que el periodo este publicado.
        if (period.Status != ExpensePeriodStatus.Published
            && !await accessScope.CanAccessBuildingAsync(period.BuildingId, cancellationToken))
        {
            return NotFound();
        }

        var summary = await BuildSettlementSummaryAsync(period, cancellationToken);

        if (!summary.IsCalculated)
        {
            return BadRequest("El período todavía no tiene una liquidación calculada para exportar.");
        }

        var (approverSignature, presidentSignature, publisherSignature) = await LoadSettlementSignaturesAsync(id, cancellationToken);

        // Detalle linea por linea (proveedor, concepto, monto) para la planilla sobre el modelo propio.
        await LoadSettlementLinesAsync(summary, id, cancellationToken);
        summary.IncomeTreatment = period.Building?.IncomeTreatment ?? IncomeTreatment.CreditToOwners;
        summary.ReservePercentage = period.Building?.ReserveFundPercentage;
        summary.ExtraordinaryPercentage = period.Building?.ExtraordinaryPercentage;
        summary.PeriodYear = period.Year;
        summary.PeriodMonth = period.Month;

        // Si el edificio tiene su propio modelo de liquidacion (cargado por el superadmin) la liquidacion se
        // imprime sobre ese papel; sin modelo propio (o si el archivo ya no esta) sale con el diseno estandar.
        var settlementTemplate = period.Building is { UseStandardTemplates: false }
            ? TemplateImageReader.Read(env.WebRootPath, period.Building.SettlementTemplateUrl)
            : null;

        var document = new SettlementPdfDocument(
            summary,
            period.StartDate.ToString("dd/MM/yyyy"),
            period.EndDate.ToString("dd/MM/yyyy"),
            period.DueDate.ToString("dd/MM/yyyy"),
            approverSignature,
            presidentSignature,
            publisherSignature,
            standardTemplate: settlementTemplate is null,
            backgroundImage: settlementTemplate,
            fieldPositionsJson: period.Building?.SettlementFieldPositionsJson,
            hideFrame: period.Building?.SettlementHideFrame ?? true);

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

    private async Task<ExpenseSettlementSummaryDto> BuildSettlementSummaryAsync(ExpensePeriod period, CancellationToken cancellationToken)
    {
        var settlement = await dbContext.ExpenseSettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExpensePeriodId == period.Id, cancellationToken);

        if (settlement is null)
        {
            return await BuildLiveSettlementSummaryAsync(period, cancellationToken);
        }

        var userIds = new[]
            {
                settlement.GeneratedByUserId, settlement.ApprovedByUserId, settlement.PublishedByUserId, settlement.RejectedByUserId,
                settlement.PresidentApprovedByUserId, settlement.PresidentRejectedByUserId, period.Building?.PresidentUserId
            }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        var userNames = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
        UserRole? approverRole = settlement.ApprovedByUserId.HasValue
            ? await dbContext.ApplicationUsers.AsNoTracking()
                .Where(x => x.Id == settlement.ApprovedByUserId.Value)
                .Select(x => (UserRole?)x.Role)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

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
            // Se recalcula sin el Fondo operativo: las liquidaciones generadas antes de excluirlo guardan el total viejo.
            TotalBuildingIncomes = await dbContext.BuildingIncomes.AsNoTracking()
                .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id && x.Category != BuildingIncomeCategory.OperationalFund)
                .SumAsync(x => x.Amount, cancellationToken),
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
            ApprovedByRole = approverRole?.ToString() ?? string.Empty,
            PublishedAtUtc = settlement.PublishedAtUtc,
            PublishedByUserId = settlement.PublishedByUserId,
            PublishedByUserName = settlement.PublishedByUserId.HasValue
                ? userNames.GetValueOrDefault(settlement.PublishedByUserId.Value, string.Empty)
                : string.Empty,
            RejectionReason = settlement.RejectionReason,
            RejectedAtUtc = settlement.RejectedAtUtc,
            RejectedByUserId = settlement.RejectedByUserId,
            RejectedByUserName = settlement.RejectedByUserId.HasValue
                ? userNames.GetValueOrDefault(settlement.RejectedByUserId.Value, string.Empty)
                : string.Empty,
            PresidentUserId = period.Building?.PresidentUserId,
            PresidentUserName = period.Building?.PresidentUserId.HasValue == true
                ? userNames.GetValueOrDefault(period.Building.PresidentUserId!.Value, string.Empty)
                : string.Empty,
            PresidentApprovedAtUtc = settlement.PresidentApprovedAtUtc,
            PresidentApprovedByUserId = settlement.PresidentApprovedByUserId,
            PresidentApprovedByUserName = settlement.PresidentApprovedByUserId.HasValue
                ? userNames.GetValueOrDefault(settlement.PresidentApprovedByUserId.Value, string.Empty)
                : string.Empty,
            PresidentRejectionReason = settlement.PresidentRejectionReason,
            PresidentRejectedAtUtc = settlement.PresidentRejectedAtUtc,
            PresidentRejectedByUserId = settlement.PresidentRejectedByUserId,
            PresidentRejectedByUserName = settlement.PresidentRejectedByUserId.HasValue
                ? userNames.GetValueOrDefault(settlement.PresidentRejectedByUserId.Value, string.Empty)
                : string.Empty,
            Status = settlement.Status,
            PeriodStatus = period.Status,
            GeneratedChargeCount = generatedChargeCount,
            IsCalculated = true,
            CategoryTotals = await BuildCategoryTotalsAsync(period.Id, cancellationToken)
        };
    }

    // Mismos gastos que las categorias de la liquidacion (los que se reparten a las unidades), uno por linea;
    // los del fondo de reserva van en su propia columna de la planilla. Los ingresos del periodo van aparte.
    private async Task LoadSettlementLinesAsync(ExpenseSettlementSummaryDto summary, Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var expenses = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                     && x.ExpensePeriodId == expensePeriodId
                     && (x.DistributionType == BuildingExpenseDistributionType.ByCoefficient
                      || x.DistributionType == BuildingExpenseDistributionType.FixedPerUnit
                      || x.PaidByReserveFund))
            .Select(x => new { x.Category, x.SupplierName, x.Description, x.Amount, x.ExpenseDate, x.PaidByReserveFund })
            .ToListAsync(cancellationToken);

        summary.ExpenseLines = expenses
            .OrderBy(x => x.Category == BuildingExpenseCategory.ReserveFund ? 1 : 0)
            .ThenBy(x => x.Category)
            .ThenBy(x => x.ExpenseDate)
            .Select(x => new SettlementExpenseLineDto
            {
                Supplier = x.SupplierName,
                Description = x.Description,
                Amount = x.Amount,
                IsReserveFund = x.Category == BuildingExpenseCategory.ReserveFund,
                PaidByReserveFund = x.PaidByReserveFund,
                Category = x.Category.ToString()
            })
            .ToList();

        var incomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == expensePeriodId)
            .Select(x => new { x.Category, x.Description, x.Amount })
            .ToListAsync(cancellationToken);

        // Cada categoria de ingreso se imprime aparte (titulo y valor propios), no como filas del cuerpo.
        summary.IncomeTotals = incomes
            .GroupBy(x => x.Category)
            .ToDictionary(g => g.Key.ToString(), g => g.Sum(x => x.Amount));

        summary.IncomeDescriptions = incomes
            .GroupBy(x => x.Category)
            .ToDictionary(
                g => g.Key.ToString(),
                g => string.Join(" / ", g.Select(x => x.Description.Trim()).Where(x => x.Length > 0).Distinct()));
    }

    private async Task<List<SettlementCategoryTotalDto>> BuildCategoryTotalsAsync(Guid expensePeriodId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.BuildingExpenses
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                     && x.ExpensePeriodId == expensePeriodId
                     && !x.PaidByReserveFund
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

        // El Fondo operativo es informativo: no cuenta en los ingresos de la liquidacion (total para gastos).
        var incomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == period.Id && x.Category != BuildingIncomeCategory.OperationalFund)
            .ToListAsync(cancellationToken);

        // Lo pagado por el fondo de reserva no suma: ni al total de gastos ni a lo que se reparte a las unidades.
        var totalBuildingExpenses = expenses.Where(x => !x.PaidByReserveFund).Sum(x => x.Amount);
        var totalBuildingIncomes = incomes.Sum(x => x.Amount);
        // Si los ingresos van al fondo de reserva no reducen lo que se reparte entre las unidades.
        var config = await dbContext.Buildings.AsNoTracking()
            .Where(x => x.Id == period.BuildingId)
            .Select(x => new { x.IncomeTreatment, x.ReserveFundPercentage, x.ExtraordinaryPercentage })
            .FirstOrDefaultAsync(cancellationToken);
        var creditedIncomes = config?.IncomeTreatment == IncomeTreatment.ToReserveFund ? 0m : totalBuildingIncomes;

        // Mismos aportes que genera el reparto de cargos (base: gastos comunes que se reparten, sin los de la
        // categoria Fondo de reserva).
        var commonExpenses = expenses
            .Where(x => x.DistributionType is BuildingExpenseDistributionType.ByCoefficient or BuildingExpenseDistributionType.FixedPerUnit
                        && x.Category != BuildingExpenseCategory.ReserveFund
                        && !x.PaidByReserveFund)
            .Sum(x => x.Amount);
        var contributions = SettlementContributions.Compute(
            commonExpenses, creditedIncomes, config?.ReserveFundPercentage, config?.ExtraordinaryPercentage);
        var reserveFundAmount = expenses
            .Where(x => x.Category == BuildingExpenseCategory.ReserveFund && !x.PaidByReserveFund)
            .Sum(x => x.Amount);
        // Si los ingresos van al fondo de reserva, lo componen todas las categorias menos Fondo operativo.
        // No es un cargo a las unidades: solo engrosa el valor del fondo.
        var reserveFundIncomes = config?.IncomeTreatment == IncomeTreatment.ToReserveFund ? totalBuildingIncomes : 0m;
        // Lo que pago el fondo de reserva se descuenta del fondo (y no suma a nada de lo que se cobra).
        var paidByReserveFund = expenses.Where(x => x.PaidByReserveFund).Sum(x => x.Amount);
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
            ReserveFundAmount = SettlementContributions.ReserveFundBalance(
                reserveFundAmount, contributions.ReserveContribution, reserveFundIncomes, paidByReserveFund),
            ExtraordinaryAmount = extraordinaryAmount + contributions.ExtraordinaryContribution,
            NetCommonAmount = totalBuildingExpenses - creditedIncomes
                              + contributions.ReserveContribution + contributions.ExtraordinaryContribution,
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
            Notes = e.Notes,
            PaidByReserveFund = e.PaidByReserveFund,
            LedgerCategoryId = e.LedgerCategoryId
        }).ToList();

        if (copiedExpenses.Count > 0)
        {
            dbContext.BuildingExpenses.AddRange(copiedExpenses);
        }

        // Ingresos: se copian todos menos el Saldo acumulado, que no se repite tal cual sino que arranca del valor con
        // el que cerro el periodo anterior (el mismo calculo del arrastre de saldo).
        var sourceIncomes = await dbContext.BuildingIncomes
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ExpensePeriodId == source.Id && x.Category != BuildingIncomeCategory.AccumulatedBalance)
            .ToListAsync(cancellationToken);

        var copiedIncomes = sourceIncomes.Select(i => new BuildingIncome
        {
            CompanyId = i.CompanyId,
            BuildingId = i.BuildingId,
            ExpensePeriodId = cloned.Id,
            Category = i.Category,
            Description = i.Description,
            IncomeDate = startDate,
            Amount = i.Amount,
            Notes = i.Notes,
            LedgerCategoryId = i.LedgerCategoryId
        }).ToList();

        var accumulatedBalance = 0m;
        if (source.Building is not null)
        {
            var closing = await PeriodClosingBalance.ComputeAsync(dbContext, source.Building, source.Id, cancellationToken);
            if (closing.Saldo > 0)
            {
                accumulatedBalance = closing.Saldo;
                copiedIncomes.Add(new BuildingIncome
                {
                    CompanyId = source.CompanyId,
                    BuildingId = source.BuildingId,
                    ExpensePeriodId = cloned.Id,
                    Category = BuildingIncomeCategory.AccumulatedBalance,
                    Description = $"Saldo anterior período {source.Name}",
                    IncomeDate = startDate,
                    Amount = closing.Saldo,
                    Notes = closing.Note
                });
            }
        }

        if (copiedIncomes.Count > 0)
        {
            dbContext.BuildingIncomes.AddRange(copiedIncomes);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CloneExpensePeriodResultDto
        {
            Period = ToDto(cloned, source.Building?.Name),
            CopiedExpenses = copiedExpenses.Count,
            CopiedIncomes = sourceIncomes.Count,
            AccumulatedBalance = accumulatedBalance
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

        return await dbContext.UnitResidents
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.EndDate == null && x.Unit != null && x.Unit.BuildingId == buildingId
                && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId == uid, cancellationToken);
    }

    // Al publicar, el saldo a favor de cada propietario del edificio se aplica solo a sus cargos
    // pendientes (del mas antiguo al mas reciente). Un fallo aqui no revierte la publicacion:
    // el propietario puede usar "Aplicar" manualmente.
    private async Task ApplyOwnerCreditsAsync(ExpensePeriod period, CancellationToken ct)
    {
        if (!OwnerCreditFeature.Enabled) return;

        var userIds = await GetBuildingUserIdsAsync(period.BuildingId, ct);
        if (userIds.Count == 0) return;

        var ownersWithCredit = await dbContext.OwnerCredits
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == period.CompanyId && x.Amount > 0 && userIds.Contains(x.OwnerId))
            .Select(x => x.OwnerId)
            .ToListAsync(ct);

        foreach (var ownerId in ownersWithCredit)
        {
            try
            {
                await ownerCredits.ApplyCreditAsync(ownerId, period.CompanyId, CreditApplyMode.Automatic, period.Name, ct);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "No se pudo aplicar automaticamente el saldo a favor del propietario {OwnerId} al publicar el periodo {PeriodId}.", ownerId, period.Id);
            }
        }
    }

    private async Task NotifyBuildingUsersAsync(ExpensePeriod period, CancellationToken ct)
    {
        var recipientIds = await GetBuildingUserIdsAsync(period.BuildingId, ct);
        if (recipientIds.Count == 0) return;

        const string publishedTitle = "Nuevo periodo de expensas publicado";
        var publishedBody = period.Name;

        foreach (var rid in recipientIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                CompanyId = period.CompanyId,
                RecipientId = rid,
                Type = NotificationType.ExpensePeriodPublished,
                Title = publishedTitle,
                Body = publishedBody,
                EntityType = "ExpensePeriod",
                EntityId = period.Id
            });
        }

        await dbContext.SaveChangesAsync(ct);
        await pushDispatcher.NotifyUsersAsync(recipientIds, publishedTitle, publishedBody, "ExpensePeriod", period.Id, ct);
    }

    private async Task<List<Guid>> GetBuildingUserIdsAsync(Guid buildingId, CancellationToken ct)
    {
        var ownerIds = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId)
            .Select(x => x.OwnerId)
            .ToListAsync(ct);

        var residentUserIds = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                     && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == buildingId
                     && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId != null)
            .Select(x => x.Resident!.ApplicationUserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return ownerIds.Concat(residentUserIds).Distinct().ToList();
    }
}
