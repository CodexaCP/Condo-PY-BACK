namespace Condo.Domain.Enums;

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Check = 4,
    Other = 5,
    // Cobro confirmado por un proveedor de pagos online. Solo lo crea el sistema: el registro manual del personal no lo acepta.
    Online = 6
}
