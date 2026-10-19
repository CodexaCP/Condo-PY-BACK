namespace Condo.Domain.Enums;

// Tipo de un asiento asentado en el Libro Diario. Se guarda como texto: agregar valores no cambia el esquema de la base.
public enum JournalEntryKind
{
    // Sale de un movimiento del libro de Finanzas (cobro, gasto, ingreso o nota de credito del proveedor).
    Auto = 1,

    // Saldos iniciales de las cuentas del edificio.
    Opening = 2,

    // Ajuste manual del Administrador.
    Adjustment = 3,

    // Contraasiento: invierte otro asiento para corregirlo (Ley 1034/83, art. 79).
    Reversal = 4,

    // Cierre del ejercicio.
    Closing = 5
}
