namespace Condo.Domain.Enums;

// Naturaleza de una cuenta del plan de cuentas del edificio (la clase del plan: 1 Activo, 2 Pasivo, 3 Patrimonio / Fondos,
// 4 Ingresos, 5 Egresos). Solo los ingresos y los egresos reciben movimientos; el resto es de referencia (y exportacion).
// Se guarda como texto: agregar valores no cambia el esquema de la base.
public enum LedgerCategoryType
{
    Income = 1,
    Expense = 2,
    Fund = 3,
    Asset = 4,
    Liability = 5
}
