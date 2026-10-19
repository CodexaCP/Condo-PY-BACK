using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Renglon de un asiento asentado. Guarda copia del codigo y nombre de la cuenta; LedgerCategoryId es solo una referencia informativa
// (sin llave foranea a proposito: reemplazar el plan de cuentas no puede tocar ni bloquear lo ya asentado).
public class JournalEntryLine : BaseEntity
{
    public Guid EntryId { get; set; }
    public int LineNo { get; set; }

    public Guid? LedgerCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    // Financial (banco, caja o fondo), Rubro (ingreso o gasto), VatCredit o Equity.
    public string Kind { get; set; } = string.Empty;

    // Clase de la cuenta del plan al asentar (Asset, Liability, Fund, Income o Expense). Nula en lo asentado antes de guardarla: se deduce de
    // LedgerCategoryId o del codigo. Sirve para el cierre del ejercicio y los estados.
    public LedgerCategoryType? AccountType { get; set; }

    public JournalEntry? Entry { get; set; }
}
