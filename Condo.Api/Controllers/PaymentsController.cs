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
public class PaymentsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> GetAll(
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? expensePeriodId,
        [FromQuery] Guid? unitId,
        [FromQuery] bool legacyOnly,
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

        // Historico: solo los pagos cargados a mano antes del cambio (los de pagos de propietario / registrados
        // por el sistema se ven en Pagos de propietarios, con su misma referencia PAY-).
        if (legacyOnly)
        {
            query = query.Where(x => !dbContext.OwnerPayments.Any(o =>
                !o.IsDeleted && o.CompanyId == x.CompanyId && o.Reference == x.Reference));
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


    // Los pagos ya no se cargan ni se editan aqui: se registran desde "Pagos" (OwnerPaymentsController.Register),
    // que sigue el mismo camino que un pago aprobado desde la app. Este controlador queda para consultar el
    // historico y para revertir un pago de los que se cargaron a mano antes de ese cambio.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!accessScope.IsSuperAdmin) return Forbid();

        var entity = await dbContext.Payments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        if (entity.IsReversed)
            return Conflict("El pago ya fue revertido.");

        // Un pago que salio de un pago de propietario se revierte desde ese pago (o con una nota de credito).
        var belongsToOwnerPayment = await dbContext.OwnerPayments.AnyAsync(
            o => !o.IsDeleted && o.CompanyId == entity.CompanyId && o.Reference == entity.Reference, cancellationToken);
        if (belongsToOwnerPayment)
            return Conflict("Este pago proviene de un pago de propietario: se revierte desde Pagos › Hechos por el sistema, o con una nota de crédito.");

        var hasInvoice = await dbContext.Invoices.AnyAsync(
            i => !i.IsDeleted && i.PaymentId == entity.Id && i.Status != InvoiceStatus.Voided, cancellationToken);
        if (hasInvoice)
            return Conflict("Este pago tiene una factura vigente. Anule la factura antes de revertir el pago.");

        var unit = await dbContext.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == entity.UnitId, cancellationToken);

        if (unit is null) return BadRequest("La unidad no existe.");

        // Cierre contable de un mes (Centro de configuracion): revertir cambia la caja del mes en que se hizo el pago.
        var closedMonth = await new Condo.Api.Services.FinancePeriodGuard(dbContext).FindClosedForPaymentsAsync([entity], cancellationToken);
        if (closedMonth is not null)
            return Condo.Api.Services.FinancePeriodGuard.ClosedResponse(closedMonth);

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
}
