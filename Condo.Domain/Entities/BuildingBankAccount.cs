using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Cuenta bancaria donde el edificio recibe los pagos (datos de cobro). Es solo informacion de registro: se muestra
// a quien tiene que transferir y sirve de base para los cobros online. No es la cuenta contable de Finanzas
// (FinancialAccount), que lleva saldos y movimientos.
public class BuildingBankAccount : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public BankAccountType AccountType { get; set; } = BankAccountType.Checking;
    public string AccountNumber { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public string? HolderDocument { get; set; }
    public string? Alias { get; set; }
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
