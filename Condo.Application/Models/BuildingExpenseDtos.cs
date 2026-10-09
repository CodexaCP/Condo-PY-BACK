using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class BuildingExpenseUpsertRequest
{
    public Guid BuildingId { get; set; }
    public Guid ExpensePeriodId { get; set; }
    public BuildingExpenseCategory Category { get; set; } = BuildingExpenseCategory.Other;
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; } = BuildingExpenseDistributionType.ByCoefficient;
    public Guid? TargetUnitId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool PaidByReserveFund { get; set; }
    // Rubro del plan de cuentas de Finanzas del edificio (opcional). Con rubro, la categoria se toma del rubro.
    public Guid? LedgerCategoryId { get; set; }
    // Proveedor de la lista de la empresa (opcional): el nombre del gasto sale del proveedor. Sin proveedor rige SupplierName (texto libre).
    public Guid? SupplierId { get; set; }
    // Factura del proveedor (opcional), para el libro de compras.
    public string? InvoiceNumber { get; set; }
    public string? InvoiceTimbrado { get; set; }
    // Cuentas por pagar: con vencimiento el gasto queda "a pagar" y entra a la caja cuando se registra su pago. Sin vencimiento ni pago, entra en su fecha.
    public DateOnly? DueDate { get; set; }
    public DateOnly? PaidAt { get; set; }
    public Guid? PaidFromAccountId { get; set; }
    // IVA incluido en el monto: 10, 5 o 0 (exento). Vacio = la tasa que corresponde al rubro, si la tiene definida.
    public decimal? VatRate { get; set; }
}

public class BuildingExpenseDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public BuildingExpenseCategory Category { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    // Monto que se reparte (neto de las notas de credito del proveedor aplicadas).
    public decimal Amount { get; set; }
    // Monto facturado por el proveedor, antes de las notas de credito (igual a Amount si no tiene ninguna).
    public decimal OriginalAmount { get; set; }
    public decimal CreditedAmount { get; set; }
    // Notas de credito del proveedor registradas con el periodo ya publicado (suman al total que se acredita; el monto repartido no cambia).
    public decimal CreditedAfterPublishAmount { get; set; }
    public BuildingExpenseDistributionType DistributionType { get; set; }
    public Guid? TargetUnitId { get; set; }
    public string TargetUnitCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool PaidByReserveFund { get; set; }
    public bool HasReceipt { get; set; }
    public string? ReceiptFileName { get; set; }
    public Guid? LedgerCategoryId { get; set; }
    public string? LedgerCategoryCode { get; set; }
    public string? LedgerCategoryName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceTimbrado { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? PaidAt { get; set; }
    public Guid? PaidFromAccountId { get; set; }
    public string? PaidFromAccountName { get; set; }
    // Estado de la cuenta por pagar: None (sin vencimiento ni pago: cuenta en su fecha), Pending (a pagar), Overdue (vencido sin pagar) o Paid.
    public string PayableStatus { get; set; } = "None";
    public decimal? VatRate { get; set; }
    // IVA incluido en el monto vigente (null si el gasto no tiene tasa)
    public decimal? VatAmount { get; set; }
}

// Registrar el pago de una factura de proveedor (cuenta por pagar): fecha del pago y cuenta desde la que se pago (opcional).
public class RegisterExpensePaymentRequest
{
    public DateOnly PaidAt { get; set; }
    public Guid? AccountId { get; set; }
}
