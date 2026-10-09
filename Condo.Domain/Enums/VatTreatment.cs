namespace Condo.Domain.Enums;

// Tratamiento de IVA de una cuenta de egresos del plan de cuentas. Se guarda como texto.
public enum VatTreatment
{
    // IVA 10 %: el monto del comprobante lo incluye (base = monto / 1,10).
    Vat10 = 1,
    // IVA 5 %: el monto del comprobante lo incluye (base = monto / 1,05).
    Vat5 = 2,
    // Exento: el comprobante no discrimina IVA.
    Exempt = 3,
    // No corresponde (por ejemplo sueldos, impuestos o aportes): no entra al libro de compras.
    NotApplicable = 4
}
