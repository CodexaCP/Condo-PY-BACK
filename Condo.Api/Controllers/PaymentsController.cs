using Condo.Api.Documents;
using Condo.Api.Services;
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
[Route("api/payments")]
public class PaymentsController(ICondoDbContext dbContext, IAccessScopeService accessScope, ComprobanteService comprobantes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? expensePeriodId,
        [FromQuery] Guid? unitId,
        CancellationToken cancellationToken)
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
                AllocatedAmount = dbContext.PaymentAllocations
                    .Where(a => !a.IsDeleted && a.PaymentId == x.Id)
                    .Sum(a => (decimal?)a.AllocatedAmount) ?? 0m,
                Method = x.Method,
                Reference = x.Reference,
                Notes = x.Notes,
                IsReversed = x.IsReversed,
                ReversedAt = x.ReversedAt,
                Allocations = dbContext.PaymentAllocations
                    .Where(a => !a.IsDeleted && a.PaymentId == x.Id)
                    .Select(a => new PaymentAllocationDto
                    {
                        Id = a.Id,
                        ExpenseChargeId = a.ExpenseChargeId,
                        ChargeConcept = a.Charge != null ? a.Charge.Concept : string.Empty,
                        ChargeType = a.Charge != null ? a.Charge.ChargeType : ExpenseChargeType.Ordinary,
                        AllocatedAmount = a.AllocatedAmount
                    }).ToList()
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
                AllocatedAmount = dbContext.PaymentAllocations
                    .Where(a => !a.IsDeleted && a.PaymentId == x.Id)
                    .Sum(a => (decimal?)a.AllocatedAmount) ?? 0m,
                Method = x.Method,
                Reference = x.Reference,
                Notes = x.Notes,
                IsReversed = x.IsReversed,
                ReversedAt = x.ReversedAt,
                Allocations = dbContext.PaymentAllocations
                    .Where(a => !a.IsDeleted && a.PaymentId == x.Id)
                    .Select(a => new PaymentAllocationDto
                    {
                        Id = a.Id,
                        ExpenseChargeId = a.ExpenseChargeId,
                        ChargeConcept = a.Charge != null ? a.Charge.Concept : string.Empty,
                        ChargeType = a.Charge != null ? a.Charge.ChargeType : ExpenseChargeType.Ordinary,
                        AllocatedAmount = a.AllocatedAmount
                    }).ToList()
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

        var (contextError, period, unit) = await LoadAndValidateContextAsync(request, cancellationToken);
        if (contextError is not null) return contextError;

        // Un comprobante = un pago completo: se paga la totalidad de la unidad en el periodo o no se paga.
        var comprobante = await comprobantes.LoadUnitPeriodAsync(request.UnitId, request.ExpensePeriodId, unit!.CompanyId, cancellationToken);
        if (comprobante is null)
            return BadRequest("El comprobante de esta unidad y periodo no tiene deuda pendiente.");
        if (Math.Abs(request.Amount - comprobante.Total) > 0.01m)
            return BadRequest($"El pago debe cubrir la totalidad del comprobante: Gs. {ComprobanteService.Gs(comprobante.Total)}. " +
                              "No se aceptan pagos parciales ni por línea.");

        request.Allocations = comprobante.Lines
            .Select(l => new AllocationRequest { ExpenseChargeId = l.Charge.Id, Amount = l.Pending })
            .ToList();

        var entity = new Payment
        {
            CompanyId = unit!.CompanyId,
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

        if (request.Allocations.Count > 0)
        {
            await SaveAllocationsAsync(entity, request.Allocations, unit!.CompanyId, cancellationToken);
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id },
            await BuildDtoAsync(entity, period!.Name, unit!, cancellationToken));
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

        if (entity is null) return NotFound();

        if (entity.IsReversed)
            return Conflict("No se puede editar un pago revertido.");

        var (contextError, period, unit) = await LoadAndValidateContextAsync(request, cancellationToken);
        if (contextError is not null) return contextError;

        // El monto, la unidad y el periodo de un pago no se cambian (un pago es un comprobante completo).
        if (request.UnitId != entity.UnitId || request.ExpensePeriodId != entity.ExpensePeriodId
            || Math.Abs(request.Amount - entity.Amount) > 0.01m)
            return BadRequest("No se puede cambiar el monto, la unidad ni el periodo de un pago: revierta el pago y regístrelo de nuevo.");

        // Las imputaciones existentes se conservan.
        request.Allocations = new List<AllocationRequest>();

        entity.CompanyId = unit!.CompanyId;
        entity.ExpensePeriodId = request.ExpensePeriodId;
        entity.UnitId = request.UnitId;
        entity.PaymentDate = request.PaymentDate;
        entity.Amount = request.Amount;
        entity.Method = request.Method;
        entity.Reference = request.Reference.Trim();
        entity.Notes = request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDtoAsync(entity, period!.Name, unit!, cancellationToken));
    }

    [HttpGet("{id:guid}/receipt-pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadReceiptPdf(Guid id, [FromQuery(Name = "access_token")] string? accessToken, CancellationToken cancellationToken)
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
                AllocatedAmount = dbContext.PaymentAllocations
                    .Where(a => !a.IsDeleted && a.PaymentId == x.Id)
                    .Sum(a => (decimal?)a.AllocatedAmount) ?? 0m,
                Method = x.Method,
                Reference = x.Reference,
                Notes = x.Notes,
                IsReversed = x.IsReversed,
                ReversedAt = x.ReversedAt,
                Allocations = dbContext.PaymentAllocations
                    .Where(a => !a.IsDeleted && a.PaymentId == x.Id)
                    .Select(a => new PaymentAllocationDto
                    {
                        Id = a.Id,
                        ExpenseChargeId = a.ExpenseChargeId,
                        ChargeConcept = a.Charge != null ? a.Charge.Concept : string.Empty,
                        ChargeType = a.Charge != null ? a.Charge.ChargeType : ExpenseChargeType.Ordinary,
                        AllocatedAmount = a.AllocatedAmount
                    }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(payment.BuildingId, cancellationToken)) return Forbid();

        var document = new PaymentReceiptPdfDocument(payment);
        var pdfBytes = document.GeneratePdf();
        var fileName = $"pago_{payment.UnitCode}_{payment.PaymentDate:yyyyMMdd}_{payment.Id.ToString()[..8].ToUpper()}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Payments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        if (entity.IsReversed)
            return Conflict("El pago ya fue revertido.");

        var unit = await dbContext.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.UnitId, cancellationToken);

        if (unit is null) return BadRequest("La unidad no existe.");

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
            return Forbid();

        // Soft-delete allocations to free up the charges
        var allocations = await dbContext.PaymentAllocations
            .Where(a => !a.IsDeleted && a.PaymentId == id)
            .ToListAsync(cancellationToken);

        foreach (var alloc in allocations)
            alloc.IsDeleted = true;

        // Mark as reversed, not deleted — keeps audit trail
        entity.IsReversed = true;
        entity.ReversedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(ActionResult? Error, ExpensePeriod? Period, Unit? Unit)> LoadAndValidateContextAsync(
        PaymentUpsertRequest request, CancellationToken cancellationToken)
    {
        var period = await dbContext.ExpensePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.ExpensePeriodId, cancellationToken);

        if (period is null)
        {
            return (BadRequest("El periodo de expensas no existe."), null, null);
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .Include(x => x.Building)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.UnitId, cancellationToken);

        if (unit is null)
        {
            return (BadRequest("La unidad no existe."), null, null);
        }

        if (unit.BuildingId != period.BuildingId)
        {
            return (BadRequest("La unidad debe pertenecer al mismo edificio que el periodo."), null, null);
        }

        if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, cancellationToken))
        {
            return (Forbid(), null, null);
        }

        return (null, period, unit);
    }

    private async Task<ActionResult?> ValidateAllocationsAsync(
        PaymentUpsertRequest request,
        Unit unit,
        CancellationToken cancellationToken,
        Guid? excludePaymentId = null)
    {
        var totalAllocated = request.Allocations.Sum(a => a.Amount);
        if (totalAllocated > request.Amount)
        {
            return BadRequest($"El total asignado ({totalAllocated:N0}) supera el monto del pago ({request.Amount:N0}).");
        }

        foreach (var alloc in request.Allocations)
        {
            if (alloc.ExpenseChargeId == Guid.Empty || alloc.Amount <= 0)
            {
                return BadRequest("Cada asignación requiere un cargo válido y un monto mayor que cero.");
            }

            var charge = await dbContext.ExpenseCharges
                .AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == alloc.ExpenseChargeId, cancellationToken);

            if (charge is null)
            {
                return BadRequest($"El cargo {alloc.ExpenseChargeId} no existe.");
            }

            if (charge.UnitId != request.UnitId || charge.ExpensePeriodId != request.ExpensePeriodId)
            {
                return BadRequest("Todos los cargos deben pertenecer a la misma unidad y periodo que el pago.");
            }

            if (charge.IsReversal)
            {
                return BadRequest("No se puede asignar un pago a un cargo de reversión.");
            }

            var alreadyPaid = await dbContext.PaymentAllocations
                .Where(a => !a.IsDeleted && a.ExpenseChargeId == alloc.ExpenseChargeId &&
                            (excludePaymentId == null || a.PaymentId != excludePaymentId))
                .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;

            var available = charge.Amount - alreadyPaid;
            if (alloc.Amount > available + 0.01m)
            {
                return BadRequest($"El cargo '{charge.Concept}' tiene disponible {available:N0} pero se intenta asignar {alloc.Amount:N0}.");
            }
        }

        return null;
    }

    private async Task SaveAllocationsAsync(
        Payment payment,
        List<AllocationRequest> allocations,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        foreach (var alloc in allocations)
        {
            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                CompanyId = companyId,
                PaymentId = payment.Id,
                ExpenseChargeId = alloc.ExpenseChargeId,
                AllocatedAmount = alloc.Amount
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PaymentDto> BuildDtoAsync(
        Payment entity,
        string periodName,
        Unit unit,
        CancellationToken cancellationToken)
    {
        var allocations = await dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.PaymentId == entity.Id)
            .Select(a => new PaymentAllocationDto
            {
                Id = a.Id,
                ExpenseChargeId = a.ExpenseChargeId,
                ChargeConcept = a.Charge != null ? a.Charge.Concept : string.Empty,
                ChargeType = a.Charge != null ? a.Charge.ChargeType : ExpenseChargeType.Ordinary,
                AllocatedAmount = a.AllocatedAmount
            })
            .ToListAsync(cancellationToken);

        return new PaymentDto
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            ExpensePeriodId = entity.ExpensePeriodId,
            ExpensePeriodName = periodName,
            BuildingId = unit.BuildingId,
            BuildingName = unit.Building?.Name ?? string.Empty,
            UnitId = entity.UnitId,
            UnitCode = unit.Code,
            PaymentDate = entity.PaymentDate,
            Amount = entity.Amount,
            AllocatedAmount = allocations.Sum(a => a.AllocatedAmount),
            Method = entity.Method,
            Reference = entity.Reference,
            Notes = entity.Notes,
            IsReversed = entity.IsReversed,
            ReversedAt = entity.ReversedAt,
            Allocations = allocations
        };
    }

    private static bool IsValidRequest(PaymentUpsertRequest request, out string error)
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

        if (request.Amount <= 0)
        {
            error = "El monto debe ser mayor que cero.";
            return false;
        }

        if (request.PaymentDate == default)
        {
            error = "La fecha de pago es obligatoria.";
            return false;
        }

        if (request.Reference.Trim().Length > 100)
        {
            error = "La referencia no puede superar los 100 caracteres.";
            return false;
        }

        if (request.Notes.Trim().Length > 500)
        {
            error = "Las notas no pueden superar los 500 caracteres.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
