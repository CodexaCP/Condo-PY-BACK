using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Cierre del ejercicio del Libro Diario. Salda las cuentas de ingresos y gastos del ejercicio contra «Resultado del periodo» (3.2.02 por defecto)
/// en un asiento de cierre y pasa ese resultado a «Resultados acumulados» (3.2.01 por defecto) en otro, ambos fechados el ultimo dia del ejercicio.
/// Asi el ejercicio siguiente arranca con las cuentas de resultado en cero y el saldo inicial de las demas es lo que arrastra el Mayor. Un ejercicio
/// esta cerrado mientras tenga asientos de cierre vigentes (que ningun contraasiento anulo); reabrirlo es anularlos con contraasientos. Solo se cierra
/// un ejercicio terminado, asentado por completo y sin diferencias pendientes, en orden (el anterior primero).
/// </summary>
public class FiscalYearClosingService(
    ICondoDbContext dbContext, JournalBooksService books, JournalPostingService journal, ITenantContext tenantContext)
{
    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    // ─── Estado y vista previa ────────────────────────────────────────────────

    private sealed record Loaded(
        List<JournalEntry> Entries, HashSet<Guid> ReversedIds, List<BookLine> Lines, List<JournalEntry> ActiveClosings);

    private async Task<Loaded> LoadAsync(Guid buildingId, DateOnly upTo, CancellationToken ct)
    {
        var entries = await dbContext.JournalEntries.AsNoTracking().Include(x => x.Lines)
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Date <= upTo)
            .ToListAsync(ct);
        var allClosings = await dbContext.JournalEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Kind == JournalEntryKind.Closing)
            .ToListAsync(ct);
        var reversed = (await dbContext.JournalEntries.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.ReversesEntryId != null)
                .Select(x => x.ReversesEntryId!.Value).ToListAsync(ct))
            .ToHashSet();
        return new Loaded(entries, reversed, JournalBooksCalculator.Flatten(entries), allClosings.Where(c => !reversed.Contains(c.Id)).ToList());
    }

    public async Task<FiscalYearClosingDto> GetAsync(LedgerContext ctx, int? fiscalYear, bool canEdit, CancellationToken ct)
    {
        var (year, start, end, years) = await books.PeriodAsync(ctx, fiscalYear, ct);
        var loaded = await LoadAsync(ctx.BuildingId, end, ct);
        var plan = JournalBooksService.PlanTypes(ctx);
        var roles = await dbContext.LedgerAccountRoles.AsNoTracking().Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId).ToListAsync(ct);

        var closings = loaded.ActiveClosings.Where(c => c.FiscalYear == year).OrderBy(c => c.Number).ToList();
        var isClosed = closings.Count > 0;
        var canReopen = isClosed && !loaded.ActiveClosings.Any(c => c.FiscalYear > year);

        // Lo que se cierra: los ingresos y gastos del ejercicio sin los asientos de cierre (si ya esta cerrado, lo que se cerro).
        var gross = JournalStatementsCalculator.ResultAccounts(loaded.Lines.Where(l => l.Date >= start && l.Date <= end && !l.IsClosing), plan);
        var accounts = gross.Select(r => new FiscalClosingAccountDto
        {
            Code = r.Code, Name = r.Name, Type = r.Type.ToString(), Amount = r.Type == LedgerCategoryType.Income ? -r.Balance : r.Balance
        }).ToList();
        var income = accounts.Where(a => a.Type == nameof(LedgerCategoryType.Income)).Sum(a => a.Amount);
        var expense = accounts.Where(a => a.Type == nameof(LedgerCategoryType.Expense)).Sum(a => a.Amount);

        var resultAccount = AccountingEntriesService.EquityAccount(ctx, roles, LedgerAccountRoleKind.PeriodResult);
        var equityAccount = AccountingEntriesService.EquityAccount(ctx, roles, LedgerAccountRoleKind.OpeningEquity);

        var blockers = new List<string>();
        if (!isClosed)
        {
            var settings = await dbContext.FinanceSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId, ct);
            if (FinancePeriods.Today() <= end) blockers.Add($"El ejercicio {year} todavía no terminó (cierra el {end:dd/MM/yyyy}).");
            if (settings?.JournalPostedThrough is not { } posted || posted < end)
            {
                blockers.Add($"Asentá el Libro diario hasta el {end:dd/MM/yyyy} antes de cerrar el ejercicio.");
            }
            else
            {
                var differences = await journal.DetectAsync(ctx, posted, ct);
                if (differences.Count > 0) blockers.Add($"Hay {differences.Count} asientos asentados que cambiaron: corregilos (Asentar) antes de cerrar.");
            }

            var earlier = JournalStatementsCalculator.ResultAccounts(loaded.Lines.Where(l => l.Date < start), plan);
            if (earlier.Count > 0) blockers.Add("Cerrá primero el ejercicio anterior: sus ingresos y gastos todavía no se saldaron.");
            if (resultAccount is null) blockers.Add("Falta la cuenta del plan del resultado del ejercicio (3.2.02 «Resultado del período»): asignala en «Cuentas del plan».");
            if (equityAccount is null) blockers.Add("Falta la cuenta del plan «Resultados acumulados» (3.2.01): asignala en «Cuentas del plan».");
            var residual = JournalStatementsCalculator.ResultAccounts(loaded.Lines.Where(l => l.Date >= start && l.Date <= end), plan);
            if (residual.Count == 0) blockers.Add("No hay ingresos ni gastos asentados en el ejercicio: no hace falta cerrarlo.");
        }

        return new FiscalYearClosingDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            FiscalYear = year,
            FiscalYears = years,
            Start = start,
            End = end,
            IsClosed = isClosed,
            ClosingLabels = closings.Select(c => JournalPostingService.LabelOf(c.FiscalYear, c.Number)).ToList(),
            CanClose = !isClosed && blockers.Count == 0,
            Blockers = blockers,
            Accounts = accounts,
            TotalIncome = income,
            TotalExpense = expense,
            Result = income - expense,
            ResultAccount = resultAccount is null ? null : $"{resultAccount.Code} {resultAccount.Name}",
            EquityAccount = equityAccount is null ? null : $"{equityAccount.Code} {equityAccount.Name}",
            CanReopen = canReopen,
            CanEdit = canEdit
        };
    }

    // ─── Cerrar ───────────────────────────────────────────────────────────────

    public async Task<(FiscalYearClosingResultDto? Result, string? Error, bool Conflict)> CloseAsync(
        LedgerContext ctx, int fiscalYear, Guid companyId, CancellationToken ct)
    {
        var preview = await GetAsync(ctx, fiscalYear, canEdit: true, ct);
        if (preview.IsClosed) return (null, $"El ejercicio {fiscalYear} ya está cerrado.", true);
        if (!preview.CanClose) return (null, preview.Blockers[0], true);

        var (_, start, end, _) = await books.PeriodAsync(ctx, fiscalYear, ct);
        var loaded = await LoadAsync(ctx.BuildingId, end, ct);
        var plan = JournalBooksService.PlanTypes(ctx);
        var roles = await dbContext.LedgerAccountRoles.AsNoTracking().Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId).ToListAsync(ct);
        var result = AccountingEntriesService.EquityAccount(ctx, roles, LedgerAccountRoleKind.PeriodResult)!;
        var equity = AccountingEntriesService.EquityAccount(ctx, roles, LedgerAccountRoleKind.OpeningEquity)!;

        // Lo que queda en las cuentas de resultado del ejercicio (de cualquier asiento, incluidos cierres y reaperturas anteriores).
        var residual = JournalStatementsCalculator.ResultAccounts(loaded.Lines.Where(l => l.Date >= start && l.Date <= end), plan);
        var total = residual.Sum(r => r.Balance);                                                // gastos menos ingresos: negativo = superavit
        var now = DateTime.UtcNow;
        var rows = new List<JournalEntry>();

        var closing = NewRow(ctx, companyId, end, $"Cierre del ejercicio {fiscalYear}: se saldan los ingresos y gastos contra {result.Name}", now);
        var line = 0;
        foreach (var r in residual)
        {
            closing.Lines.Add(Line(++line, r.Code, r.ExternalCode, r.Name, r.CategoryId, r.Type, debit: r.Balance < 0 ? -r.Balance : 0m, credit: r.Balance > 0 ? r.Balance : 0m));
        }

        closing.Lines.Add(Line(++line, result.Code, result.ExternalCode, result.Name, result.Id, LedgerCategoryType.Fund,
            debit: total > 0 ? total : 0m, credit: total < 0 ? -total : 0m));
        Finish(closing);
        rows.Add(closing);

        if (total != 0m)
        {
            var transfer = NewRow(ctx, companyId, end,
                $"Cierre del ejercicio {fiscalYear}: el resultado ({(total < 0 ? "superávit" : "déficit")}) pasa a {equity.Name}", now);
            var amount = Math.Abs(total);
            // Superavit: se debita el resultado y se acredita resultados acumulados; deficit, al reves.
            transfer.Lines.Add(Line(1, result.Code, result.ExternalCode, result.Name, result.Id, LedgerCategoryType.Fund, debit: total < 0 ? amount : 0m, credit: total > 0 ? amount : 0m));
            transfer.Lines.Add(Line(2, equity.Code, equity.ExternalCode, equity.Name, equity.Id, LedgerCategoryType.Fund, debit: total > 0 ? amount : 0m, credit: total < 0 ? amount : 0m));
            Finish(transfer);
            rows.Add(transfer);
        }

        await journal.AssignNumbersAsync(ctx, rows, companyId, now, ct);
        Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Accounting, "Closed",
            $"Libro Diario: se cerró el ejercicio {fiscalYear} ({preview.Result:N0} Gs. de {(total < 0 ? "superávit" : total > 0 ? "déficit" : "resultado cero")}).",
            "JournalEntry", null, [new ConfigChange("fiscalYear", "Ejercicio cerrado", null, fiscalYear.ToString(CultureInfo.InvariantCulture))]);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return (null, "Otra persona asentó o cerró al mismo tiempo. No se guardó nada: actualizá la pantalla y reintentá.", true);
        }

        return (new FiscalYearClosingResultDto
        {
            CreatedCount = rows.Count,
            FirstLabel = JournalPostingService.LabelOf(rows[0].FiscalYear, rows[0].Number),
            LastLabel = JournalPostingService.LabelOf(rows[^1].FiscalYear, rows[^1].Number)
        }, null, false);
    }

    // ─── Reabrir ──────────────────────────────────────────────────────────────

    public async Task<(FiscalYearClosingResultDto? Result, string? Error, bool Conflict)> ReopenAsync(
        LedgerContext ctx, int fiscalYear, string? reason, Guid companyId, CancellationToken ct)
    {
        var preview = await GetAsync(ctx, fiscalYear, canEdit: true, ct);
        if (!preview.IsClosed) return (null, $"El ejercicio {fiscalYear} no está cerrado.", true);
        if (!preview.CanReopen) return (null, "Reabrí primero el ejercicio siguiente: su cierre depende de este.", true);
        var text = reason?.Trim() ?? string.Empty;
        if (text.Length == 0) return (null, "El motivo de la reapertura es obligatorio.", false);
        if (text.Length > 300) return (null, "El motivo no puede superar los 300 caracteres.", false);

        var (_, _, end, _) = await books.PeriodAsync(ctx, fiscalYear, ct);
        var loaded = await LoadAsync(ctx.BuildingId, end, ct);
        var closingIds = loaded.ActiveClosings.Where(c => c.FiscalYear == fiscalYear).Select(c => c.Id).ToList();
        var originals = await dbContext.JournalEntries.AsNoTracking().Include(x => x.Lines)
            .Where(x => closingIds.Contains(x.Id))
            .OrderBy(x => x.Number)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var rows = originals.Select(o => journal.ReversalRow(o, end, companyId, ctx.BuildingId, now, "reapertura del ejercicio", text)).ToList();
        await journal.AssignNumbersAsync(ctx, rows, companyId, now, ct);
        Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Accounting, "Reopened", $"Libro Diario: se reabrió el ejercicio {fiscalYear}. Motivo: {text}",
            "JournalEntry", null, [new ConfigChange("fiscalYear", "Ejercicio reabierto", fiscalYear.ToString(CultureInfo.InvariantCulture), null)]);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return (null, "Otra persona reabrió o asentó al mismo tiempo. No se guardó nada: actualizá la pantalla y reintentá.", true);
        }

        return (new FiscalYearClosingResultDto
        {
            CreatedCount = rows.Count,
            FirstLabel = JournalPostingService.LabelOf(rows[0].FiscalYear, rows[0].Number),
            LastLabel = JournalPostingService.LabelOf(rows[^1].FiscalYear, rows[^1].Number)
        }, null, false);
    }

    // ─── Armado de los asientos de cierre ─────────────────────────────────────

    private JournalEntry NewRow(LedgerContext ctx, Guid companyId, DateOnly date, string description, DateTime now) => new()
    {
        CompanyId = companyId,
        BuildingId = ctx.BuildingId,
        Date = date,
        OperationDate = date,
        Description = description.Length > 500 ? description[..500] : description,
        Kind = JournalEntryKind.Closing,
        PostedAtUtc = now,
        PostedByUserId = tenantContext.UserId
    };

    private static JournalEntryLine Line(
        int lineNo, string code, string? externalCode, string name, Guid? categoryId, LedgerCategoryType type, decimal debit, decimal credit) => new()
    {
        LineNo = lineNo, Code = code, ExternalCode = externalCode, Name = name, LedgerCategoryId = categoryId,
        Debit = debit, Credit = credit, Kind = "Closing", AccountType = type
    };

    // Totales y huella del asiento ya armado.
    private static void Finish(JournalEntry entry)
    {
        entry.TotalDebit = entry.Lines.Sum(l => l.Debit);
        entry.TotalCredit = entry.Lines.Sum(l => l.Credit);
        var text = new StringBuilder($"closing|{entry.Date:yyyy-MM-dd}");
        foreach (var l in entry.Lines.OrderBy(l => l.Code, StringComparer.Ordinal))
        {
            text.Append('\n').Append(l.Code).Append('|').Append(l.Debit.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('|').Append(l.Credit.ToString("0.00", CultureInfo.InvariantCulture));
        }

        entry.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
