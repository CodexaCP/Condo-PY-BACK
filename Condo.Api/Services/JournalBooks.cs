using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

/// <summary>Un renglon de un asiento asentado, aplanado con los datos de su asiento (lo que necesitan el Mayor y el balance).</summary>
public sealed record BookLine(
    Guid EntryId, int FiscalYear, int Number, DateOnly Date, string Description, int LineNo,
    string Code, string? ExternalCode, string Name, decimal Debit, decimal Credit,
    Guid? LedgerCategoryId = null, LedgerCategoryType? AccountType = null, bool IsClosing = false)
{
    public string Label => JournalPostingService.LabelOf(FiscalYear, Number);
}

/// <summary>
/// Calculos puros de los libros oficiales a partir de lo asentado: Libro Mayor (saldo inicial, movimientos y saldo acumulado por cuenta) y
/// balance de sumas y saldos. Las cuentas se identifican por su codigo (el asiento guarda copia del codigo y el nombre al asentar). El saldo
/// inicial es todo lo asentado con fecha anterior al periodo, de cualquier ejercicio: sin asiento de cierre (fase 4) las cuentas de resultado
/// arrastran su saldo de un ejercicio al siguiente.
/// </summary>
public static class JournalBooksCalculator
{
    /// <summary>
    /// Aplana los asientos en renglones. Los del cierre del ejercicio (y sus contraasientos, si se reabrio) se marcan: el estado de resultados
    /// no los cuenta, porque justamente saldan los ingresos y gastos del ejercicio.
    /// </summary>
    public static List<BookLine> Flatten(IEnumerable<JournalEntry> entries)
    {
        var all = entries.ToList();
        var closingIds = all.Where(e => e.Kind == JournalEntryKind.Closing).Select(e => e.Id).ToHashSet();
        return all.SelectMany(e =>
            {
                var closing = e.Kind == JournalEntryKind.Closing || (e.ReversesEntryId.HasValue && closingIds.Contains(e.ReversesEntryId.Value));
                return e.Lines.Select(l => new BookLine(
                    e.Id, e.FiscalYear, e.Number, e.Date, e.Description, l.LineNo, l.Code, l.ExternalCode, l.Name, l.Debit, l.Credit,
                    l.LedgerCategoryId, l.AccountType, closing));
            })
            .ToList();
    }

    /// <summary>Cuentas con movimiento en el periodo o con saldo inicial, ordenadas por codigo. <paramref name="withMovements"/> agrega el detalle.</summary>
    public static List<JournalLedgerAccountDto> Ledger(
        IReadOnlyList<BookLine> lines, DateOnly from, DateOnly to, bool withMovements, string? onlyCode = null)
    {
        var result = new List<JournalLedgerAccountDto>();
        foreach (var group in lines.Where(l => onlyCode is null || l.Code == onlyCode).GroupBy(l => l.Code))
        {
            var opening = group.Where(l => l.Date < from).Sum(l => l.Debit - l.Credit);
            var inPeriod = group.Where(l => l.Date >= from && l.Date <= to)
                .OrderBy(l => l.Date).ThenBy(l => l.FiscalYear).ThenBy(l => l.Number).ThenBy(l => l.LineNo).ToList();
            if (inPeriod.Count == 0 && opening == 0m) continue;

            // El nombre vigente es el del ultimo renglon asentado de la cuenta.
            var latest = group.OrderBy(l => l.Date).ThenBy(l => l.FiscalYear).ThenBy(l => l.Number).ThenBy(l => l.LineNo).Last();
            var balance = opening;
            var movements = new List<JournalLedgerMovementDto>();
            foreach (var l in inPeriod)
            {
                balance += l.Debit - l.Credit;
                if (withMovements)
                {
                    movements.Add(new JournalLedgerMovementDto
                    {
                        Date = l.Date, EntryId = l.EntryId, EntryLabel = l.Label, Description = l.Description,
                        Debit = l.Debit, Credit = l.Credit, Balance = balance
                    });
                }
            }

            result.Add(new JournalLedgerAccountDto
            {
                Code = group.Key,
                ExternalCode = latest.ExternalCode,
                Name = latest.Name,
                OpeningBalance = opening,
                Debit = inPeriod.Sum(l => l.Debit),
                Credit = inPeriod.Sum(l => l.Credit),
                ClosingBalance = balance,
                MovementCount = inPeriod.Count,
                Movements = movements
            });
        }

        return result.OrderBy(a => a.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Balance de sumas y saldos: por cuenta, saldo inicial, sumas del periodo y saldo final, cada uno separado en deudor y acreedor.</summary>
    public static (List<JournalTrialBalanceRowDto> Rows, JournalTrialBalanceRowDto Totals, bool IsBalanced, decimal Difference) TrialBalance(
        IReadOnlyList<BookLine> lines, DateOnly from, DateOnly to)
    {
        var rows = Ledger(lines, from, to, withMovements: false).Select(a => new JournalTrialBalanceRowDto
        {
            Code = a.Code,
            ExternalCode = a.ExternalCode,
            Name = a.Name,
            OpeningDebit = Math.Max(a.OpeningBalance, 0m),
            OpeningCredit = Math.Max(-a.OpeningBalance, 0m),
            Debit = a.Debit,
            Credit = a.Credit,
            ClosingDebit = Math.Max(a.ClosingBalance, 0m),
            ClosingCredit = Math.Max(-a.ClosingBalance, 0m)
        }).ToList();

        var totals = new JournalTrialBalanceRowDto
        {
            Name = "Totales",
            OpeningDebit = rows.Sum(r => r.OpeningDebit),
            OpeningCredit = rows.Sum(r => r.OpeningCredit),
            Debit = rows.Sum(r => r.Debit),
            Credit = rows.Sum(r => r.Credit),
            ClosingDebit = rows.Sum(r => r.ClosingDebit),
            ClosingCredit = rows.Sum(r => r.ClosingCredit)
        };
        var balanced = totals.Debit == totals.Credit && totals.OpeningDebit == totals.OpeningCredit && totals.ClosingDebit == totals.ClosingCredit;
        return (rows, totals, balanced, totals.ClosingDebit - totals.ClosingCredit);
    }
}

public enum BookRowKind
{
    // Cabecera de un asiento: numero, fecha y glosa (ocupa dos renglones de la hoja).
    Entry = 1,
    Line = 2
}

public sealed record BookRow(
    BookRowKind Kind, string Label, DateOnly Date, string Description, string ThirdParty, bool Continued,
    string Code, string Name, decimal Debit, decimal Credit)
{
    // Renglones de la hoja que ocupa: la cabecera lleva dos (la glosa puede ocupar dos lineas).
    public int Weight => Kind == BookRowKind.Entry ? 2 : 1;
}

/// <summary>Una hoja del Libro Diario: su folio, sus renglones y las sumas de la hoja, anterior y acumulada.</summary>
public sealed record BookPage(int Folio, IReadOnlyList<BookRow> Rows, decimal PageDebit, decimal PageCredit, decimal PreviousDebit, decimal PreviousCredit)
{
    public decimal CumulativeDebit => PreviousDebit + PageDebit;
    public decimal CumulativeCredit => PreviousCredit + PageCredit;
}

/// <summary>
/// Reparte los asientos en hojas con folio correlativo (Ley 1034/83, art. 78 y Ley 4924/2013: hojas sueltas con numeracion correlativa).
/// Un asiento no se parte entre hojas salvo que sea mas grande que una hoja entera; entonces sigue en la hoja siguiente con la cabecera
/// marcada «continuacion». Sin renglones en blanco (art. 79): cada hoja lleva solo asientos y al pie sus sumas. Lo comparten el PDF y el Excel.
/// </summary>
public static class JournalBookPaginator
{
    public const int DefaultRowsPerPage = 40;

    // Tercero del asiento con el respaldo del comprobante (RUC, timbrado y numero) cuando el asiento los tiene.
    internal static string WithSupport(JournalEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.SupplierRuc)) parts.Add($"RUC {entry.SupplierRuc}");
        if (!string.IsNullOrWhiteSpace(entry.Timbrado)) parts.Add($"timbrado {entry.Timbrado}");
        if (parts.Count > 0 && !string.IsNullOrWhiteSpace(entry.Reference)) parts.Add($"comprobante {entry.Reference}");
        return parts.Count == 0 ? entry.ThirdParty : $"{entry.ThirdParty} ({string.Join(", ", parts)})".TrimStart();
    }

    public static List<BookPage> Paginate(IReadOnlyList<JournalEntry> entries, int startFolio, int rowsPerPage = DefaultRowsPerPage)
    {
        var capacity = Math.Max(rowsPerPage, 3);
        var pages = new List<BookPage>();
        var rows = new List<BookRow>();
        var used = 0;
        decimal previousDebit = 0m, previousCredit = 0m;

        void Close()
        {
            if (rows.Count == 0) return;
            var debit = rows.Where(r => r.Kind == BookRowKind.Line).Sum(r => r.Debit);
            var credit = rows.Where(r => r.Kind == BookRowKind.Line).Sum(r => r.Credit);
            pages.Add(new BookPage(startFolio + pages.Count, rows.ToList(), debit, credit, previousDebit, previousCredit));
            previousDebit += debit;
            previousCredit += credit;
            rows.Clear();
            used = 0;
        }

        foreach (var entry in entries.OrderBy(e => e.FiscalYear).ThenBy(e => e.Number))
        {
            var lines = entry.Lines.OrderBy(l => l.LineNo).ToList();
            var label = JournalPostingService.LabelOf(entry.FiscalYear, entry.Number);
            var blockWeight = 2 + lines.Count;
            if (used > 0 && used + blockWeight > capacity && blockWeight <= capacity) Close();

            BookRow Header(bool continued) => new(
                BookRowKind.Entry, label, entry.Date, entry.Description, WithSupport(entry), continued, string.Empty, string.Empty, 0m, 0m);

            var needsHeader = true;
            var continued = false;
            foreach (var line in lines)
            {
                // La cabecera nunca queda sola al pie de una hoja: se necesita lugar para ella y para al menos un renglon.
                if (needsHeader && used > 0 && used + 3 > capacity) Close();
                if (!needsHeader && used + 1 > capacity)
                {
                    Close();
                    needsHeader = true;
                    continued = true;
                }

                if (needsHeader)
                {
                    rows.Add(Header(continued));
                    used += 2;
                    needsHeader = false;
                }

                rows.Add(new BookRow(BookRowKind.Line, label, entry.Date, string.Empty, string.Empty, false, line.Code, line.Name, line.Debit, line.Credit));
                used += 1;
            }
        }

        Close();
        return pages;
    }
}
