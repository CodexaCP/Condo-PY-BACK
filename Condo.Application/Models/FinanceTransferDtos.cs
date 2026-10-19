using Condo.Domain.Enums;

namespace Condo.Application.Models;

public sealed class CreateFinanceTransferRequest
{
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public decimal Amount { get; set; }
    // Nula = hoy.
    public DateOnly? TransferDate { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    // Acta o resolucion de asamblea (obligatoria si participa el fondo de reserva).
    public string? SupportReference { get; set; }
    // El usuario vio el aviso de saldo negativo en banco o caja y confirma igual.
    public bool ConfirmNegativeBalance { get; set; }
}

public sealed class VoidFinanceTransferRequest
{
    public string? Reason { get; set; }
}

/// <summary>Una cuenta financiera del edificio con su saldo, para armar la pantalla de operaciones.</summary>
public sealed class FinanceTransferAccountDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; }
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
}

public sealed class FinanceTransferDto
{
    public Guid Id { get; set; }
    public DateOnly TransferDate { get; set; }
    public Guid FromAccountId { get; set; }
    public string FromAccountName { get; set; } = string.Empty;
    public Guid ToAccountId { get; set; }
    public string ToAccountName { get; set; } = string.Empty;
    // Deposito de efectivo, retiro, aporte o retiro del fondo de reserva, o transferencia entre cuentas.
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string SupportReference { get; set; } = string.Empty;
    public FinanceTransferStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime? VoidedAtUtc { get; set; }
    public string VoidedByName { get; set; } = string.Empty;
    public string VoidReason { get; set; } = string.Empty;
    // Numeros de asiento vigentes del Libro Diario que salen de esta transferencia (vacio = todavia no asentada).
    public List<string> JournalLabels { get; set; } = [];
}

public sealed class FinanceTransfersDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
    public DateOnly FinanceStartDate { get; set; }
    public DateOnly? PostedThrough { get; set; }
    public decimal TotalBalance { get; set; }
    public List<FinanceTransferAccountDto> Accounts { get; set; } = [];
    public List<FinanceTransferDto> Items { get; set; } = [];
}

/// <summary>Resumen previo a confirmar: como quedan los saldos y que asiento sale.</summary>
public sealed class FinanceTransferPreviewDto
{
    public string Kind { get; set; } = string.Empty;
    public string FromAccountName { get; set; } = string.Empty;
    public decimal FromBefore { get; set; }
    public decimal FromAfter { get; set; }
    public string ToAccountName { get; set; } = string.Empty;
    public decimal ToBefore { get; set; }
    public decimal ToAfter { get; set; }
    public decimal TotalBalance { get; set; }
    public bool RequiresSupport { get; set; }
    // Quedaria en negativo la cuenta de origen (banco o caja): hay que confirmar.
    public bool NeedsNegativeConfirmation { get; set; }
    // Cuenta del plan que se debita y que se acredita en el asiento (nula = la cuenta financiera no tiene cuenta del plan asignada).
    public string? DebitAccount { get; set; }
    public string? CreditAccount { get; set; }
    public List<string> Warnings { get; set; } = [];
}
