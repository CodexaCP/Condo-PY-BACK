namespace Condo.Domain.Enums;

// Para que sirve una cuenta del plan en los asientos sugeridos. Se guarda como texto: agregar valores no cambia el esquema de la base.
public enum LedgerAccountRoleKind
{
    // La cuenta del plan (activo o fondo) que representa a una cuenta financiera del edificio (banco, caja o fondo de reserva).
    FinancialAccount = 1,

    // La cuenta del plan (activo) donde se asienta el IVA crédito fiscal de las compras.
    VatCredit = 2,

    // La cuenta del plan (fondos o patrimonio) contra la que se asientan los saldos iniciales de la apertura: lo que el edificio ya habia acumulado.
    // Sin asignar rige «Resultados acumulados» (3.2.01 del plan generico).
    OpeningEquity = 3,

    // La cuenta del plan (fondos o patrimonio) donde se saldan los ingresos y gastos al cerrar el ejercicio: el resultado del ejercicio.
    // Sin asignar rige «Resultado del periodo» (3.2.02 del plan generico).
    PeriodResult = 4
}
