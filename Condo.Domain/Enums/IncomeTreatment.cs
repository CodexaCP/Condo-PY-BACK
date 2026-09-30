namespace Condo.Domain.Enums;

// Que pasa con los ingresos del periodo (saldo acumulado, alquileres, intereses...) al liquidar.
public enum IncomeTreatment
{
    // Se reparten por coeficiente como un descuento en la expensa de cada propietario.
    CreditToOwners = 1,

    // Van al fondo de reserva: no reducen lo que se cobra a los propietarios.
    ToReserveFund = 2
}
