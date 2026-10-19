using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Libro Diario, fase 4 (apertura): el asiento con los saldos iniciales de las cuentas financieras, contra «Resultados acumulados» (3.2.01) o la
/// cuenta que se elija por edificio. Entra en los sugeridos y en el primer asentado, y se corrige con contraasiento si los saldos cambian.
/// </summary>
public class JournalOpeningTests
{
    private static readonly DateOnly Start = new(2026, 8, 1);
    private static readonly Guid BuildingId = Guid.NewGuid();
    private static readonly Guid BankId = Guid.NewGuid();
    private static readonly Guid CashId = Guid.NewGuid();
    private static readonly Guid FundId = Guid.NewGuid();

    private static readonly AccountingPlanAccount BankPlan = new(Guid.NewGuid(), "1.1.03", null, "Banco cuenta corriente", LedgerCategoryType.Asset);
    private static readonly AccountingPlanAccount CashPlan = new(Guid.NewGuid(), "1.1.01", null, "Caja administración", LedgerCategoryType.Asset);
    private static readonly AccountingPlanAccount FundPlan = new(Guid.NewGuid(), "3.1.01", null, "Fondo de reserva", LedgerCategoryType.Fund);
    private static readonly AccountingPlanAccount EquityPlan = new(Guid.NewGuid(), "3.2.01", null, "Resultados acumulados", LedgerCategoryType.Fund);

    private static AccountingBuildInput Input(AccountingOpeningInput? opening, bool plans = true) => new()
    {
        FinancialPlan = plans
            ? new Dictionary<Guid, AccountingPlanAccount> { [BankId] = BankPlan, [CashId] = CashPlan, [FundId] = FundPlan }
            : new Dictionary<Guid, AccountingPlanAccount>(),
        FinancialNames = new Dictionary<Guid, string> { [BankId] = "Banco Itaú", [CashId] = "Caja", [FundId] = "Fondo de Reserva" },
        RubroPlan = _ => null,
        VatRates = new Dictionary<(LedgerSourceType Type, Guid Id), decimal>(),
        Opening = opening
    };

    private static AccountingOpeningInput Opening(decimal bank, decimal cash, decimal fund, AccountingPlanAccount? equity = null) =>
        new(Start, BuildingId, [(BankId, bank), (CashId, cash), (FundId, fund)], equity ?? EquityPlan);

    // ═════════════════════════════════════════════════════════════════════════
    // Armado del asiento (puro)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LaAperturaDebitaLasCuentasConSaldoYAcreditaResultadosAcumuladosPorLaSuma()
    {
        var result = AccountingEntryBuilder.Build([], Input(Opening(50_000_000m, 2_000_000m, 30_000_000m)));

        var entry = Assert.Single(result.Entries);
        Assert.Equal(LedgerSourceType.OpeningBalance, entry.SourceType);
        Assert.Equal(BuildingId, entry.SourceId);
        Assert.Null(entry.SourceAccountId);
        Assert.Equal(Start, entry.Date);
        Assert.Equal(1, entry.Number);
        Assert.Contains("apertura", entry.Description);
        Assert.True(entry.IsComplete);
        Assert.Equal(82_000_000m, entry.Debit);
        Assert.Equal(entry.Debit, entry.Credit);

        var bank = entry.Lines.Single(l => l.Code == "1.1.03");
        Assert.Equal(50_000_000m, bank.Debit);
        Assert.Equal(LedgerCategoryType.Asset, bank.AccountType);
        Assert.Equal(30_000_000m, entry.Lines.Single(l => l.Code == "3.1.01").Debit);
        var equity = entry.Lines.Single(l => l.Code == "3.2.01");
        Assert.Equal(82_000_000m, equity.Credit);
        Assert.Equal("Equity", equity.Kind);
        Assert.Equal(LedgerCategoryType.Fund, equity.AccountType);
    }

    [Fact]
    public void UnaCuentaConSaldoNegativoVaAlHaberYLaContrapartidaSeAjusta()
    {
        var entry = Assert.Single(AccountingEntryBuilder.Build([], Input(Opening(10_000_000m, -1_000_000m, 0m))).Entries);

        Assert.Equal(1_000_000m, entry.Lines.Single(l => l.Code == "1.1.01").Credit);
        Assert.Equal(9_000_000m, entry.Lines.Single(l => l.Code == "3.2.01").Credit);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.DoesNotContain(entry.Lines, l => l.Code == "3.1.01");                              // el fondo en cero no genera renglon
    }

    [Fact]
    public void SinSaldosInicialesNoHayAsientoDeApertura()
    {
        Assert.Empty(AccountingEntryBuilder.Build([], Input(Opening(0m, 0m, 0m))).Entries);
        Assert.Empty(AccountingEntryBuilder.Build([], Input(null)).Entries);
    }

    [Fact]
    public void SiFaltaLaCuentaDelPlanDeUnBancoOLaContrapartidaElAsientoQuedaIncompleto()
    {
        var noPlans = Assert.Single(AccountingEntryBuilder.Build([], Input(Opening(1_000m, 0m, 0m), plans: false)).Entries);
        Assert.False(noPlans.IsComplete);
        Assert.Contains(noPlans.Lines, l => l.Missing && l.Name.Contains("Banco Itaú"));

        var noEquity = Assert.Single(AccountingEntryBuilder.Build([], Input(new AccountingOpeningInput(Start, BuildingId, [(BankId, 1_000m)], null))).Entries);
        Assert.False(noEquity.IsComplete);
        Assert.Contains(noEquity.Lines, l => l.Missing && l.Name.Contains("apertura"));
    }

    [Fact]
    public void LaAperturaVaPrimeroYSumaALasSumasPorCuenta()
    {
        var result = AccountingEntryBuilder.Build([], Input(Opening(100m, 0m, 0m)));

        Assert.Equal(["1.1.03", "3.2.01"], result.Totals.Select(t => t.Code).ToArray());
        Assert.Equal(100m, result.Totals.Single(t => t.Code == "1.1.03").Debit);
        Assert.Equal(100m, result.Totals.Single(t => t.Code == "3.2.01").Credit);
    }

    [Fact]
    public void LaHuellaDeLaAperturaIncluyeLaCuentaDelPlanDeCadaRenglon()
    {
        var a = Assert.Single(AccountingEntryBuilder.Build([], Input(Opening(50m, 2m, 0m))).Entries);
        var sameAmounts = Assert.Single(AccountingEntryBuilder.Build([], Input(Opening(50m, 2m, 0m))).Entries);
        var swapped = Assert.Single(AccountingEntryBuilder.Build([], Input(Opening(2m, 50m, 0m))).Entries);        // mismos importes en otras cuentas

        Assert.Equal(JournalPostingService.HashOf(a), JournalPostingService.HashOf(sameAmounts));
        Assert.NotEqual(JournalPostingService.HashOf(a), JournalPostingService.HashOf(swapped));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Contrapartidas por edificio y asentado (con la base)
    // ═════════════════════════════════════════════════════════════════════════

    private static FinanceEnv NewEnv(decimal openingBalance = 5_000_000m)
    {
        var env = new FinanceEnv();
        var bank = env.Setup();
        bank.OpeningBalance = openingBalance;
        env.T.Db.FinancialAccounts.Single(x => x.Id == bank.Id).OpeningBalance = openingBalance;
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = ExpensePeriodStatus.Published;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        return env;
    }

    private static async Task<LedgerContext> Context(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static AccountingEntriesService Service(FinanceEnv env) =>
        new(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);

    [Fact]
    public async Task SinAsignarRigeResultadosAcumuladosYElResultadoDelPeriodoDelPlanGenerico()
    {
        using var env = NewEnv();

        var roles = await Service(env).RolesAsync(await Context(env), canEdit: true, default);

        Assert.Equal(env.Cat("3.2.01").Id, roles.DefaultOpeningEquityCategoryId);
        Assert.Equal(env.Cat("3.2.02").Id, roles.DefaultPeriodResultCategoryId);
        Assert.Null(roles.OpeningEquityCategoryId);
        Assert.Null(roles.PeriodResultCategoryId);
    }

    [Fact]
    public async Task LaContrapartidaSeEligePorEdificioYSePuedeVolverAlPorDefecto()
    {
        using var env = NewEnv();
        var other = env.Cat("3.1.01");
        var service = Service(env);

        var (set, error) = await service.UpdateRolesAsync(await Context(env),
            new UpdateAccountingRolesRequest { SetOpeningEquity = true, OpeningEquityCategoryId = other.Id }, env.Company.Id, default);
        Assert.Null(error);
        Assert.Equal(other.Id, set!.OpeningEquityCategoryId);
        Assert.Equal("3.1.01", AccountingEntriesService.EquityAccount(await Context(env), env.T.NewContext().LedgerAccountRoles.Where(x => !x.IsDeleted).ToList(),
            LedgerAccountRoleKind.OpeningEquity)!.Code);
        Assert.Contains("openingEquity", env.T.NewContext().FinanceAuditLogs.Single(x => x.Section == ConfigSectionKeys.Accounting).ChangesJson);

        var (reset, error2) = await service.UpdateRolesAsync(await Context(env),
            new UpdateAccountingRolesRequest { SetOpeningEquity = true, OpeningEquityCategoryId = null }, env.Company.Id, default);
        Assert.Null(error2);
        Assert.Null(reset!.OpeningEquityCategoryId);
        Assert.Equal("3.2.01", AccountingEntriesService.EquityAccount(await Context(env), [], LedgerAccountRoleKind.OpeningEquity)!.Code);
    }

    [Fact]
    public async Task LaContrapartidaDebeSerUnaCuentaFinalDeFondos()
    {
        using var env = NewEnv();
        var service = Service(env);

        var asset = await service.UpdateRolesAsync(await Context(env),
            new UpdateAccountingRolesRequest { SetOpeningEquity = true, OpeningEquityCategoryId = env.Cat("1.1.03").Id }, env.Company.Id, default);
        var income = await service.UpdateRolesAsync(await Context(env),
            new UpdateAccountingRolesRequest { SetPeriodResult = true, PeriodResultCategoryId = env.Cat("4.1.01").Id }, env.Company.Id, default);
        var missing = await service.UpdateRolesAsync(await Context(env),
            new UpdateAccountingRolesRequest { SetOpeningEquity = true, OpeningEquityCategoryId = Guid.NewGuid() }, env.Company.Id, default);

        Assert.Contains("fondos", asset.Error);
        Assert.Contains("fondos", income.Error);
        Assert.NotNull(missing.Error);
        Assert.Empty(env.T.NewContext().LedgerAccountRoles.ToList());
    }

    private static async Task AssignBank(FinanceEnv env)
    {
        var bank = env.T.Db.FinancialAccounts.Single(x => x.BuildingId == env.Building.Id);
        var (_, error) = await Service(env).UpdateRolesAsync(await Context(env), new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = env.Cat("1.4.06").Id
        }, env.Company.Id, default);
        Assert.Null(error);
    }

    [SqlServerFact]
    public async Task LaAperturaEntraEnLosSugeridosYAlAsentarSaleComoPrimerAsientoDeTipoApertura()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        env.T.Db.FinancialAccounts.Single(x => x.Id == bank.Id).OpeningBalance = 5_000_000m;
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = ExpensePeriodStatus.Published;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.AddIncome(null, 1_000_000m, FinanceEnv.Yesterday.AddDays(-5));
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        await AssignBank(env);
        var api = new FinanceJournalController(
            env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db),
            new JournalPostingService(env.T.Db, Service(env), env.Tenant),
            new JournalBooksService(env.T.Db, new AccountingBooksService(env.T.Db, env.Tenant)));

        var suggested = (await Service(env).EntriesAsync(await Context(env), (await Context(env)).StartDate, FinanceEnv.Yesterday, default)).Entries!;
        Assert.Equal(2, suggested.EntryCount);
        Assert.Equal(LedgerSourceType.OpeningBalance, suggested.Entries[0].SourceType);
        Assert.Equal(5_000_000m, suggested.Entries[0].Debit);

        FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = FinanceEnv.Yesterday }, default));

        var saved = env.T.NewContext().JournalEntries.Include(x => x.Lines).Where(x => x.BuildingId == env.Building.Id).OrderBy(x => x.Number).ToList();
        Assert.Equal([JournalEntryKind.Opening, JournalEntryKind.Auto], saved.Select(e => e.Kind).ToArray());
        Assert.Equal(1, saved[0].Number);
        Assert.Equal((await Context(env)).StartDate, saved[0].Date);
        Assert.All(saved.SelectMany(e => e.Lines), l => Assert.NotNull(l.AccountType));
        Assert.Equal(6_000_000m, saved.SelectMany(e => e.Lines).Where(l => l.Code == "1.1.03").Sum(l => l.Debit - l.Credit));   // = saldo real del banco
        Assert.Empty(FinanceEnv.Ok(await api.Differences(env.Building.Id, default)).Items);
    }

    [SqlServerFact]
    public async Task UnCambioDeLosSaldosInicialesDespuesDeAsentarSeCorrigeConContraasiento()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        env.T.Db.FinancialAccounts.Single(x => x.Id == bank.Id).OpeningBalance = 5_000_000m;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        await AssignBank(env);
        var journal = new JournalPostingService(env.T.Db, Service(env), env.Tenant);
        var ctx = await Context(env);
        Assert.Null((await journal.PostAsync(ctx, ctx.StartDate, env.Company.Id, default)).Error);

        env.T.Db.FinancialAccounts.Single(x => x.Id == bank.Id).OpeningBalance = 6_500_000m;                // se corrigio el saldo inicial
        env.T.Db.SaveChanges();
        var differences = await journal.DifferencesAsync(await Context(env), default);

        var diff = Assert.Single(differences.Items);
        Assert.Equal(JournalDifferenceKind.Changed, diff.Kind);
        Assert.Equal(LedgerSourceType.OpeningBalance, diff.SourceType);
        Assert.Equal("Saldos iniciales modificados", diff.Cause);
        Assert.Equal(5_000_000m, diff.AmountBefore);
        Assert.Equal(6_500_000m, diff.AmountAfter);

        var post = await journal.PostAsync(await Context(env), ctx.StartDate, env.Company.Id, default);
        Assert.Null(post.Error);
        Assert.Equal(2, post.Result!.CorrectionEntryCount);
        var saved = env.T.NewContext().JournalEntries.Include(x => x.Lines).Where(x => x.BuildingId == env.Building.Id).OrderBy(x => x.Number).ToList();
        Assert.Equal([JournalEntryKind.Opening, JournalEntryKind.Reversal, JournalEntryKind.Opening], saved.Select(e => e.Kind).ToArray());
        Assert.Equal(6_500_000m, saved.SelectMany(e => e.Lines).Where(l => l.Code == "1.1.03").Sum(l => l.Debit - l.Credit));
    }

    [SqlServerFact]
    public async Task UnaAperturaOlvidadaSeAsientaDespuesComoAsientoFaltante()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = ExpensePeriodStatus.Published;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        await AssignBank(env);
        var journal = new JournalPostingService(env.T.Db, Service(env), env.Tenant);
        var ctx = await Context(env);
        Assert.Null((await journal.PostAsync(ctx, ctx.StartDate, env.Company.Id, default)).Error);        // sin saldos iniciales: no hay apertura

        env.T.Db.FinancialAccounts.Single(x => x.Id == bank.Id).OpeningBalance = 3_000_000m;                // despues se cargaron
        env.T.Db.SaveChanges();

        var diff = Assert.Single((await journal.DifferencesAsync(await Context(env), default)).Items);
        Assert.Equal(JournalDifferenceKind.Missing, diff.Kind);
        Assert.Equal("Asiento de apertura sin asentar", diff.Cause);
    }
}
