using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class BuildingExpense : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public BuildingExpenseCategory Category { get; set; } = BuildingExpenseCategory.Other;
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    // Monto que se reparte (neto). Con notas de credito del proveedor aplicadas es OriginalAmount menos su suma; sin ellas es el
    // monto del gasto. Todos los reportes y la liquidacion leen este campo, por eso ya viene neto.
    public decimal Amount { get; set; }
    // Monto facturado por el proveedor antes de las notas de credito. Null mientras el gasto no tenga ninguna aplicada.
    public decimal? OriginalAmount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; } = BuildingExpenseDistributionType.ByCoefficient;
    public Guid? TargetUnitId { get; set; }
    public string Notes { get; set; } = string.Empty;
    // El gasto lo paga el fondo de reserva (no la expensa): decide la columna de la planilla y queda para operaciones futuras.
    public bool PaidByReserveFund { get; set; }
    public string? ReceiptFileName { get; set; }
    public string? ReceiptStoredName { get; set; }
    // Rubro del plan de cuentas de Finanzas del edificio (opcional). Con rubro, Category sale del rubro; sin rubro, el
    // libro usa la categoria como siempre.
    public Guid? LedgerCategoryId { get; set; }

    // Proveedor elegido de la lista de la empresa (opcional): SupplierName queda con su nombre. Sin proveedor, SupplierName es texto libre.
    public Guid? SupplierId { get; set; }

    // Factura del proveedor (opcional): numero y timbrado, para el libro de compras.
    public string? InvoiceNumber { get; set; }
    public string? InvoiceTimbrado { get; set; }

    // Cuentas por pagar. Un gasto SIN vencimiento ni pago cargados cuenta en la caja en su fecha (como siempre). Con vencimiento queda
    // "a pagar": no entra a la caja hasta que se registra el pago (PaidAt), y entonces cuenta en esa fecha y desde PaidFromAccountId
    // (sin cuenta, la que le corresponde por defecto).
    public DateOnly? DueDate { get; set; }
    public DateOnly? PaidAt { get; set; }
    public Guid? PaidFromAccountId { get; set; }

    // IVA incluido en el monto del comprobante: tasa (10, 5 o 0 = exento). Sin tasa, el gasto no esta clasificado. El importe del IVA no se guarda:
    // se calcula del monto vigente (VatMath), asi acompana a las notas de credito del proveedor que bajan el monto.
    public decimal? VatRate { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ExpensePeriod? ExpensePeriod { get; set; }
    public Unit? TargetUnit { get; set; }
    public LedgerCategory? LedgerCategory { get; set; }
    public Supplier? Supplier { get; set; }
    public FinancialAccount? PaidFromAccount { get; set; }
    public ICollection<ExpenseCharge> ExpenseCharges { get; set; } = new List<ExpenseCharge>();
    public ICollection<BuildingExpenseCreditNote> CreditNotes { get; set; } = new List<BuildingExpenseCreditNote>();
}
