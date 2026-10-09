using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Contrapartida contable de los asientos sugeridos: a que cuenta del plan de cuentas corresponde cada cuenta financiera del edificio
// (banco, caja, fondo de reserva) y donde se asienta el IVA credito. Es solo una asignacion: no mueve saldos ni cambia el libro.
// FinancialAccountId va solo en el rol FinancialAccount; el IVA credito es uno por edificio.
public class LedgerAccountRole : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public LedgerAccountRoleKind Role { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public Guid LedgerCategoryId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public LedgerCategory? LedgerCategory { get; set; }
}
