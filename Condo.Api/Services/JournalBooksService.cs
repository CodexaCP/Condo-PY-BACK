using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Todo lo que necesita imprimir el Libro Diario de un ejercicio: titular, contador, rubrica, ejercicio y las hojas con su folio.</summary>
public sealed record JournalBookPrint(
    string BuildingName, AccountingBooksDto Books, int FiscalYear, DateOnly From, DateOnly To, int StartFolio,
    IReadOnlyList<BookPage> Pages, int EntryCount, decimal TotalDebit, decimal TotalCredit);

/// <summary>
/// Libros oficiales a partir de lo asentado: Libro Diario del ejercicio con folios, Libro Mayor y balance de sumas y saldos. Solo lee: no
/// asienta nada. El ejercicio por defecto es el ultimo con asientos (o el actual si todavia no hay).
/// </summary>
public class JournalBooksService(ICondoDbContext dbContext, AccountingBooksService books)
{
    public const int MaxStartFolio = 1_000_000;

    /// <summary>Ejercicio (por defecto el ultimo con asientos) y su rango de fechas.</summary>
    public async Task<(int FiscalYear, DateOnly Start, DateOnly End, List<int> Years)> PeriodAsync(
        LedgerContext ctx, int? fiscalYear, CancellationToken ct)
    {
        var years = await dbContext.JournalEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .Select(x => x.FiscalYear).Distinct().OrderByDescending(x => x).ToListAsync(ct);
        var year = fiscalYear ?? (years.Count > 0 ? years[0] : FinancePeriods.FiscalYearOf(FinancePeriods.Today(), ctx.FiscalYearStartMonth));
        var (start, end) = FinancePeriods.FiscalYearRange(year, ctx.FiscalYearStartMonth);
        return (year, start, end, years);
    }

    /// <summary>Primer folio sugerido: el siguiente a los folios usados de la rubrica (1 si no se cargo).</summary>
    public async Task<int> SuggestedStartFolioAsync(Guid buildingId, CancellationToken ct)
    {
        var used = await dbContext.AccountingBookSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => x.UsedFolios).FirstOrDefaultAsync(ct);
        return Math.Max((used ?? 0) + 1, 1);
    }

    // Renglones asentados hasta una fecha (para el saldo inicial y los movimientos del periodo).
    private async Task<List<BookLine>> LinesAsync(Guid buildingId, DateOnly to, CancellationToken ct)
    {
        var entries = await dbContext.JournalEntries.AsNoTracking().Include(x => x.Lines)
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Date <= to)
            .ToListAsync(ct);
        return JournalBooksCalculator.Flatten(entries);
    }

    // ─── Libro Mayor ──────────────────────────────────────────────────────────

    public async Task<JournalLedgerDto> LedgerAsync(
        LedgerContext ctx, int? fiscalYear, DateOnly? from, DateOnly? to, string? account, bool withMovements, CancellationToken ct)
    {
        var (year, start, end, years) = await PeriodAsync(ctx, fiscalYear, ct);
        var rangeFrom = from.HasValue && from.Value > start ? from.Value : start;
        var rangeTo = to.HasValue && to.Value < end ? to.Value : end;
        var lines = await LinesAsync(ctx.BuildingId, rangeTo, ct);

        // Con una cuenta pedida se devuelve su detalle; sin ella, solo el resumen de cada cuenta (el detalle va en el Excel).
        var accounts = JournalBooksCalculator.Ledger(lines, rangeFrom, rangeTo, withMovements || !string.IsNullOrWhiteSpace(account),
            string.IsNullOrWhiteSpace(account) ? null : account.Trim());
        return new JournalLedgerDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            FiscalYear = year,
            FiscalYears = years,
            From = rangeFrom,
            To = rangeTo,
            Accounts = accounts,
            TotalDebit = accounts.Sum(a => a.Debit),
            TotalCredit = accounts.Sum(a => a.Credit)
        };
    }

    // ─── Balance de sumas y saldos ────────────────────────────────────────────

    public async Task<JournalTrialBalanceDto> TrialBalanceAsync(
        LedgerContext ctx, int? fiscalYear, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (year, start, end, years) = await PeriodAsync(ctx, fiscalYear, ct);
        var rangeFrom = from.HasValue && from.Value > start ? from.Value : start;
        var rangeTo = to.HasValue && to.Value < end ? to.Value : end;
        var lines = await LinesAsync(ctx.BuildingId, rangeTo, ct);
        var (rows, totals, balanced, difference) = JournalBooksCalculator.TrialBalance(lines, rangeFrom, rangeTo);

        return new JournalTrialBalanceDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            FiscalYear = year,
            FiscalYears = years,
            From = rangeFrom,
            To = rangeTo,
            Rows = rows,
            Totals = totals,
            IsBalanced = balanced,
            Difference = difference
        };
    }

    // ─── Libro Diario imprimible ──────────────────────────────────────────────

    public async Task<JournalBookPrint> PrintAsync(
        LedgerContext ctx, Building building, int? fiscalYear, int? startFolio, int rowsPerPage, CancellationToken ct)
    {
        var (year, start, end, _) = await PeriodAsync(ctx, fiscalYear, ct);
        var entries = await dbContext.JournalEntries.AsNoTracking().Include(x => x.Lines)
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && x.FiscalYear == year)
            .OrderBy(x => x.Number)
            .ToListAsync(ct);
        var folio = startFolio is >= 1 and <= MaxStartFolio ? startFolio.Value : await SuggestedStartFolioAsync(ctx.BuildingId, ct);
        var pages = JournalBookPaginator.Paginate(entries, folio, rowsPerPage);

        return new JournalBookPrint(
            ctx.BuildingName, await books.GetAsync(building, canEdit: false, ct), year, start, end, folio, pages, entries.Count,
            entries.Sum(e => e.TotalDebit), entries.Sum(e => e.TotalCredit));
    }
}
