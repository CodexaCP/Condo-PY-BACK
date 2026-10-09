using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Libro de compras: los gastos con tasa de IVA (por fecha de la factura, no del pago) y las notas de credito del proveedor, con el IVA incluido
/// en cada comprobante discriminado por tasa. Un gasto sin tasa cuya cuenta no es "no corresponde" se cuenta aparte como "sin clasificar".
/// Una nota de credito aplicada va como renglon negativo con la tasa del gasto que corrige; asi el libro cuadra con el monto neto del gasto.
/// </summary>
public class FinanceVatService(ICondoDbContext dbContext)
{
    // Un libro no abarca mas de esto (evita consultas enormes).
    public const int MaxRangeDays = 400;

    public async Task<(VatPurchasesBookDto? Book, string? Error)> PurchasesAsync(
        Guid buildingId, string buildingName, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from) return (null, "La fecha desde no puede ser posterior a la fecha hasta.");
        if (to.DayNumber - from.DayNumber > MaxRangeDays) return (null, $"El libro no puede abarcar más de {MaxRangeDays} días.");

        var expenses = await dbContext.BuildingExpenses.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.ExpenseDate >= from && x.ExpenseDate <= to)
            .Select(x => new
            {
                x.Id, x.ExpenseDate, x.SupplierName, SupplierRuc = x.Supplier != null ? x.Supplier.Ruc : null,
                x.InvoiceTimbrado, x.InvoiceNumber, x.Description, x.Amount, x.OriginalAmount, x.VatRate,
                CategoryCode = x.LedgerCategory != null ? x.LedgerCategory.Code : null,
                CategoryName = x.LedgerCategory != null ? x.LedgerCategory.Name : null,
                Treatment = x.LedgerCategory != null ? x.LedgerCategory.VatTreatment : null
            })
            .ToListAsync(ct);

        var rows = new List<VatPurchaseRowDto>();
        var unclassifiedCount = 0;
        var unclassifiedTotal = 0m;
        foreach (var e in expenses)
        {
            if (e.VatRate is null)
            {
                if (e.Treatment != VatTreatment.NotApplicable)
                {
                    unclassifiedCount++;
                    unclassifiedTotal += e.Amount;
                }

                continue;
            }

            // La factura del proveedor, por su monto original (antes de las notas de credito, que van como renglones propios).
            var total = e.OriginalAmount ?? e.Amount;
            rows.Add(Row(e.ExpenseDate, e.Id, e.SupplierName, e.SupplierRuc, e.InvoiceTimbrado, e.InvoiceNumber, false, e.Description,
                e.CategoryCode, e.CategoryName, total, e.VatRate.Value));
        }

        var notes = await dbContext.BuildingExpenseCreditNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == BuildingExpenseCreditNoteStatus.Applied
                        && x.IssueDate >= from && x.IssueDate <= to
                        && x.BuildingExpense != null && !x.BuildingExpense.IsDeleted && x.BuildingExpense.VatRate != null)
            .Select(x => new
            {
                x.Id, x.IssueDate, x.BuildingExpenseId, x.SupplierName, x.Numero, x.Timbrado, x.Amount, x.Reason,
                SupplierRuc = x.BuildingExpense!.Supplier != null ? x.BuildingExpense.Supplier.Ruc : null,
                VatRate = x.BuildingExpense.VatRate!.Value,
                CategoryCode = x.BuildingExpense.LedgerCategory != null ? x.BuildingExpense.LedgerCategory.Code : null,
                CategoryName = x.BuildingExpense.LedgerCategory != null ? x.BuildingExpense.LedgerCategory.Name : null
            })
            .ToListAsync(ct);
        foreach (var n in notes)
        {
            rows.Add(Row(n.IssueDate, n.BuildingExpenseId, n.SupplierName, n.SupplierRuc, n.Timbrado, n.Numero, true,
                $"Nota de crédito: {n.Reason}", n.CategoryCode, n.CategoryName, -n.Amount, n.VatRate));
        }

        rows = rows.OrderBy(r => r.Date).ThenBy(r => r.SupplierName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.DocumentNumber).ToList();

        var totals = new[] { 10m, 5m, 0m }.Select(rate =>
        {
            var own = rows.Where(r => r.VatRate == rate).ToList();
            return new VatRateTotalsDto
            {
                Rate = rate,
                Count = own.Count,
                Total = own.Sum(r => r.Total),
                Base = own.Sum(r => r.Base),
                Vat = own.Sum(r => r.Vat)
            };
        }).ToList();

        return (new VatPurchasesBookDto
        {
            BuildingId = buildingId,
            BuildingName = buildingName,
            From = from,
            To = to,
            Rows = rows,
            TotalsByRate = totals,
            GrandTotal = totals.Sum(t => t.Total),
            GrandBase = totals.Sum(t => t.Base),
            GrandVat = totals.Sum(t => t.Vat),
            UnclassifiedCount = unclassifiedCount,
            UnclassifiedTotal = unclassifiedTotal
        }, null);
    }

    private static VatPurchaseRowDto Row(
        DateOnly date, Guid expenseId, string supplierName, string? ruc, string? timbrado, string? number, bool isCreditNote,
        string description, string? categoryCode, string? categoryName, decimal total, decimal rate)
    {
        var vat = VatMath.VatOf(total, rate) ?? 0m;
        return new VatPurchaseRowDto
        {
            Date = date,
            ExpenseId = expenseId,
            SupplierName = supplierName,
            SupplierRuc = ruc,
            Timbrado = timbrado,
            DocumentNumber = number,
            IsCreditNote = isCreditNote,
            Description = description,
            LedgerCategoryCode = categoryCode,
            LedgerCategoryName = categoryName,
            Total = total,
            VatRate = rate,
            Base = total - vat,
            Vat = vat
        };
    }
}
