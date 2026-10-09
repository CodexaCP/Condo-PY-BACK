namespace Condo.Domain.Enums;

// Para que sirve una cuenta del plan en los asientos sugeridos. Se guarda como texto: agregar valores no cambia el esquema de la base.
public enum LedgerAccountRoleKind
{
    // La cuenta del plan (activo o fondo) que representa a una cuenta financiera del edificio (banco, caja o fondo de reserva).
    FinancialAccount = 1,

    // La cuenta del plan (activo) donde se asienta el IVA crédito fiscal de las compras.
    VatCredit = 2
}
