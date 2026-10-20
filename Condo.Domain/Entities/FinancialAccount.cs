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

    // Si la cuenta se concilia con un extracto. Nulo = lo que corresponde a su tipo (los bancos si; la caja y el fondo de reserva no); la empresa
    // puede elegir lo contrario, por ejemplo conciliar el fondo de reserva cuando esta en una cuenta bancaria.
    public bool? ReconcileOverride { get; set; }

    // Valor efectivo: la eleccion de la empresa o, si no eligio, lo que corresponde a su tipo.
    public bool Reconciles => ReconcileOverride ?? Type == FinancialAccountType.Bank;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
