using Condo.Domain.Enums;

namespace Condo.Application.Models;

// Cuentas por pagar y por cobrar de un proveedor en un edificio. «Por pagar» son sus facturas con vencimiento sin pago registrado;
// «por cobrar» es lo que el proveedor le debe al edificio por notas de credito de gastos ya pagados (devolucion de dinero o descuento
// en su proxima factura). Se agrupa por el nombre normalizado del proveedor.
public class SupplierAccountRowDto
{
    public string SupplierKey { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public int PayableCount { get; set; }
    public decimal PayableTotal { get; set; }
    public decimal PayableOverdueTotal { get; set; }
    public int ReceivableCount { get; set; }
    // Devoluciones de dinero que el proveedor todavia no hizo.
    public decimal PendingRefundTotal { get; set; }
    // Descuentos acordados que todavia no se vieron aplicados en una factura.
    public decimal PendingDiscountTotal { get; set; }
    public decimal ReceivableTotal { get; set; }
    // Por pagar menos por cobrar: lo que el edificio le pagaria al proveedor si se compensara todo (negativo = el proveedor debe).
    public decimal NetBalance { get; set; }
}

public class SupplierAccountsSummaryDto
{
    public Guid BuildingId { get; set; }
    public decimal PayableTotal { get; set; }
    public decimal PayableOverdueTotal { get; set; }
    public decimal PendingRefundTotal { get; set; }
    public decimal PendingDiscountTotal { get; set; }
    public decimal ReceivableTotal { get; set; }
    public List<SupplierAccountRowDto> Suppliers { get; set; } = new();
}

public class SupplierReceivableRowDto
{
    public Guid CreditNoteId { get; set; }
    public Guid BuildingExpenseId { get; set; }
    public string SupplierKey { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Timbrado { get; set; }
    public DateOnly IssueDate { get; set; }
    public string ExpenseDescription { get; set; } = string.Empty;
    public string ExpensePeriodName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public SupplierRecoveryType RecoveryType { get; set; }
    public SupplierRecoveryStatus RecoveryStatus { get; set; }
    public decimal DiscountApplied { get; set; }
    public decimal PendingAmount { get; set; }
    public DateOnly? RefundedOn { get; set; }
    public string? RefundReference { get; set; }
    // Dias desde la nota hasta hoy mientras siga pendiente (para que nadie la olvide).
    public int DaysPending { get; set; }
}

// Aviso al cargar una factura: el proveedor tiene descuentos acordados sin aplicar.
public class SupplierPendingDiscountDto
{
    public string SupplierName { get; set; } = string.Empty;
    public decimal PendingTotal { get; set; }
    public List<SupplierReceivableRowDto> Notes { get; set; } = new();
}
