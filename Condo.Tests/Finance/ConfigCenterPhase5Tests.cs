using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Finanzas, conciliacion bancaria manual: las cuentas del saldo conciliado y la diferencia (logica pura), y el flujo de armar, cerrar y reabrir
/// una conciliacion con sus reglas (una sola abierta por cuenta, un movimiento no se concilia dos veces, lo cerrado queda firme, el cierre de
/// periodo, los permisos y el historial). El libro de renglones suma importes en la base: las pruebas del flujo completo usan SQL Server real y
/// se omiten solas sin la variable CONDO_TEST_SQLSERVER.
/// </summary>
public class ConfigCenterPhase5Tests
{
    // ═════════════════════════════════════════════════════════════════════════
    // Calculo (sin base de datos)
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly DateOnly D1 = new(2026, 3, 5);
    private static readonly DateOnly D2 = new(2026, 3, 10);
    private static readonly DateOnly D3 = new(2026, 3, 20);
    private static readonly IReadOnlyDictionary<Guid, string> NoNames = new Dictionary<Guid, string>();

    private static LedgerRow Row(DateOnly date, decimal amount, LedgerSourceType type, Guid id, string description = "Mov") =>
        new(date, Guid.NewGuid(), "Income.Other", amount >= 0 ? LedgerDirection.In : LedgerDirection.Out, Math.Abs(amount), type, id, description, "Tercero", "Ref");

    private static ReconciliationMark Mark(LedgerSourceType type, Guid id, DateOnly date, decimal amount, Guid? reconciliation = null) =>
        new(Guid.NewGuid(), reconciliation ?? Guid.NewGuid(), (int)type, id, date, amount, "Mov", Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public void ElSaldoConciliadoEsElInicialMasLoMarcado_YLaDiferenciaEsElExtractoMenosEseSaldo()
    {
        var income = Guid.NewGuid();
        var expense = Guid.NewGuid();
        var inTransit = Guid.NewGuid();
        var movements = BankReconciliationCalculator.GroupRows(
        [
            Row(D1, 1_000_000m, LedgerSourceType.BuildingIncome, income),
            Row(D2, -300_000m, LedgerSourceType.BuildingExpense, expense),
            Row(D3, 200_000m, LedgerSourceType.BuildingIncome, inTransit)
        ], D3);

        var calc = BankReconciliationCalculator.Calculate(
            openingBalance: 500_000m, statementBalance: 1_200_000m, movements,
            [Mark(LedgerSourceType.BuildingIncome, income, D1, 1_000_000m), Mark(LedgerSourceType.BuildingExpense, expense, D2, -300_000m)], null, NoNames);

        Assert.Equal(1_200_000m, calc.ReconciledBalance);                     // 500.000 + 1.000.000 - 300.000
        Assert.Equal(0m, calc.Difference);
        Assert.Equal(200_000m, calc.PendingIn);                               // el deposito en transito
        Assert.Equal(0m, calc.PendingOut);
        Assert.Single(calc.Pending);
        Assert.Equal(2, calc.Marked.Count);
        Assert.Equal(0, calc.ChangedCount + calc.MissingCount);
    }

    [Fact]
    public void LaDiferenciaTieneSignoYMuestraLoQueElBancoTieneDeMasODeMenos()
    {
        var id = Guid.NewGuid();
        var movements = BankReconciliationCalculator.GroupRows([Row(D1, 100_000m, LedgerSourceType.BuildingIncome, id)], D3);
        var marks = new[] { Mark(LedgerSourceType.BuildingIncome, id, D1, 100_000m) };

        Assert.Equal(-5_000m, BankReconciliationCalculator.Calculate(0m, 95_000m, movements, marks, null, NoNames).Difference);    // el banco cobro 5.000 de comision
        Assert.Equal(7_000m, BankReconciliationCalculator.Calculate(0m, 107_000m, movements, marks, null, NoNames).Difference);    // el banco acredito 7.000 de intereses
    }

    [Fact]
    public void UnCobroPartidoEnVariosRenglonesDeLaMismaCuentaEsUnSoloMovimiento()
    {
        var payment = Guid.NewGuid();
        var movements = BankReconciliationCalculator.GroupRows(
        [
            Row(D1, 800_000m, LedgerSourceType.OwnerPayment, payment, "Expensas"),
            Row(D1, 200_000m, LedgerSourceType.OwnerPayment, payment, "Mora")
        ], D3);

        var movement = Assert.Single(movements);
        Assert.Equal(1_000_000m, movement.Amount);
        Assert.Equal(LedgerSourceType.OwnerPayment, movement.SourceType);
    }

    [Fact]
    public void LoPosteriorALaFechaDeCorteNoEntra()
    {
        var movements = BankReconciliationCalculator.GroupRows(
        [
            Row(D1, 100_000m, LedgerSourceType.BuildingIncome, Guid.NewGuid()),
            Row(D3, 900_000m, LedgerSourceType.BuildingIncome, Guid.NewGuid())
        ], D2);

        Assert.Equal(100_000m, Assert.Single(movements).Amount);
    }

    [Fact]
    public void UnMarcadoCuyoImporteCambioCuentaPorSuImporteActualYSeAvisa()
    {
        var id = Guid.NewGuid();
        var movements = BankReconciliationCalculator.GroupRows([Row(D1, 120_000m, LedgerSourceType.BuildingIncome, id)], D3);

        var calc = BankReconciliationCalculator.Calculate(0m, 100_000m, movements, [Mark(LedgerSourceType.BuildingIncome, id, D1, 100_000m)], null, NoNames);

        Assert.Equal(1, calc.ChangedCount);
        Assert.Equal(120_000m, calc.ReconciledBalance);
        Assert.Equal("Changed", calc.Marked.Single().State);
        Assert.Equal(100_000m, calc.Marked.Single().MarkedAmount);
        Assert.Equal(120_000m, calc.Marked.Single().Amount);
    }

    [Fact]
    public void UnMarcadoQueYaNoEstaEnElLibroNoCuentaYSeAvisa()
    {
        var gone = Guid.NewGuid();

        var calc = BankReconciliationCalculator.Calculate(
            0m, 0m, [], [Mark(LedgerSourceType.BuildingExpense, gone, D1, -50_000m)], null, NoNames);

        Assert.Equal(1, calc.MissingCount);
        Assert.Equal(0m, calc.ReconciledBalance);                             // no cuenta
        Assert.Equal("Missing", calc.Marked.Single().State);
        Assert.Equal(-50_000m, calc.Marked.Single().Amount);
        Assert.Equal(LedgerDirection.Out, calc.Marked.Single().Direction);
    }

    [Fact]
    public void LoMarcadoEnOtraConciliacionCuentaIgualPeroNoEsDeEstaYLoPendienteEstaOrdenado()
    {
        var thisId = Guid.NewGuid();
        var earlier = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var pendingA = Guid.NewGuid();
        var pendingB = Guid.NewGuid();
        var movements = BankReconciliationCalculator.GroupRows(
        [
            Row(D3, -10_000m, LedgerSourceType.BuildingExpense, pendingB, "Zeta"),
            Row(D1, 1_000m, LedgerSourceType.BuildingIncome, earlier),
            Row(D2, 2_000m, LedgerSourceType.BuildingIncome, mine),
            Row(D2, 5_000m, LedgerSourceType.BuildingIncome, pendingA, "Alfa")
        ], D3);

        var calc = BankReconciliationCalculator.Calculate(
            0m, 3_000m, movements,
            [Mark(LedgerSourceType.BuildingIncome, earlier, D1, 1_000m), Mark(LedgerSourceType.BuildingIncome, mine, D2, 2_000m, thisId)], thisId, NoNames);

        Assert.Equal(3_000m, calc.ReconciledBalance);
        Assert.Equal(0m, calc.Difference);
        Assert.False(calc.Marked.First(m => m.SourceId == earlier).InThisReconciliation);
        Assert.True(calc.Marked.First(m => m.SourceId == mine).InThisReconciliation);
        Assert.Equal([pendingA, pendingB], calc.Pending.Select(p => p.SourceId).ToArray());
        Assert.Equal(5_000m, calc.PendingIn);
        Assert.Equal(10_000m, calc.PendingOut);
    }

    [Fact]
    public void SinMovimientosNiMarcasLaDiferenciaEsElExtractoMenosElSaldoInicial()
    {
        var calc = BankReconciliationCalculator.Calculate(250_000m, 250_000m, [], [], null, NoNames);

        Assert.Equal(0m, calc.Difference);
        Assert.Equal(250_000m, calc.ReconciledBalance);
        Assert.Empty(calc.Marked);
        Assert.Empty(calc.Pending);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Validaciones antes de tocar el libro (SQLite alcanza)
    // ═════════════════════════════════════════════════════════════════════════

    private static BankReconciliationService Service(FinanceEnv env)
    {
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        return new BankReconciliationService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);
    }

    private static FinanceReconciliationController Api(FinanceEnv env)
    {
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        return new FinanceReconciliationController(
            env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), Service(env));
    }

    private static async Task<LedgerContext> Context(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static StartReconciliationRequest Start(FinanceEnv env, Guid accountId, DateOnly? date = null, decimal balance = 0m) =>
        new() { BuildingId = env.Building.Id, AccountId = accountId, StatementDate = date ?? FinanceEnv.Yesterday, StatementBalance = balance };

    [Fact]
    public async Task SoloSeConcilianCuentasBancariasDelEdificio()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        var cash = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Caja", Type = FinancialAccountType.Cash };
        var fund = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Fondo", Type = FinancialAccountType.ReserveFund };
        env.T.Db.FinancialAccounts.AddRange(cash, fund);
        env.T.Db.SaveChanges();
        var ctx = await Context(env);

        Assert.Equal("account_not_bank", (await Service(env).StartAsync(ctx, Start(env, cash.Id), env.Company.Id, default)).Code);
        Assert.Equal("account_not_bank", (await Service(env).StartAsync(ctx, Start(env, fund.Id), env.Company.Id, default)).Code);
        Assert.Equal("account_not_found", (await Service(env).StartAsync(ctx, Start(env, Guid.NewGuid()), env.Company.Id, default)).Code);
        _ = bank;
    }

    [Fact]
    public async Task LaFechaDeCorteYElSaldoDelExtractoSeValidan()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        var ctx = await Context(env);
        var service = Service(env);

        Assert.Contains("futura", (await service.StartAsync(ctx, Start(env, bank.Id, FinancePeriods.Today().AddDays(1)), env.Company.Id, default)).Message);
        Assert.Contains("arranque", (await service.StartAsync(ctx, Start(env, bank.Id, ctx.StartDate.AddDays(-1)), env.Company.Id, default)).Message);
        Assert.Contains("saldo", (await service.StartAsync(ctx, Start(env, bank.Id, balance: 10_000_000_000_000m), env.Company.Id, default)).Message);
        var notes = Start(env, bank.Id);
        notes.Notes = new string('x', 501);
        Assert.Contains("notas", (await service.StartAsync(ctx, notes, env.Company.Id, default)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(env.T.NewContext().BankReconciliations.ToList());
    }

    [Fact]
    public async Task UnaConciliacionAjenaOInexistenteResponde404YLosRolesSeRespetan()
    {
        using var env = new FinanceEnv();
        env.Setup();
        var api = Api(env);

        Assert.IsType<NotFoundResult>((await api.Get(Guid.NewGuid(), default)).Result);
        env.LoginAs("CompanyOperator");
        var denied = await api.Start(new StartReconciliationRequest { BuildingId = env.Building.Id, AccountId = Guid.NewGuid() }, default);
        Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>(denied.Result).StatusCode);
        Assert.Equal("reconciliation_forbidden", denied.Result!.GetType().GetProperty("Value")!.GetValue(denied.Result)!.GetType().GetProperty("error")!.GetValue(((ObjectResult)denied.Result).Value));
    }

    [Fact]
    public async Task ElResumenDelCentroMuestraLaConciliacionBancariaComoOpcional()
    {
        using var env = new FinanceEnv();
        env.Setup();
        env.Tenant.CompanyId = env.Company.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var config = new BuildingConfigController(env.T.Db, env.Access, env.Tenant, new BuildingConfigOverviewService(env.T.Db, new FinanceModuleGate(env.T.Db)));

        var section = FinanceEnv.Ok(await config.GetOverview(env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Reconciliation);

        Assert.Equal(ConfigSectionStatus.Optional, section.Status);
        Assert.False(section.Required);
        Assert.Equal("finance", section.LinkKind);
        Assert.Equal("reconciliation", section.LinkTab);
        Assert.Contains(section.Summary, i => i.Label == "Cuentas bancarias" && i.Value == "1");
        Assert.Contains("ninguna", section.Reasons.Single());

        env.T.Db.BankReconciliations.Add(new BankReconciliation
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, AccountId = env.T.Db.FinancialAccounts.Single(a => a.Type == FinancialAccountType.Bank).Id,
            StatementDate = FinanceEnv.Yesterday, StatementBalance = 0m, Status = BankReconciliationStatus.Completed, CreatedByUserId = env.Admin.Id
        });
        env.T.Db.SaveChanges();
        var done = FinanceEnv.Ok(await config.GetOverview(env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Reconciliation);
        Assert.Equal(ConfigSectionStatus.Complete, done.Status);
        Assert.Contains(done.Summary, i => i.Label == "Última conciliación cerrada" && i.Value == FinanceEnv.Yesterday.ToString("dd/MM/yyyy"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Flujo completo (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    private sealed record Fixture(FinancialAccount Bank, BuildingIncome Deposit, BuildingExpense Payment, BuildingIncome InTransit);

    // Un banco con tres movimientos: un deposito de 1.000.000, un pago de 300.000 y otro deposito de 200.000 (el "en transito").
    private static Fixture Seed(FinanceEnv env)
    {
        var bank = env.Setup();
        var deposit = env.AddIncome(null, 1_000_000m, FinanceEnv.Yesterday.AddDays(-12));
        var payment = env.AddExpense(null, 300_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-8));
        var inTransit = env.AddIncome(null, 200_000m, FinanceEnv.Yesterday.AddDays(-2));
        return new Fixture(bank, deposit, payment, inTransit);
    }

    private static ReconciliationItemRef Ref(LedgerSourceType type, Guid id) => new() { SourceType = type, SourceId = id };

    private static ReconciliationItemRef Ref(BuildingIncome income) => Ref(LedgerSourceType.BuildingIncome, income.Id);

    private static ReconciliationItemRef Ref(BuildingExpense expense) => Ref(LedgerSourceType.BuildingExpense, expense.Id);

    private static async Task<ReconciliationWorkspaceDto> StartOk(FinanceEnv env, Fixture f, DateOnly? date = null, decimal balance = 700_000m)
    {
        var result = await Service(env).StartAsync(await Context(env), Start(env, f.Bank.Id, date, balance), env.Company.Id, default);
        Assert.True(result.Ok, result.Message);
        return result.Value!;
    }

    private static async Task<ReconciliationWorkspaceDto> MarkOk(FinanceEnv env, Guid id, params ReconciliationItemRef[] items)
    {
        var result = await Service(env).MarkAsync(await Context(env), id, items, default);
        Assert.True(result.Ok, result.Message);
        return result.Value!;
    }

    [SqlServerFact]
    public async Task ElCaminoFelizSeConcilianLosMovimientosSeCierraYLaSiguienteContinuaDeAhi()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);

        var started = await StartOk(env, f, balance: 700_000m);
        Assert.Equal(BankReconciliationStatus.Open, started.Status);
        Assert.Equal(900_000m, started.BookBalance);                        // 1.000.000 - 300.000 + 200.000
        Assert.Equal(0m, started.ReconciledBalance);
        Assert.Equal(700_000m, started.Difference);
        Assert.Equal(3, started.PendingCount);

        var marked = await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));
        Assert.Equal(700_000m, marked.ReconciledBalance);
        Assert.Equal(0m, marked.Difference);
        Assert.True(marked.IsBalanced);
        Assert.Equal(200_000m, marked.PendingIn);                           // el deposito en transito
        Assert.Equal(2, marked.Marked.Count);
        Assert.All(marked.Marked, m => Assert.True(m.InThisReconciliation));

        var completed = await Service(env).CompleteAsync(await Context(env), started.Id, default);
        Assert.True(completed.Ok, completed.Message);
        Assert.Equal(BankReconciliationStatus.Completed, completed.Value!.Status);
        Assert.False(completed.Value.CanEdit);

        // La siguiente conciliacion (a hoy) arranca desde lo ya conciliado: solo falta marcar el deposito en transito.
        var next = await StartOk(env, f, FinancePeriods.Today(), 900_000m);
        Assert.Equal(700_000m, next.ReconciledBalance);
        Assert.Equal(200_000m, next.Difference);
        Assert.Single(next.Pending);
        var nextMarked = await MarkOk(env, next.Id, Ref(f.InTransit));
        Assert.Equal(0m, nextMarked.Difference);
        Assert.True((await Service(env).CompleteAsync(await Context(env), next.Id, default)).Ok);
        Assert.Equal(2, env.T.NewContext().BankReconciliations.Count(x => x.Status == BankReconciliationStatus.Completed));
    }

    [SqlServerFact]
    public async Task NoSeCierraConDiferenciaYElMensajeDiceCuantoEs()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 650_000m);                         // el banco tiene 50.000 menos (una comision)
        await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));

        var result = await Service(env).CompleteAsync(await Context(env), started.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(409, result.Status);
        Assert.Equal("reconciliation_difference", result.Code);
        Assert.Contains("-50.000", result.Message!.Replace(",", "."));
        Assert.Equal(BankReconciliationStatus.Open, env.T.NewContext().BankReconciliations.Single().Status);
    }

    [SqlServerFact]
    public async Task UnMovimientoNoSeConciliaDosVecesNiSeMarcaLoQueNoEsDeLaCuentaONoExisteONoEstaAFecha()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, FinanceEnv.Yesterday.AddDays(-5), 1_000_000m);     // el corte deja afuera el pago y el deposito en transito
        await MarkOk(env, started.Id, Ref(f.Deposit));
        var ctx = await Context(env);
        var service = Service(env);

        var twice = await service.MarkAsync(ctx, started.Id, [Ref(f.Deposit)], default);
        Assert.Equal(409, twice.Status);
        Assert.Equal("movement_already_reconciled", twice.Code);

        Assert.Equal("movement_not_found", (await service.MarkAsync(ctx, started.Id, [Ref(f.InTransit)], default)).Code);                  // posterior al corte
        Assert.Equal("movement_not_found", (await service.MarkAsync(ctx, started.Id, [Ref(LedgerSourceType.BuildingIncome, Guid.NewGuid())], default)).Code);
        Assert.Equal("invalid_items", (await service.MarkAsync(ctx, started.Id, [], default)).Code);
        Assert.Equal("invalid_items", (await service.MarkAsync(ctx, started.Id, [Ref(f.Payment), Ref(f.Payment)], default)).Code);
        Assert.Single(env.T.NewContext().BankReconciledMovements.Where(x => !x.IsDeleted));
    }

    [SqlServerFact]
    public async Task UnMovimientoConciliadoEnOtraConciliacionNoSeMarcaDeNuevoNiSeDesmarcaDesdeOtra()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var first = await StartOk(env, f, FinanceEnv.Yesterday.AddDays(-5), 1_000_000m);
        await MarkOk(env, first.Id, Ref(f.Deposit));
        Assert.True((await Service(env).CompleteAsync(await Context(env), first.Id, default)).Ok);
        var second = await StartOk(env, f, FinanceEnv.Yesterday, 700_000m);
        var ctx = await Context(env);

        Assert.Equal("movement_already_reconciled", (await Service(env).MarkAsync(ctx, second.Id, [Ref(f.Deposit)], default)).Code);
        var unmark = await Service(env).UnmarkAsync(ctx, second.Id, [Ref(f.Deposit)], default);
        Assert.Equal("mark_in_other_reconciliation", unmark.Code);
        Assert.Equal(409, unmark.Status);

        var open = await Service(env).GetAsync(ctx, second.Id, true, default);
        Assert.False(open.Value!.Marked.Single().InThisReconciliation);        // el deposito figura como conciliado, pero no de esta
        Assert.Equal(1_000_000m, open.Value.ReconciledBalance);
    }

    [SqlServerFact]
    public async Task SeDesmarcaMientrasEstaAbiertaYQuedaQuienYCuando()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 700_000m);
        await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));

        var result = await Service(env).UnmarkAsync(await Context(env), started.Id, [Ref(f.Payment)], default);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1_000_000m, result.Value!.ReconciledBalance);
        Assert.Single(result.Value.Marked);
        var undone = env.T.NewContext().BankReconciledMovements.IgnoreQueryFilters().Single(x => x.SourceId == f.Payment.Id);
        Assert.True(undone.IsDeleted);
        Assert.NotNull(undone.UnmarkedAtUtc);
        Assert.Equal(env.Admin.Id, undone.UnmarkedByUserId);
        Assert.Equal(env.Admin.Id, undone.MarkedByUserId);

        // Se puede volver a marcar (la marca anterior quedo deshecha).
        Assert.True((await Service(env).MarkAsync(await Context(env), started.Id, [Ref(f.Payment)], default)).Ok);
        Assert.Equal(2, env.T.NewContext().BankReconciledMovements.IgnoreQueryFilters().Count(x => x.SourceId == f.Payment.Id));
    }

    [SqlServerFact]
    public async Task LoConciliadoDeUnMesCerradoSePuedeMarcarPeroNoDesmarcar()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 700_000m);

        // Se cierra el mes del deposito y del pago: marcar sigue permitido.
        var month = new DateOnly(f.Deposit.IncomeDate.Year, f.Deposit.IncomeDate.Month, 1);
        env.T.Db.FinanceSettings.Single().PeriodClosingEnabled = true;
        env.T.Db.FinancePeriodClosures.Add(new FinancePeriodClosure { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Year = month.Year, Month = month.Month, ClosedByUserId = env.Admin.Id });
        env.T.Db.SaveChanges();
        await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));

        var unmark = await Service(env).UnmarkAsync(await Context(env), started.Id, [Ref(f.Deposit)], default);

        Assert.Equal(409, unmark.Status);
        Assert.Equal(FinancePeriodGuard.ClosedCode, unmark.Code);
        var discard = await Service(env).DiscardAsync(await Context(env), started.Id, default);
        Assert.Equal(FinancePeriodGuard.ClosedCode, discard.Code);                      // descartar tambien desmarca
        Assert.Equal(2, env.T.NewContext().BankReconciledMovements.Count(x => !x.IsDeleted));
    }

    [SqlServerFact]
    public async Task DescartarUnaConciliacionAbiertaDeshaceSusMarcasYLaCuentaPuedeEmpezarOtra()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 700_000m);
        await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));

        var discarded = await Service(env).DiscardAsync(await Context(env), started.Id, default);

        Assert.True(discarded.Ok);
        Assert.Empty(env.T.NewContext().BankReconciliations.Where(x => !x.IsDeleted).ToList());
        Assert.Empty(env.T.NewContext().BankReconciledMovements.Where(x => !x.IsDeleted).ToList());
        Assert.Equal("reconciliation_not_found", (await Service(env).GetAsync(await Context(env), started.Id, true, default)).Code);
        await StartOk(env, f, balance: 700_000m);
        Assert.Equal(2, env.T.NewContext().BankReconciledMovements.Count(x => x.IsDeleted));      // las marcas viejas quedan como historial
    }

    [SqlServerFact]
    public async Task HayUnaSolaConciliacionAbiertaPorCuentaYLaFechaTieneQueAvanzar()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 700_000m);
        var ctx = await Context(env);

        var second = await Service(env).StartAsync(ctx, Start(env, f.Bank.Id, FinanceEnv.Yesterday.AddDays(-3)), env.Company.Id, default);
        Assert.Equal("reconciliation_open_exists", second.Code);
        Assert.Equal(409, second.Status);

        await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));
        Assert.True((await Service(env).CompleteAsync(ctx, started.Id, default)).Ok);

        var older = await Service(env).StartAsync(ctx, Start(env, f.Bank.Id, FinanceEnv.Yesterday), env.Company.Id, default);          // misma fecha que la cerrada
        Assert.Equal("statement_date_before_last", older.Code);
        Assert.True((await Service(env).StartAsync(ctx, Start(env, f.Bank.Id, FinancePeriods.Today()), env.Company.Id, default)).Ok);
    }

    [SqlServerFact]
    public async Task SeEditaElExtractoMientrasEstaAbiertaPeroNoSePuedeCorrerElCorteAntesDeUnMarcado()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 700_000m);
        await MarkOk(env, started.Id, Ref(f.Payment));
        var ctx = await Context(env);

        var updated = await Service(env).UpdateAsync(ctx, started.Id,
            new UpdateReconciliationRequest { StatementDate = FinanceEnv.Yesterday, StatementBalance = 123_000m, Notes = "Extracto de marzo" }, default);
        Assert.True(updated.Ok, updated.Message);
        Assert.Equal(123_000m, updated.Value!.StatementBalance);
        Assert.Equal("Extracto de marzo", updated.Value.Notes);

        var tooEarly = await Service(env).UpdateAsync(ctx, started.Id,
            new UpdateReconciliationRequest { StatementDate = f.Payment.ExpenseDate.AddDays(-1), StatementBalance = 0m }, default);
        Assert.Equal("statement_date_before_mark", tooEarly.Code);
        Assert.Single(env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Reconciliation && x.Action == "Updated").ToList());
    }

    [SqlServerFact]
    public async Task UnaConciliacionCerradaQuedaFirmeYSoloSeReabreConMotivoLaUltimaDeSuCuenta()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var first = await StartOk(env, f, FinanceEnv.Yesterday.AddDays(-5), 1_000_000m);
        await MarkOk(env, first.Id, Ref(f.Deposit));
        Assert.True((await Service(env).CompleteAsync(await Context(env), first.Id, default)).Ok);
        var ctx = await Context(env);
        var service = Service(env);

        // Cerrada: no se edita ni se marca.
        Assert.Equal("reconciliation_not_open", (await service.MarkAsync(ctx, first.Id, [Ref(f.Payment)], default)).Code);
        Assert.Equal("reconciliation_not_open", (await service.UpdateAsync(ctx, first.Id, new UpdateReconciliationRequest { StatementDate = FinanceEnv.Yesterday }, default)).Code);
        Assert.Equal("reconciliation_not_open", (await service.DiscardAsync(ctx, first.Id, default)).Code);

        Assert.Equal("reason_required", (await service.ReopenAsync(ctx, first.Id, "  ", default)).Code);
        Assert.Equal("reason_too_long", (await service.ReopenAsync(ctx, first.Id, new string('x', 501), default)).Code);

        var second = await StartOk(env, f, FinanceEnv.Yesterday, 700_000m);                         // otra abierta: no se puede reabrir la anterior
        Assert.Equal("reconciliation_open_exists", (await service.ReopenAsync(ctx, first.Id, "Error", default)).Code);
        Assert.True((await service.DiscardAsync(ctx, second.Id, default)).Ok);

        var reopened = await service.ReopenAsync(ctx, first.Id, "Faltó un movimiento", default);
        Assert.True(reopened.Ok, reopened.Message);
        Assert.Equal(BankReconciliationStatus.Open, reopened.Value!.Status);
        Assert.True(reopened.Value.CanEdit);
        var saved = env.T.NewContext().BankReconciliations.Single(x => x.Id == first.Id);
        Assert.Equal("Faltó un movimiento", saved.ReopenReason);
        Assert.Equal(env.Admin.Id, saved.ReopenedByUserId);
        Assert.Null(saved.CompletedAtUtc);

        // Ahora se puede desmarcar y marcar de nuevo.
        Assert.True((await service.UnmarkAsync(ctx, first.Id, [Ref(f.Deposit)], default)).Ok);
    }

    [SqlServerFact]
    public async Task SoloSeReabreLaUltimaCerradaDeLaCuenta()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var first = await StartOk(env, f, FinanceEnv.Yesterday.AddDays(-5), 1_000_000m);
        await MarkOk(env, first.Id, Ref(f.Deposit));
        Assert.True((await Service(env).CompleteAsync(await Context(env), first.Id, default)).Ok);
        var second = await StartOk(env, f, FinanceEnv.Yesterday, 700_000m);
        await MarkOk(env, second.Id, Ref(f.Payment));
        Assert.True((await Service(env).CompleteAsync(await Context(env), second.Id, default)).Ok);

        var result = await Service(env).ReopenAsync(await Context(env), first.Id, "Error", default);

        Assert.Equal("not_last_reconciliation", result.Code);
        Assert.Equal(409, result.Status);
        Assert.True((await Service(env).ReopenAsync(await Context(env), second.Id, "Error", default)).Ok);
    }

    [SqlServerFact]
    public async Task UnMovimientoQueCambioDespuesDeMarcarseSeAvisaYUnoQueDesaparecioImpideCerrar()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 700_000m);
        await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));

        // El deposito sube de importe y el pago se elimina.
        env.T.Db.BuildingIncomes.Single(x => x.Id == f.Deposit.Id).Amount = 1_100_000m;
        env.T.Db.BuildingExpenses.Single(x => x.Id == f.Payment.Id).IsDeleted = true;
        env.T.Db.SaveChanges();
        var ctx = await Context(env);

        var view = (await Service(env).GetAsync(ctx, started.Id, true, default)).Value!;
        Assert.Equal(1, view.ChangedCount);
        Assert.Equal(1, view.MissingCount);
        Assert.Equal(2, view.Alerts.Count);
        Assert.Equal(1_100_000m, view.ReconciledBalance);                               // el cambiado cuenta por su importe actual; el que falta no cuenta
        Assert.False(view.IsBalanced);
        Assert.Equal("Changed", view.Marked.Single(m => m.SourceId == f.Deposit.Id).State);
        Assert.Equal("Missing", view.Marked.Single(m => m.SourceId == f.Payment.Id).State);

        var blocked = await Service(env).CompleteAsync(ctx, started.Id, default);
        Assert.Equal("reconciliation_missing_movements", blocked.Code);

        // Se desmarca el que falta y el extracto se ajusta al importe nuevo: ahora cierra.
        Assert.True((await Service(env).UnmarkAsync(ctx, started.Id, [Ref(f.Payment)], default)).Ok);
        Assert.True((await Service(env).UpdateAsync(ctx, started.Id, new UpdateReconciliationRequest { StatementDate = FinanceEnv.Yesterday, StatementBalance = 1_100_000m }, default)).Ok);
        var done = await Service(env).CompleteAsync(ctx, started.Id, default);
        Assert.True(done.Ok, done.Message);
        Assert.Equal(1_100_000m, env.T.NewContext().BankReconciledMovements.Single(x => x.SourceId == f.Deposit.Id && !x.IsDeleted).Amount);     // la foto se actualiza al cerrar
    }

    [SqlServerFact]
    public async Task CadaCuentaBancariaSeConciliaPorSuLado()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var second = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Banco dos", Type = FinancialAccountType.Bank, OpeningBalance = 100_000m };
        env.T.Db.FinancialAccounts.Add(second);
        env.T.Db.SaveChanges();
        env.T.Db.FinanceSettings.Single().DefaultAccountId = f.Bank.Id;
        var paidFromSecond = env.AddExpense(null, 40_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-3));
        env.T.Db.BuildingExpenses.Single(x => x.Id == paidFromSecond.Id).PaidFromAccountId = second.Id;
        env.T.Db.SaveChanges();

        var one = await Service(env).StartAsync(await Context(env), Start(env, f.Bank.Id, balance: 0m), env.Company.Id, default);
        var two = await Service(env).StartAsync(await Context(env), Start(env, second.Id, balance: 60_000m), env.Company.Id, default);

        Assert.True(one.Ok && two.Ok);
        Assert.Equal(3, one.Value!.PendingCount);                                       // el pago de "Banco dos" no aparece aca
        Assert.Equal(1, two.Value!.PendingCount);
        Assert.Equal(100_000m, two.Value.OpeningBalance);
        var marked = await MarkOk(env, two.Value.Id, Ref(paidFromSecond));
        Assert.Equal(60_000m, marked.ReconciledBalance);                                // 100.000 - 40.000
        Assert.Equal(0m, marked.Difference);

        // Un movimiento de una cuenta no se puede marcar en la conciliacion de la otra.
        Assert.Equal("movement_not_found", (await Service(env).MarkAsync(await Context(env), one.Value.Id, [Ref(paidFromSecond)], default)).Code);
    }

    [SqlServerFact]
    public async Task ElResumenListaLasCuentasBancariasConSuUltimaConciliacionYElHistorialLasCerradas()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var first = await StartOk(env, f, FinanceEnv.Yesterday.AddDays(-5), 1_000_000m);
        await MarkOk(env, first.Id, Ref(f.Deposit));
        Assert.True((await Service(env).CompleteAsync(await Context(env), first.Id, default)).Ok);
        var open = await StartOk(env, f, FinanceEnv.Yesterday, 700_000m);

        var overview = await Service(env).OverviewAsync(await Context(env), canEdit: true, default);
        var account = Assert.Single(overview.Accounts);
        Assert.Equal(f.Bank.Id, account.AccountId);
        Assert.Equal(900_000m, account.BookBalance);
        Assert.Equal(FinanceEnv.Yesterday.AddDays(-5), account.LastCompletedDate);
        Assert.Equal(1_000_000m, account.LastStatementBalance);
        Assert.Equal(open.Id, account.OpenReconciliationId);
        Assert.Equal(FinanceEnv.Yesterday, account.OpenStatementDate);

        var history = await Service(env).HistoryAsync(env.Building.Id, f.Bank.Id, default);
        var row = Assert.Single(history);
        Assert.Equal(first.Id, row.Id);
        Assert.Equal(1, row.MarkedCount);
        Assert.Equal(env.Admin.FullName, row.CompletedByName);
        Assert.Equal(1_000_000m, row.ReconciledBalanceAtCompletion);
    }

    [SqlServerFact]
    public async Task TodoQuedaAuditadoEnElHistorialDelCentro()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var started = await StartOk(env, f, balance: 700_000m);
        await MarkOk(env, started.Id, Ref(f.Deposit), Ref(f.Payment));
        await Service(env).UnmarkAsync(await Context(env), started.Id, [Ref(f.Payment)], default);
        await MarkOk(env, started.Id, Ref(f.Payment));
        await Service(env).CompleteAsync(await Context(env), started.Id, default);
        await Service(env).ReopenAsync(await Context(env), started.Id, "Revisar", default);
        await Service(env).DiscardAsync(await Context(env), started.Id, default);

        var actions = env.T.NewContext().FinanceAuditLogs
            .Where(x => x.Section == ConfigSectionKeys.Reconciliation).OrderBy(x => x.CreatedAtUtc).Select(x => x.Action).ToList();

        Assert.Equal(["Started", "Marked", "Unmarked", "Marked", "Completed", "Reopened", "Discarded"], actions);
        Assert.All(env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Reconciliation).ToList(),
            x => Assert.Equal(env.Admin.Id, x.UserId));
    }

    [SqlServerFact]
    public async Task LosCuatroRolesVenYSoloAdministradorYSuperAdminArman_YUnEdificioAjenoNoSeVe()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var api = Api(env);
        var started = await StartOk(env, f, balance: 700_000m);

        foreach (var role in new[] { "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            var overview = FinanceEnv.Ok(await api.GetOverview(env.Building.Id, default));
            Assert.Equal(role == "CompanyAdmin", overview.CanEdit);
            Assert.Equal(started.Id, FinanceEnv.Ok(await api.Get(started.Id, default)).Id);
            Assert.Equal(role == "CompanyAdmin", FinanceEnv.Ok(await api.Get(started.Id, default)).CanEdit);
            Assert.NotNull(FinanceEnv.Ok(await api.GetHistory(env.Building.Id, null, default)));
        }

        var items = new ReconciliationItemsRequest { Items = [Ref(f.Deposit)] };
        foreach (var role in new[] { "CompanyOperator", "BuildingManager", "Owner" })
        {
            env.LoginAs(role);
            Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>((await api.Mark(started.Id, items, default)).Result).StatusCode);
            Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>((await api.Complete(started.Id, default)).Result).StatusCode);
            Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>(await api.Discard(started.Id, default)).StatusCode);
        }

        env.LoginAs("CompanyAdmin");
        Assert.Equal(2, FinanceEnv.Ok(await api.Mark(started.Id, items, default)).Marked.Count + 1);
        env.Access.Buildings.Clear();
        Assert.IsType<NotFoundResult>((await api.Get(started.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await api.Mark(started.Id, items, default)).Result);
    }

    [SqlServerFact]
    public async Task ElControladorTraduceLosErroresConSuCodigoYSuEstado()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var api = Api(env);

        var created = await api.Start(Start(env, f.Bank.Id, balance: 700_000m), default);
        var workspace = (ReconciliationWorkspaceDto)Assert.IsAssignableFrom<ObjectResult>(created.Result).Value!;
        Assert.Equal(201, Assert.IsAssignableFrom<ObjectResult>(created.Result).StatusCode);

        var again = await api.Start(Start(env, f.Bank.Id, balance: 700_000m), default);
        var error = Assert.IsAssignableFrom<ObjectResult>(again.Result);
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("reconciliation_open_exists", error.Value!.GetType().GetProperty("error")!.GetValue(error.Value));

        var difference = await api.Complete(workspace.Id, default);
        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(difference.Result).StatusCode);

        Assert.IsType<NoContentResult>(await api.Discard(workspace.Id, default));
        Assert.IsType<NotFoundResult>((await api.Get(workspace.Id, default)).Result);
    }
}
