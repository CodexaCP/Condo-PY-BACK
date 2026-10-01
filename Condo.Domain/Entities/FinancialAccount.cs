using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Cuenta financiera del edificio (caja, banco o fondo de reserva), en guaranies. El saldo inicial rige a
// FinanceSettings.FinanceStartDate; el saldo actual se arma con los movimientos posteriores (fase 2).
public class FinancialAccount : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; } = FinancialAccountType.Cash;
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
