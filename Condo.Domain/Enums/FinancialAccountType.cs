namespace Condo.Domain.Enums;

// Tipo de cuenta financiera del modulo "Finanzas del edificio". Por edificio: como maximo una caja y un fondo
// de reserva; los bancos pueden ser varios.
public enum FinancialAccountType
{
    Cash = 1,
    Bank = 2,
    ReserveFund = 3
}
