namespace Condo.Domain.Enums;

// Tipo de cuenta financiera del modulo "Finanzas del edificio". Por edificio: como maximo una caja, un fondo
// de reserva y una cuenta de cobros online por liquidar; los bancos pueden ser varios.
public enum FinancialAccountType
{
    Cash = 1,
    Bank = 2,
    ReserveFund = 3,
    // Cobros online por liquidar: un derecho de cobro frente al proveedor de pagos, no dinero disponible. La crea el sistema (una por
    // edificio) y no se edita a mano; el neto pasa al banco con una transferencia cuando el proveedor liquida.
    Clearing = 4
}
