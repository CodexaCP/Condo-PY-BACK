using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Operacion guiada de Finanzas: plata que pasa de una cuenta financiera del edificio a otra (deposito de efectivo, retiro, aporte o retiro del
// fondo de reserva, transferencia entre bancos). No es un ingreso ni un gasto: el saldo total del edificio no cambia. No se edita; se anula con
// motivo (desaparece del libro y el Libro Diario la corrige con un contraasiento). Sin borrado fisico.
public class FinanceTransfer : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly TransferDate { get; set; }

    // Numero de boleta de deposito, de transferencia, etc.
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    // Acta o resolucion de asamblea que respalda el movimiento. Obligatoria cuando participa el fondo de reserva.
    public string SupportReference { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    public FinanceTransferStatus Status { get; set; } = FinanceTransferStatus.Active;
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string VoidReason { get; set; } = string.Empty;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public FinancialAccount? FromAccount { get; set; }
    public FinancialAccount? ToAccount { get; set; }
}
