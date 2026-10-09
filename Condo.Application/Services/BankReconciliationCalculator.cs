using Condo.Application.Models;

namespace Condo.Application.Services;

/// <summary>Una marca de conciliacion vigente (foto de lo que se marco).</summary>
public sealed record ReconciliationMark(
    Guid Id, Guid ReconciliationId, int SourceType, Guid SourceId, DateOnly Date, decimal Amount, string Description,
    Guid MarkedByUserId, DateTime MarkedAtUtc);

/// <summary>Un movimiento reconciliable: todo lo que el libro asienta de un mismo origen en la cuenta (un cobro puede partirse en varios renglones).</summary>
public sealed record ReconcilableMovement(
    LedgerSourceType SourceType, Guid SourceId, DateOnly Date, decimal Amount, string Description, string ThirdParty, string Reference);

public sealed class ReconciliationCalculation
{
    public decimal ReconciledBalance { get; init; }
    public decimal Difference { get; init; }
    public decimal PendingIn { get; init; }
    public decimal PendingOut { get; init; }
    public int ChangedCount { get; init; }
    public int MissingCount { get; init; }
    public IReadOnlyList<ReconciliationMovementDto> Marked { get; init; } = [];
    public IReadOnlyList<ReconciliationMovementDto> Pending { get; init; } = [];
}

/// <summary>
/// Cuentas de la conciliacion bancaria manual (logica pura). Saldo conciliado = saldo inicial de la cuenta + lo marcado como conciliado hasta la
/// fecha de corte (en todas las conciliaciones de la cuenta). Diferencia = saldo del extracto - saldo conciliado. Lo del libro que todavia no se
/// marco queda como pendiente. Un marcado cuyo importe cambio despues cuenta por su importe vigente y se avisa; uno que ya no figura en el libro
/// no cuenta y se avisa (impide cerrar).
/// </summary>
public static class BankReconciliationCalculator
{
    /// <summary>Agrupa los renglones del libro de la cuenta por origen: un movimiento reconciliable por cobro, gasto, ingreso o nota.</summary>
    public static List<ReconcilableMovement> GroupRows(IEnumerable<LedgerRow> accountRows, DateOnly statementDate) =>
        accountRows
            .Where(r => r.Date <= statementDate)
            .GroupBy(r => (r.SourceType, r.SourceId))
            .Select(g =>
            {
                var first = g.OrderBy(r => r.Date).First();
                return new ReconcilableMovement(
                    g.Key.SourceType, g.Key.SourceId, g.Max(r => r.Date), g.Sum(r => r.Signed),
                    first.Description, first.ThirdParty, first.Reference);
            })
            .ToList();

    public static ReconciliationCalculation Calculate(
        decimal openingBalance,
        decimal statementBalance,
        IReadOnlyCollection<ReconcilableMovement> movements,
        IReadOnlyCollection<ReconciliationMark> marks,
        Guid? thisReconciliationId,
        IReadOnlyDictionary<Guid, string> userNames)
    {
        var byKey = movements.ToDictionary(m => ((int)m.SourceType, m.SourceId));
        var markedKeys = new HashSet<(int, Guid)>();
        var marked = new List<ReconciliationMovementDto>();
        decimal reconciled = openingBalance;
        int changed = 0, missing = 0;

        foreach (var mark in marks.OrderBy(m => m.Date).ThenBy(m => m.MarkedAtUtc))
        {
            var key = (mark.SourceType, mark.SourceId);
            markedKeys.Add(key);
            var dto = new ReconciliationMovementDto
            {
                SourceType = (LedgerSourceType)mark.SourceType,
                SourceId = mark.SourceId,
                Date = mark.Date,
                Description = mark.Description,
                IsMarked = true,
                ReconciliationId = mark.ReconciliationId,
                InThisReconciliation = thisReconciliationId.HasValue && mark.ReconciliationId == thisReconciliationId.Value,
                MarkedAmount = mark.Amount,
                MarkedAtUtc = mark.MarkedAtUtc,
                MarkedByName = userNames.GetValueOrDefault(mark.MarkedByUserId)
            };

            if (byKey.TryGetValue(key, out var current))
            {
                dto.Date = current.Date;
                dto.Description = string.IsNullOrWhiteSpace(current.Description) ? mark.Description : current.Description;
                dto.ThirdParty = current.ThirdParty;
                dto.Reference = current.Reference;
                dto.Amount = current.Amount;
                dto.Direction = current.Amount >= 0m ? LedgerDirection.In : LedgerDirection.Out;
                if (current.Amount != mark.Amount)
                {
                    dto.State = "Changed";
                    changed++;
                }

                reconciled += current.Amount;
            }
            else
            {
                // Ya no figura en el libro a esa fecha (se borro, se revirtio o cambio de fecha o de cuenta): no cuenta.
                dto.State = "Missing";
                dto.Amount = mark.Amount;
                dto.Direction = mark.Amount >= 0m ? LedgerDirection.In : LedgerDirection.Out;
                missing++;
            }

            marked.Add(dto);
        }

        var pending = movements
            .Where(m => !markedKeys.Contains(((int)m.SourceType, m.SourceId)))
            .OrderBy(m => m.Date).ThenBy(m => m.Description, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new ReconciliationMovementDto
            {
                SourceType = m.SourceType,
                SourceId = m.SourceId,
                Date = m.Date,
                Description = m.Description,
                ThirdParty = m.ThirdParty,
                Reference = m.Reference,
                Amount = m.Amount,
                Direction = m.Amount >= 0m ? LedgerDirection.In : LedgerDirection.Out
            })
            .ToList();

        return new ReconciliationCalculation
        {
            ReconciledBalance = reconciled,
            Difference = statementBalance - reconciled,
            PendingIn = pending.Where(p => p.Amount > 0m).Sum(p => p.Amount),
            PendingOut = -pending.Where(p => p.Amount < 0m).Sum(p => p.Amount),
            ChangedCount = changed,
            MissingCount = missing,
            Marked = marked,
            Pending = pending
        };
    }
}
