using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Libro Diario, fase 4 (cierre y estados): el cierre del ejercicio (ingresos y gastos contra el resultado y este contra resultados acumulados), su
/// reapertura con contraasientos, el balance general y el estado de resultados, y el cierre de mes que exige el mes asentado.
/// </summary>
public class FiscalYearClosingTests
{
    private static readonly Dictionary<Guid, LedgerCategoryType> NoPlan = [];

    // ═════════════════════════════════════════════════════════════════════════
    // Clasificacion y estados (puros)
    // ═════════════════════════════════════════════════════════════════════════

    private static BookLine L(
        string code, decimal debit, decimal credit, DateOnly date, int number = 1, LedgerCategoryType? type = null, bool closing = false, Guid? category = null) =>
        new(Guid.NewGuid(), date.Year, number, date, "Asiento", 1, code, null, $"Cuenta {code}", debit, credit, category, type, closing);

    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateOnly Mar = new(2026, 3, 10);
    private static readonly DateOnly Dec = new(2026, 12, 31);

    [Fact]
    public void LaClaseDeLaCuentaSalDelRenglonDelPlanOSiFaltaDelPrimerDigitoDelCodigo()
    {
        var id = Guid.NewGuid();
        var plan = new Dictionary<Guid, LedgerCategoryType> { [id] = LedgerCategoryType.Liability };

        Assert.Equal(LedgerCategoryType.Fund, JournalStatementsCalculator.TypeOf(L("9.9", 1, 0, Mar, type: LedgerCategoryType.Fund), plan));
        Assert.Equal(LedgerCategoryType.Liability, JournalStatementsCalculator.TypeOf(L("9.9", 1, 0, Mar, category: id), plan));
        Assert.Equal(LedgerCategoryType.Income, JournalStatementsCalculator.TypeOf(L("4.1.01", 0, 1, Mar), NoPlan));
        Assert.Equal(LedgerCategoryType.Expense, JournalStatementsCalculator.TypeOf(L("5.01.01", 1, 0, Mar), NoPlan));
        Assert.Equal(LedgerCategoryType.Asset, JournalStatementsCalculator.TypeOf(L("1.1.03", 1, 0, Mar), NoPlan));
        Assert.Null(JournalStatementsCalculator.TypeOf(L("9.9.9", 1, 0, Mar), NoPlan));
    }

    private static List<BookLine> Year()
    {
        var lines = new List<BookLine>
        {
            L("1.1.03", 1_000m, 0m, Jan, 1, LedgerCategoryType.Asset), L("3.2.01", 0m, 1_000m, Jan, 1, LedgerCategoryType.Fund),               // apertura
            L("1.1.03", 500m, 0m, Mar, 2, LedgerCategoryType.Asset), L("4.1.01", 0m, 500m, Mar, 2, LedgerCategoryType.Income),                  // cobro
            L("5.01.01", 200m, 0m, Mar, 3, LedgerCategoryType.Expense), L("1.1.03", 0m, 200m, Mar, 3, LedgerCategoryType.Asset)                // gasto
        };
        return lines;
    }

    [Fact]
    public void ElBalanceGeneralDeUnEjercicioSinCerrarSumaElResultadoPendienteYCuadra()
    {
        var s = JournalStatementsCalculator.Build(Year(), NoPlan, Jan, Dec);

        Assert.Equal(1_300m, s.TotalAssets);
        Assert.Equal(0m, s.TotalLiabilities);
        Assert.Equal(1_000m, s.TotalEquity);
        Assert.Equal(300m, s.PendingResult);
        Assert.Equal(300m, s.Result);
        Assert.Equal(500m, s.TotalIncome);
        Assert.Equal(200m, s.TotalExpenses);
        Assert.False(s.YearClosed);
        Assert.True(s.IsBalanced);
        Assert.Equal(0m, s.Difference);
    }

    [Fact]
    public void ConElEjercicioCerradoElResultadoPasaAFondosPeroElEstadoDeResultadosLoSigueMostrando()
    {
        var lines = Year();
        lines.AddRange([
            L("4.1.01", 500m, 0m, Dec, 4, LedgerCategoryType.Income, closing: true), L("5.01.01", 0m, 200m, Dec, 4, LedgerCategoryType.Expense, closing: true),
            L("3.2.02", 0m, 300m, Dec, 4, LedgerCategoryType.Fund, closing: true),
            L("3.2.02", 300m, 0m, Dec, 5, LedgerCategoryType.Fund, closing: true), L("3.2.01", 0m, 300m, Dec, 5, LedgerCategoryType.Fund, closing: true)]);

        var s = JournalStatementsCalculator.Build(lines, NoPlan, Jan, Dec);

        Assert.True(s.YearClosed);
        Assert.Equal(0m, s.PendingResult);
        Assert.Equal(1_300m, s.TotalEquity);                                                      // 1.000 + el resultado
        Assert.Equal(1_300m, s.TotalAssets);
        Assert.Equal(300m, s.Result);                                                             // el cierre no borra el estado de resultados
        Assert.Equal(500m, s.TotalIncome);
        Assert.True(s.IsBalanced);

        var beforeClosing = JournalStatementsCalculator.Build(lines, NoPlan, Jan, new DateOnly(2026, 6, 30));   // a mitad de año el cierre todavia no existe
        Assert.False(beforeClosing.YearClosed);
        Assert.Equal(300m, beforeClosing.PendingResult);
    }

    [Fact]
    public void UnaCuentaDeClaseDesconocidaSeInformaYElBalanceNoCuadra()
    {
        var lines = Year();
        lines.Add(L("9.9.9", 50m, 0m, Mar, 6));
        lines.Add(L("1.1.03", 0m, 50m, Mar, 6, LedgerCategoryType.Asset));

        var s = JournalStatementsCalculator.Build(lines, NoPlan, Jan, Dec);

        Assert.Equal(50m, s.UnclassifiedNet);
        Assert.False(s.IsBalanced);
    }

    [Fact]
    public void LasCuentasDeResultadoSalenSinLasDeBalanceNiLasSaldadas()
    {
        var accounts = JournalStatementsCalculator.ResultAccounts(Year(), NoPlan);

        Assert.Equal(["4.1.01", "5.01.01"], accounts.Select(a => a.Code).ToArray());
        Assert.Equal(-500m, accounts[0].Balance);                                                 // ingreso: saldo acreedor
        Assert.Equal(200m, accounts[1].Balance);
        Assert.Empty(JournalStatementsCalculator.ResultAccounts(
            [L("4.1.01", 100m, 0m, Mar, 1, LedgerCategoryType.Income), L("4.1.01", 0m, 100m, Mar, 2, LedgerCategoryType.Income)], NoPlan));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Con la base: cierre, reapertura y permisos (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<LedgerContext> Context(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private sealed record Services(AccountingEntriesService Accounting, JournalPostingService Journal, JournalBooksService Books, FiscalYearClosingService Closing);

    private static Services Build(FinanceEnv env)
    {
        var db = env.T.Db;
        var accounting = new AccountingEntriesService(db, new FinanceLedgerService(db), env.Tenant);
        var journal = new JournalPostingService(db, accounting, env.Tenant);
        var books = new JournalBooksService(db, new AccountingBooksService(db, env.Tenant));
        return new Services(accounting, journal, books, new FiscalYearClosingService(db, books, journal, env.Tenant));
    }

    // Edificio con Finanzas desde 2024, un banco con saldo inicial y movimientos en 2024 y 2025 (ya terminados), todo con su cuenta del plan.
    private static async Task<(FinanceEnv Env, Services Svc)> Seed(decimal opening = 2_000_000m)
    {
        var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        var settings = env.T.Db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id);
        settings.FinanceStartDate = new DateOnly(2024, 1, 1);
        settings.FiscalYearStartMonth = 1;
        env.T.Db.FinancialAccounts.Single(x => x.Id == bank.Id).OpeningBalance = opening;
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = ExpensePeriodStatus.Published;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.AddIncome(null, 1_000_000m, new DateOnly(2024, 5, 10));
        env.AddExpense(null, 300_000m, BuildingExpenseCategory.Other, new DateOnly(2024, 6, 10));
        env.AddIncome(null, 400_000m, new DateOnly(2025, 5, 10));
        env.AddExpense(null, 100_000m, BuildingExpenseCategory.Other, new DateOnly(2025, 6, 10));
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var svc = Build(env);
        var (_, error) = await svc.Accounting.UpdateRolesAsync(await Context(env), new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = env.Cat("1.4.06").Id
        }, env.Company.Id, default);
        Assert.Null(error);
        return (env, svc);
    }

    private static async Task Post(FinanceEnv env, Services svc, DateOnly through)
    {
        var (_, error, _) = await svc.Journal.PostAsync(await Context(env), through, env.Company.Id, default);
        Assert.Null(error);
    }

    private static List<JournalEntry> Saved(FinanceEnv env) =>
        env.T.NewContext().JournalEntries.Include(x => x.Lines).Where(x => x.BuildingId == env.Building.Id).OrderBy(x => x.FiscalYear).ThenBy(x => x.Number).ToList();

    [SqlServerFact]
    public async Task ElCierreSaldaIngresosYGastosContraElResultadoYElResultadoContraResultadosAcumulados()
    {
        var (env, svc) = await Seed();
        using var _ = env;
        await Post(env, svc, new DateOnly(2024, 12, 31));
        var ctx = await Context(env);

        var preview = await svc.Closing.GetAsync(ctx, 2024, canEdit: true, default);
        Assert.True(preview.CanClose, string.Join(" | ", preview.Blockers));
        Assert.False(preview.IsClosed);
        Assert.Equal(1_000_000m, preview.TotalIncome);
        Assert.Equal(300_000m, preview.TotalExpense);
        Assert.Equal(700_000m, preview.Result);
        Assert.Equal("3.2.02 Resultado del período", preview.ResultAccount);
        Assert.Equal("3.2.01 Resultados acumulados", preview.EquityAccount);
        Assert.Equal(2, preview.Accounts.Count);

        var (result, error, conflict) = await svc.Closing.CloseAsync(ctx, 2024, env.Company.Id, default);

        Assert.Null(error);
        Assert.False(conflict);
        Assert.Equal(2, result!.CreatedCount);
        var closings = Saved(env).Where(e => e.Kind == JournalEntryKind.Closing).ToList();
        Assert.Equal(2, closings.Count);
        Assert.All(closings, c => Assert.Equal(new DateOnly(2024, 12, 31), c.Date));
        Assert.All(closings, c => Assert.Equal(c.TotalDebit, c.TotalCredit));

        var first = closings[0].Lines;
        Assert.Equal(1_000_000m, first.Where(l => l.AccountType == LedgerCategoryType.Income).Sum(l => l.Debit));       // el ingreso se debita
        Assert.Equal(300_000m, first.Where(l => l.AccountType == LedgerCategoryType.Expense).Sum(l => l.Credit));       // el gasto se acredita
        Assert.Equal(700_000m, first.Single(l => l.Code == "3.2.02").Credit);                                          // el resultado se acredita (superavit)
        var second = closings[1].Lines;
        Assert.Equal(700_000m, second.Single(l => l.Code == "3.2.02").Debit);
        Assert.Equal(700_000m, second.Single(l => l.Code == "3.2.01").Credit);

        var after = await svc.Closing.GetAsync(ctx, 2024, canEdit: true, default);
        Assert.True(after.IsClosed);
        Assert.False(after.CanClose);
        Assert.Equal(2, after.ClosingLabels.Count);
        Assert.Equal(700_000m, after.Result);                                                                          // sigue mostrando lo que se cerro

        // Las cuentas de resultado quedan en cero y el balance cuadra con el resultado en fondos.
        var statements = await svc.Books.StatementsAsync(ctx, 2024, null, default);
        Assert.True(statements.YearClosed);
        Assert.Equal(0m, statements.PendingResult);
        Assert.Equal(2_000_000m + 700_000m, statements.TotalEquity);
        Assert.Equal(2_700_000m, statements.TotalAssets);
        Assert.True(statements.IsBalanced);
        Assert.Equal(700_000m, statements.Result);
        var trial = await svc.Books.TrialBalanceAsync(ctx, 2024, null, null, default);
        Assert.True(trial.IsBalanced);
        Assert.All(trial.Rows.Where(r => r.Code.StartsWith('4') || r.Code.StartsWith('5')), r => Assert.Equal(0m, r.ClosingDebit + r.ClosingCredit));

        var again = await svc.Closing.CloseAsync(ctx, 2024, env.Company.Id, default);
        Assert.True(again.Conflict);
        Assert.Contains("ya está cerrado", again.Error);
    }

    [SqlServerFact]
    public async Task ElEjercicioSeCierraEnOrdenYUnEjercicioSinTerminarONoAsentadoNoSeCierra()
    {
        var (env, svc) = await Seed();
        using var _ = env;
        await Post(env, svc, new DateOnly(2025, 12, 31));
        var ctx = await Context(env);

        var out2025 = await svc.Closing.GetAsync(ctx, 2025, canEdit: true, default);
        Assert.False(out2025.CanClose);
        Assert.Contains(out2025.Blockers, b => b.Contains("Cerrá primero el ejercicio anterior"));
        Assert.True((await svc.Closing.CloseAsync(ctx, 2025, env.Company.Id, default)).Conflict);
        Assert.Empty(Saved(env).Where(e => e.Kind == JournalEntryKind.Closing));

        Assert.Null((await svc.Closing.CloseAsync(ctx, 2024, env.Company.Id, default)).Error);
        Assert.Null((await svc.Closing.CloseAsync(ctx, 2025, env.Company.Id, default)).Error);

        // El ejercicio en curso todavia no termino.
        var current = FinancePeriods.FiscalYearOf(FinancePeriods.Today(), 1);
        var running = await svc.Closing.GetAsync(ctx, current, canEdit: true, default);
        Assert.False(running.CanClose);
        Assert.Contains(running.Blockers, b => b.Contains("todavía no terminó"));
    }

    [SqlServerFact]
    public async Task SinAsentarHastaElFinDelEjercicioOConDiferenciasPendientesNoSeCierra()
    {
        var (env, svc) = await Seed();
        using var _ = env;
        await Post(env, svc, new DateOnly(2024, 6, 30));
        var ctx = await Context(env);

        var notPosted = await svc.Closing.GetAsync(ctx, 2024, canEdit: true, default);
        Assert.Contains(notPosted.Blockers, b => b.Contains("Asentá el Libro diario hasta el 31/12/2024"));

        await Post(env, svc, new DateOnly(2024, 12, 31));
        env.T.Db.BuildingExpenses.Single(x => x.Amount == 300_000m).Amount = 350_000m;                  // un gasto ya asentado cambia
        env.T.Db.SaveChanges();

        var changed = await svc.Closing.GetAsync(await Context(env), 2024, canEdit: true, default);
        Assert.Contains(changed.Blockers, b => b.Contains("asentados que cambiaron"));
        Assert.False(changed.CanClose);
    }

    [SqlServerFact]
    public async Task ReabrirElEjercicioAnulaLosCierresConContraasientosYPideMotivo()
    {
        var (env, svc) = await Seed();
        using var _ = env;
        await Post(env, svc, new DateOnly(2024, 12, 31));
        var ctx = await Context(env);
        Assert.Null((await svc.Closing.CloseAsync(ctx, 2024, env.Company.Id, default)).Error);

        var noReason = await svc.Closing.ReopenAsync(ctx, 2024, "  ", env.Company.Id, default);
        Assert.False(noReason.Conflict);
        Assert.Contains("motivo", noReason.Error);

        var reopened = await svc.Closing.ReopenAsync(ctx, 2024, "Se cargó mal un gasto", env.Company.Id, default);
        Assert.Null(reopened.Error);
        Assert.Equal(2, reopened.Result!.CreatedCount);
        var reversals = Saved(env).Where(e => e.Kind == JournalEntryKind.Reversal).ToList();
        Assert.Equal(2, reversals.Count);
        Assert.All(reversals, r => Assert.Contains("Motivo: Se cargó mal un gasto", r.Description));
        Assert.Contains("Reopened", env.T.NewContext().FinanceAuditLogs.Select(x => x.Action).ToList());

        var open = await svc.Closing.GetAsync(await Context(env), 2024, canEdit: true, default);
        Assert.False(open.IsClosed);
        Assert.True(open.CanClose, string.Join(" | ", open.Blockers));
        Assert.Equal(700_000m, open.Result);                                                                            // las cuentas de resultado volvieron

        Assert.Null((await svc.Closing.CloseAsync(await Context(env), 2024, env.Company.Id, default)).Error);           // se puede cerrar de nuevo
        Assert.Equal(4, Saved(env).Count(e => e.Kind == JournalEntryKind.Closing));
        Assert.True((await svc.Closing.ReopenAsync(await Context(env), 2025, "x", env.Company.Id, default)).Conflict);   // 2025 no esta cerrado
    }

    [SqlServerFact]
    public async Task NoSeReabreUnEjercicioSiElSiguienteEstaCerrado()
    {
        var (env, svc) = await Seed();
        using var _ = env;
        await Post(env, svc, new DateOnly(2025, 12, 31));
        var ctx = await Context(env);
        Assert.Null((await svc.Closing.CloseAsync(ctx, 2024, env.Company.Id, default)).Error);
        Assert.Null((await svc.Closing.CloseAsync(ctx, 2025, env.Company.Id, default)).Error);

        var blocked = await svc.Closing.ReopenAsync(ctx, 2024, "x", env.Company.Id, default);

        Assert.True(blocked.Conflict);
        Assert.Contains("siguiente", blocked.Error);
        Assert.False((await svc.Closing.GetAsync(ctx, 2024, canEdit: true, default)).CanReopen);
        Assert.True((await svc.Closing.GetAsync(ctx, 2025, canEdit: true, default)).CanReopen);
        Assert.Null((await svc.Closing.ReopenAsync(ctx, 2025, "x", env.Company.Id, default)).Error);                     // primero el ultimo
    }

    [SqlServerFact]
    public async Task ElSegundoEjercicioArrancaConLasCuentasDeResultadoEnCeroYElSaldoDeLasDemasArrastrado()
    {
        var (env, svc) = await Seed();
        using var _ = env;
        await Post(env, svc, new DateOnly(2025, 12, 31));
        var ctx = await Context(env);
        Assert.Null((await svc.Closing.CloseAsync(ctx, 2024, env.Company.Id, default)).Error);

        var trial = await svc.Books.TrialBalanceAsync(ctx, 2025, null, null, default);

        Assert.True(trial.IsBalanced);
        var bank = trial.Rows.Single(r => r.Code == "1.1.03");
        Assert.Equal(2_700_000m, bank.OpeningDebit);                                                                    // saldo del banco arrastrado de 2024
        var income = trial.Rows.Where(r => r.Code.StartsWith('4')).ToList();
        Assert.All(income, r => Assert.Equal(0m, r.OpeningDebit + r.OpeningCredit));                                    // los ingresos arrancan en cero
        Assert.Equal(700_000m, trial.Rows.Single(r => r.Code == "3.2.01").OpeningCredit - 0m - 2_000_000m);              // resultados acumulados = apertura + resultado 2024
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Cierre de mes y permisos
    // ═════════════════════════════════════════════════════════════════════════

    [SqlServerFact]
    public async Task ElMesSoloSeCierraSiElLibroDiarioEstaAsentadoHastaSuUltimoDia()
    {
        var env = new FinanceEnv(sqlServer: true);
        using var _ = env;
        var bank = env.Setup();
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = ExpensePeriodStatus.Published;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var svc = Build(env);
        await svc.Accounting.UpdateRolesAsync(await Context(env), new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = env.Cat("1.4.06").Id
        }, env.Company.Id, default);
        var settings = env.T.Db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id);
        settings.PeriodClosingEnabled = true;
        env.T.Db.SaveChanges();
        var start = settings.FinanceStartDate!.Value;
        var monthEnd = FinancePeriods.EndOfMonth(start.Year, start.Month);
        await Post(env, svc, start.AddDays(2));                                                                         // asentado hasta mitad del mes

        var config = new BuildingConfigController(env.T.Db, env.Access, env.Tenant, new BuildingConfigOverviewService(env.T.Db, new FinanceModuleGate(env.T.Db)));
        var blocked = Assert.IsType<ConflictObjectResult>((await config.CloseMonth(env.Building.Id, start.Year, start.Month, default)).Result);
        Assert.Contains("journal_not_posted", blocked.Value!.ToString());
        Assert.Contains(monthEnd.ToString("dd/MM/yyyy"), blocked.Value.ToString());

        await Post(env, svc, monthEnd);
        FinanceEnv.Ok(await config.CloseMonth(env.Building.Id, start.Year, start.Month, default));
        Assert.Single(env.T.NewContext().FinancePeriodClosures.ToList());
    }

    private static FinanceJournalClosingController Api(FinanceEnv env, Services svc) => new(
        env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), svc.Closing);

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result.Result).StatusCode);

    [Fact]
    public async Task LoVenLosCuatroRolesPeroCierranYReabrenSoloSuperAdminYAdministrador()
    {
        using var env = new FinanceEnv();
        env.Setup();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var api = Api(env, Build(env));

        foreach (var role in new[] { "SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            var dto = FinanceEnv.Ok(await api.Get(env.Building.Id, null, default));
            Assert.Equal(role is "SuperAdmin" or "CompanyAdmin", dto.CanEdit);
            Assert.False(dto.CanClose);                                                                                  // nada asentado todavia
        }

        foreach (var role in new[] { "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            AssertStatus(await api.Close(env.Building.Id, new CloseFiscalYearRequest { FiscalYear = 2025 }, default), 403);
            AssertStatus(await api.Reopen(env.Building.Id, new ReopenFiscalYearRequest { FiscalYear = 2025, Reason = "x" }, default), 403);
        }

        env.LoginAs("CompanyAdmin");
        AssertStatus(await api.Close(env.Building.Id, new CloseFiscalYearRequest { FiscalYear = 2025 }, default), 409);
        env.Access.Buildings.Clear();
        Assert.IsNotType<OkObjectResult>((await api.Get(env.Building.Id, null, default)).Result);
    }
}
