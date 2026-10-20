namespace Condo.Domain.Enums;

/// <summary>Por donde entro el pago: lo declaro el propietario desde la app, lo registro el personal en la web, o lo confirmo un proveedor de pagos online.</summary>
public enum OwnerPaymentChannel
{
    App = 1,
    Web = 2,
    Online = 3
}
