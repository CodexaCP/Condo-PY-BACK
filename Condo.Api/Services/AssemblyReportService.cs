using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Informe para la asamblea de propietarios: saldos por cuenta, estado de resultados por rubro (criterio de caja), ejecucion presupuestaria del
/// ejercicio, fondo de reserva, morosidad por antiguedad y cuentas por pagar. Usa los mismos servicios y reglas que las pantallas de Finanzas
/// para que los numeros coincidan. La morosidad va solo agregada: no figura ninguna unidad ni propietario.
/// </summary>
public class AssemblyReportService(
    ICondoDbContext dbContext,
    FinanceLedgerService ledger,
    FinanceReportService reports,
    FinancePayablesService payables)
{
    // Un informe no abarca mas de esto (igual que los demas reportes por rango).
    public const int MaxRangeDays = 400;
    public const int NotesMaxLength = 4000;

    public async Task<(AssemblyReportDto? Report, string? Error)> BuildAsync(
        LedgerContext ctx, DateOnly? from, DateOnly? to, string? notes, DateTime generatedAtLocal, CancellationToken ct)
    {
        var today = FinancePeriods.Today();
        var fiscalYear = FinancePeriods.FiscalYearOf(today, ctx.FiscalYearStartMonth);
        var (fiscalStart, _) = FinancePeriods.FiscalYearRange(fiscalYear, ctx.FiscalYearStartMonth);

        var end = to ?? today;
        var begin = from ?? fiscalStart;
        if (end > today) return (null, "La fecha hasta no puede ser futura.");
        if (end < begin) return (null, "La fecha desde no puede ser posterior a la fecha hasta.");
        if (end < ctx.StartDate) return (null, $"El rango es anterior a la fecha de arranque de Finanzas ({ctx.StartDate:dd/MM/yyyy}): todavía no tiene movimientos.");
        if (begin < ctx.StartDate) begin = ctx.StartDate;
        if (end.DayNumber - begin.DayNumber > MaxRangeDays) return (null, $"El informe no puede abarcar más de {MaxRangeDays} días.");

        var cleanNotes = CleanNotes(notes);
        if (cleanNotes is not null && cleanNotes.Length > NotesMaxLength) return (null, $"Las notas no pueden superar los {NotesMaxLength} caracteres.");

        // Todo el libro hasta el cierre del rango: de ahi salen los saldos de apertura y de cierre y los movimientos del rango.
        var rows = await ledger.GetRowsAsync(ctx, ctx.StartDate, end, ct);
        var inRange = rows.Where(r => r.Date >= begin).ToList();
        var before = rows.Where(r => r.Date < begin).ToList();

        var accounts = BuildAccounts(ctx, before, inRange);
        var income = Lines(ctx, inRange.Where(r => r.Direction == LedgerDirection.In));
        var expense = Lines(ctx, inRange.Where(r => r.Direction == LedgerDirection.Out));
        var totalIncome = income.Sum(l => l.Amount);
        var totalExpense = expense.Sum(l => l.Amount);

        var warnings = new List<string>();
        if (accounts.Any(a => a.IsUnassigned))
        {
            warnings.Add("Hay movimientos sin cuenta asignada: figuran en una fila aparte de los saldos.");
        }

        var budget = await BudgetAsync(ctx, end, ct);
        if (budget is null)
        {
            warnings.Add("No se cargó presupuesto para el ejercicio: no se muestra la ejecución presupuestaria.");
        }

        return (new AssemblyReportDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            From = begin,
            To = end,
            GeneratedAt = generatedAtLocal,
            Notes = cleanNotes,
            OpeningBalance = accounts.Sum(a => a.Opening),
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            NetResult = totalIncome - totalExpense,
            ClosingBalance = accounts.Sum(a => a.Closing),
            Accounts = accounts,
            IncomeLines = income,
            ExpenseLines = expense,
            Budget = budget,
            Reserve = await ReserveAsync(ctx, before, inRange, ct),
            SnapshotDate = today,
            Receivables = await payables.ReceivablesAsync(ctx.BuildingId, today, ct),
            Payables = await payables.SummaryAsync(ctx.BuildingId, today, ct),
            Warnings = warnings
        }, null);
    }

    // Texto libre: sin espacios de mas, con saltos de linea normalizados y sin caracteres de control.
    private static string? CleanNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;

        var text = notes.Replace("\r\n", "\n").Replace('\r', '\n');
        text = new string(text.Where(c => c == '\n' || c == '\t' || !char.IsControl(c)).ToArray());
        return text.Trim();
    }

    private static List<AssemblyReportAccountDto> BuildAccounts(LedgerContext ctx, List<LedgerRow> before, List<LedgerRow> inRange)
    {
        var accounts = ctx.Accounts
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a =>
            {
                var inflows = inRange.Where(r => r.AccountId == a.Id && r.Direction == LedgerDirection.In).Sum(r => r.Amount);
                var outflows = inRange.Where(r => r.AccountId == a.Id && r.Direction == LedgerDirection.Out).Sum(r => r.Amount);
                var opening = a.OpeningBalance + before.Where(r => r.AccountId == a.Id).Sum(r => r.Signed);
                return new AssemblyReportAccountDto
                {
                    Name = a.Name,
                    Type = a.Type,
                    Opening = opening,
                    Inflows = inflows,
                    Outflows = outflows,
                    Closing = opening + inflows - outflows
                };
            })
            .ToList();

        // Los movimientos sin cuenta no se pierden: van en una fila aparte para que el total cuadre con el libro.
        var unassignedBefore = before.Where(r => r.AccountId is null).Sum(r => r.Signed);
        var unassignedIn = inRange.Where(r => r.AccountId is null && r.Direction == LedgerDirection.In).Sum(r => r.Amount);
        var unassignedOut = inRange.Where(r => r.AccountId is null && r.Direction == LedgerDirection.Out).Sum(r => r.Amount);
        if (unassignedBefore != 0m || unassignedIn != 0m || unassignedOut != 0m)
        {
            accounts.Add(new AssemblyReportAccountDto
            {
                Name = "Sin cuenta asignada",
                IsUnassigned = true,
                Opening = unassignedBefore,
                Inflows = unassignedIn,
                Outflows = unassignedOut,
                Closing = unassignedBefore + unassignedIn - unassignedOut
            });
        }

        return accounts;
    }

    // Importe por rubro: las salidas con su monto tal cual (una nota de credito del proveedor lo reduce).
    private static List<AssemblyReportRubroDto> Lines(LedgerContext ctx, IEnumerable<LedgerRow> rows) =>
        rows.GroupBy(r => r.RubroKey)
            .Select(g =>
            {
                var category = ctx.CategoryFor(g.Key);
                return new AssemblyReportRubroDto
                {
                    Code = category?.Code ?? string.Empty,
                    Name = category?.Name ?? "Sin clasificar",
                    Amount = g.Sum(r => r.Amount)
                };
            })
            .Where(l => l.Amount != 0m)
            .OrderBy(l => l.Code.Length == 0)
            .ThenBy(l => l.Code, StringComparer.Ordinal)
            .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    // Ejecucion del ejercicio hasta el mes de cierre del rango; nula si no hay ni un renglon presupuestado.
    private async Task<FinanceBudgetVsActualDto?> BudgetAsync(LedgerContext ctx, DateOnly end, CancellationToken ct)
    {
        var (dto, error) = await reports.BudgetVsActualAsync(ctx, end.Year, end.Month, ct);
        if (error is not null || dto is null) return null;

        var hasBudget = dto.IncomeLines.Concat(dto.ExpenseLines).Any(l => l.YtdBudget != 0m);
        return hasBudget ? dto : null;
    }

    private async Task<AssemblyReportReserveDto?> ReserveAsync(LedgerContext ctx, List<LedgerRow> before, List<LedgerRow> inRange, CancellationToken ct)
    {
        var fund = FinanceReportBuilder.FundAccountOf(ctx);
        if (fund is null) return null;

        var percentage = await dbContext.Buildings.AsNoTracking()
            .Where(x => x.Id == ctx.BuildingId)
            .Select(x => x.ReserveFundPercentage)
            .FirstOrDefaultAsync(ct);

        var contributions = inRange.Where(r => r.AccountId == fund.Id && r.Direction == LedgerDirection.In).Sum(r => r.Amount);
        var uses = inRange.Where(r => r.AccountId == fund.Id && r.Direction == LedgerDirection.Out).Sum(r => r.Amount);
        var opening = fund.OpeningBalance + before.Where(r => r.AccountId == fund.Id).Sum(r => r.Signed);
        return new AssemblyReportReserveDto
        {
            AccountName = fund.Name,
            ReserveFundPercentage = percentage,
            Opening = opening,
            Contributions = contributions,
            Uses = uses,
            Closing = opening + contributions - uses
        };
    }
}
