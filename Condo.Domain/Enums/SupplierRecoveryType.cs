namespace Condo.Domain.Enums;

// Como recupera el edificio lo que el proveedor le acredita con una nota de credito cuando el gasto YA estaba pagado.
public enum SupplierRecoveryType
{
    // El gasto todavia no se pago al proveedor: la nota solo baja la cuenta por pagar, no hay nada que recuperar.
    None = 0,
    // El proveedor devuelve el dinero.
    Refund = 1,
    // El proveedor lo descuenta de su proxima factura.
    NextInvoiceDiscount = 2
}
