using Condo.Application.Abstractions;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Una nota de credito del proveedor que los reportes restan del gasto en la fecha de la nota.</summary>
public sealed record SupplierCreditEntry(
    Guid Id, Guid ExpenseId, Guid BuildingId, DateOnly Date, decimal Amount, string Numero, string SupplierName,
    string ExpenseDescription, BuildingExpenseCategory Category, bool PaidByReserveFund, Guid? LedgerCategoryId);

/// <summary>
/// Notas de credito de proveedor que cuentan como un movimiento propio en los reportes (libro, estado de resultados, flujo, presupuesto
/// y comparativo): las de un periodo ya publicado que NO se pasaron a los propietarios (gasto pagado por el fondo de reserva o no
/// distribuido): el dinero queda en el edificio y se registra con la fecha de la nota, sin reescribir el mes del gasto.
///
/// No entran:
///  - Las de periodo en borrador: ya bajaron el monto del gasto (BuildingExpense.Amount es el neto), los reportes lo leen asi.
///  - Las acreditadas a los propietarios como saldo a favor: ese saldo ya entra a los reportes cuando el propietario lo usa en un pago
///    (el cobro del comprobante incluye la parte pagada con saldo). Contarlas tambien aca duplicaria el beneficio.
/// </summary>
public static class SupplierCreditNoteLedger
{
    public static async Task<List<SupplierCreditEntry>> LoadAsync(
        ICondoDbContext dbContext, IReadOnlyCollection<Guid> buildingIds, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (buildingIds.Count == 0) return new List<SupplierCreditEntry>();

        var query = dbContext.BuildingExpenseCreditNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == BuildingExpenseCreditNoteStatus.Applied && x.Mode == BuildingExpenseCreditNoteMode.Credited
                        && buildingIds.Contains(x.BuildingId)
                        && !dbContext.BuildingExpenseCreditNoteAllocations.Any(a => !a.IsDeleted && a.CreditNoteId == x.Id));

        if (from.HasValue) query = query.Where(x => x.IssueDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.IssueDate <= to.Value);

        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.BuildingExpenseId,
                x.BuildingId,
                x.IssueDate,
                x.Amount,
                x.Numero,
                x.SupplierName,
                Description = x.BuildingExpense!.Description,
                Category = x.BuildingExpense.Category,
                PaidByReserveFund = x.BuildingExpense.PaidByReserveFund,
                LedgerCategoryId = x.BuildingExpense.LedgerCategoryId
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new SupplierCreditEntry(
                x.Id, x.BuildingExpenseId, x.BuildingId, x.IssueDate, x.Amount, x.Numero, x.SupplierName, x.Description,
                x.Category, x.PaidByReserveFund, x.LedgerCategoryId))
            .ToList();
    }
}
