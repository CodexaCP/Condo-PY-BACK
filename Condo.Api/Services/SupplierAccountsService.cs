using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Cuentas por pagar y por cobrar por proveedor de un edificio. Las por pagar son las facturas con vencimiento sin pago registrado; las
/// por cobrar son las notas de credito del proveedor sobre gastos ya pagados que todavia no se recuperaron (dinero por devolver o
/// descuento en su proxima factura). Los importes se suman en memoria, como el resto de los reportes.
/// </summary>
public class SupplierAccountsService(ICondoDbContext dbContext)
{
    public const string StatusPending = "pending";
    public const string StatusRecovered = "recovered";
    public const string StatusAll = "all";

    public async Task<SupplierAccountsSummaryDto> SummaryAsync(Guid buildingId, DateOnly today, CancellationToken ct)
    {
        var payables = (await dbContext.BuildingExpenses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.DueDate != null && x.PaidAt == null)
                .Select(x => new { x.SupplierId, x.SupplierName, x.Amount, DueDate = x.DueDate!.Value })
                .ToListAsync(ct))
            .Select(x => new { Key = BuildingExpenseCreditNoteKeys.Normalize(x.SupplierName), x.SupplierId, x.SupplierName, x.Amount, x.DueDate })
            .ToList();

        var receivables = await ReceivablesAsync(buildingId, null, StatusPending, today, ct);

        var rows = new Dictionary<string, SupplierAccountRowDto>();
        SupplierAccountRowDto Row(string key, string name, Guid? supplierId)
        {
            if (!rows.TryGetValue(key, out var row))
            {
                row = new SupplierAccountRowDto { SupplierKey = key, SupplierName = name, SupplierId = supplierId };
                rows[key] = row;
            }
            else if (row.SupplierId is null && supplierId is not null)
            {
                row.SupplierId = supplierId;
            }

            return row;
        }

        foreach (var p in payables)
        {
            var row = Row(p.Key, p.SupplierName, p.SupplierId);
            row.PayableCount++;
            row.PayableTotal += p.Amount;
            if (p.DueDate < today) row.PayableOverdueTotal += p.Amount;
        }

        foreach (var r in receivables)
        {
            var row = Row(r.SupplierKey, r.SupplierName, null);
            row.ReceivableCount++;
            if (r.RecoveryType == SupplierRecoveryType.Refund) row.PendingRefundTotal += r.PendingAmount;
            else row.PendingDiscountTotal += r.PendingAmount;
            row.ReceivableTotal += r.PendingAmount;
        }

        foreach (var row in rows.Values) row.NetBalance = row.PayableTotal - row.ReceivableTotal;

        var suppliers = rows.Values.OrderBy(r => r.SupplierName, StringComparer.OrdinalIgnoreCase).ToList();
        return new SupplierAccountsSummaryDto
        {
            BuildingId = buildingId,
            PayableTotal = suppliers.Sum(r => r.PayableTotal),
            PayableOverdueTotal = suppliers.Sum(r => r.PayableOverdueTotal),
            PendingRefundTotal = suppliers.Sum(r => r.PendingRefundTotal),
            PendingDiscountTotal = suppliers.Sum(r => r.PendingDiscountTotal),
            ReceivableTotal = suppliers.Sum(r => r.ReceivableTotal),
            Suppliers = suppliers
        };
    }

    /// <summary>Notas de credito de proveedor con cuenta por cobrar de un edificio (pendientes, recuperadas o todas), con lo que falta de cada una.</summary>
    public async Task<List<SupplierReceivableRowDto>> ReceivablesAsync(
        Guid buildingId, string? supplierName, string status, DateOnly today, CancellationToken ct)
    {
        var query = dbContext.BuildingExpenseCreditNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == BuildingExpenseCreditNoteStatus.Applied
                        && x.RecoveryType != SupplierRecoveryType.None);

        query = status switch
        {
            StatusPending => query.Where(x => x.RecoveryStatus == SupplierRecoveryStatus.Pending),
            StatusRecovered => query.Where(x => x.RecoveryStatus == SupplierRecoveryStatus.Recovered),
            _ => query
        };

        var supplierKey = string.IsNullOrWhiteSpace(supplierName) ? null : BuildingExpenseCreditNoteKeys.Normalize(supplierName);
        if (supplierKey is not null) query = query.Where(x => x.SupplierKey == supplierKey);

        var notes = await query
            .OrderBy(x => x.IssueDate).ThenBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.BuildingExpenseId, x.SupplierKey, x.SupplierName, x.Numero, x.Timbrado, x.IssueDate, x.Amount,
                x.RecoveryType, x.RecoveryStatus, x.RefundedOn, x.RefundReference,
                ExpenseDescription = x.BuildingExpense != null ? x.BuildingExpense.Description : string.Empty,
                PeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty
            })
            .ToListAsync(ct);

        var ids = notes.Select(n => n.Id).ToList();
        var applied = ids.Count == 0
            ? new Dictionary<Guid, decimal>()
            : (await dbContext.BuildingExpenseCreditNoteDiscounts.AsNoTracking()
                    .Where(d => !d.IsDeleted && ids.Contains(d.CreditNoteId))
                    .Select(d => new { d.CreditNoteId, d.Amount })
                    .ToListAsync(ct))
                .GroupBy(d => d.CreditNoteId)
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));

        return notes.Select(n =>
        {
            var discount = applied.GetValueOrDefault(n.Id);
            var pending = n.RecoveryStatus == SupplierRecoveryStatus.Pending
                ? n.RecoveryType == SupplierRecoveryType.NextInvoiceDiscount ? Math.Max(n.Amount - discount, 0m) : n.Amount
                : 0m;
            return new SupplierReceivableRowDto
            {
                CreditNoteId = n.Id,
                BuildingExpenseId = n.BuildingExpenseId,
                SupplierKey = n.SupplierKey,
                SupplierName = n.SupplierName,
                Numero = n.Numero,
                Timbrado = n.Timbrado,
                IssueDate = n.IssueDate,
                ExpenseDescription = n.ExpenseDescription,
                ExpensePeriodName = n.PeriodName,
                Amount = n.Amount,
                RecoveryType = n.RecoveryType,
                RecoveryStatus = n.RecoveryStatus,
                DiscountApplied = discount,
                PendingAmount = pending,
                RefundedOn = n.RefundedOn,
                RefundReference = n.RefundReference,
                DaysPending = pending > 0m ? Math.Max(today.DayNumber - n.IssueDate.DayNumber, 0) : 0
            };
        }).ToList();
    }

    /// <summary>Descuentos acordados con un proveedor que todavia no se aplicaron del todo: se avisa al cargar su proxima factura.</summary>
    public async Task<SupplierPendingDiscountDto> PendingDiscountAsync(Guid buildingId, string supplierName, DateOnly today, CancellationToken ct)
    {
        var notes = (await ReceivablesAsync(buildingId, supplierName, StatusPending, today, ct))
            .Where(n => n.RecoveryType == SupplierRecoveryType.NextInvoiceDiscount && n.PendingAmount > 0m)
            .ToList();

        return new SupplierPendingDiscountDto
        {
            SupplierName = supplierName.Trim(),
            PendingTotal = notes.Sum(n => n.PendingAmount),
            Notes = notes
        };
    }
}
