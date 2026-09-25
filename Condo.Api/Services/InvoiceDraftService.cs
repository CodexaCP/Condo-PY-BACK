using System.Text.Json;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Genera los borradores de factura (una por comprobante: unidad + periodo) de un pago de
/// propietario ya aprobado. Nunca emite ni numera nada — eso sigue siendo una accion manual
/// aparte (InvoicesController.Emit), porque ahi se elige el timbrado y se consume un numero
/// real sobre el papel preimpreso.
/// </summary>
public sealed class InvoiceDraftService(ICondoDbContext dbContext, IAccessScopeService accessScope)
{
    public async Task<List<Guid>> CreateDraftsFromOwnerPaymentAsync(OwnerPayment ownerPayment, Guid actingUserId, CancellationToken ct)
    {
        var payments = await dbContext.Payments
            .AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => !x.IsDeleted && !x.IsReversed && x.CompanyId == ownerPayment.CompanyId
                        && x.Reference == ownerPayment.Reference && x.Unit != null)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var created = new List<Guid>();

        // Una factura por comprobante (unidad + periodo); agrupar evita una factura por cada linea.
        foreach (var unitGroup in payments.GroupBy(x => (x.UnitId, x.ExpensePeriodId)))
        {
            var unit = unitGroup.First().Unit!;
            if (!await accessScope.CanAccessBuildingAsync(unit.BuildingId, ct)) continue;

            var paymentIds = unitGroup.Select(x => x.Id).ToList();
            var alreadyInvoiced = await dbContext.Invoices.AnyAsync(x =>
                !x.IsDeleted && x.Status != InvoiceStatus.Voided && paymentIds.Contains(x.PaymentId), ct);
            if (alreadyInvoiced) continue;

            var allocations = await dbContext.PaymentAllocations
                .AsNoTracking()
                .Where(a => !a.IsDeleted && paymentIds.Contains(a.PaymentId))
                .OrderBy(a => a.Charge!.ExpensePeriod!.Year).ThenBy(a => a.Charge!.ExpensePeriod!.Month).ThenBy(a => a.Charge!.Concept)
                .Select(a => new InvoiceLineDto
                {
                    Concepto = a.Charge != null ? a.Charge.Concept : "Cargo",
                    ChargeType = a.Charge != null ? a.Charge.ChargeType : (ExpenseChargeType?)null,
                    Monto = a.AllocatedAmount
                })
                .ToListAsync(ct);

            var total = unitGroup.Sum(x => x.Amount);
            var remainder = total - allocations.Sum(a => a.Monto);
            if (remainder > 0.01m)
                allocations.Add(new InvoiceLineDto { Concepto = "Saldo a cuenta / crédito", Monto = remainder });

            var entity = new Invoice
            {
                CompanyId = ownerPayment.CompanyId,
                BuildingId = unit.BuildingId,
                UnitId = unit.Id,
                PaymentId = unitGroup.First().Id,
                OwnerPaymentId = ownerPayment.Id,
                Status = InvoiceStatus.Draft,
                MontoTotal = total,
                DetalleSnapshotJson = JsonSerializer.Serialize(allocations),
                CreatedByUserId = actingUserId
            };

            dbContext.Invoices.Add(entity);
            await dbContext.SaveChangesAsync(ct);

            dbContext.InvoiceAuditLogs.Add(new InvoiceAuditLog
            {
                CompanyId = entity.CompanyId,
                InvoiceId = entity.Id,
                Action = InvoiceAuditAction.DraftCreated,
                UserId = actingUserId,
                TimestampUtc = DateTime.UtcNow,
                DatosAntesJson = null,
                DatosDespuesJson = JsonSerializer.Serialize(new { entity.Id, entity.OwnerPaymentId, entity.UnitId, entity.MontoTotal, entity.Status }),
                Detalle = $"Borrador creado automáticamente al aprobar el pago {ownerPayment.Reference} (comprobante de la unidad {unit.Code})."
            });
            await dbContext.SaveChangesAsync(ct);

            created.Add(entity.Id);
        }

        return created;
    }
}
