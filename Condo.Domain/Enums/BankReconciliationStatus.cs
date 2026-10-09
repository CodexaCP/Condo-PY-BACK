namespace Condo.Domain.Enums;

// Estado de una conciliacion bancaria. Se guarda como texto.
public enum BankReconciliationStatus
{
    // Se esta armando: se pueden marcar y desmarcar movimientos y cambiar el saldo del extracto.
    Open = 1,
    // Cerrada con diferencia cero: sus marcas quedan firmes (solo se deshacen reabriendola, con motivo).
    Completed = 2
}
