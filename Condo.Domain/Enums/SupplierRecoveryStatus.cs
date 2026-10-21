namespace Condo.Domain.Enums;

public enum SupplierRecoveryStatus
{
    // Sin nada que recuperar (RecoveryType = None).
    None = 0,
    // Cuenta por cobrar al proveedor: dinero por devolver o descuento todavia sin aplicar del todo.
    Pending = 1,
    // Dinero devuelto o descuento aplicado por completo.
    Recovered = 2
}
