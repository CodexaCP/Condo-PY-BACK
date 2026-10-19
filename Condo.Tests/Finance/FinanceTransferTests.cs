using System.Text.Json;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Marketplace;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Operaciones guiadas de Finanzas: transferencias entre cuentas propias. La prueba central es que una transferencia NO es ingreso ni gasto:
/// mueve el saldo de cada cuenta (y la conciliacion, el fondo de reserva y el Libro Diario) pero no cambia ningun total operativo (flujo, tablero,
/// presupuesto, informe de la asamblea). Las reglas puras se prueban sin base; el resto con SQL Server real (el libro de Finanzas suma decimales en la base) y
/// (triggers, transaccion serializable, asentado de punta a punta), se omite solo sin CONDO_TEST_SQLSERVER.
/// </summary>
public class FinanceTransferTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);
    private static readonly Guid BuildingId = Guid.NewGuid();

    // ═════════════════════════════════════════════════════════════════════════
    // Reglas puras
    // ═════════════════════════════════════════════════════════════════════════

    private static FinancialAccount Account(FinancialAccountType type, string name, bool active = true, Guid? building = null) =>
        new() { Id = Guid.NewGuid(), BuildingId = building ?? BuildingId, Name = name, Type = type, IsActive = active };

    [Fact]
    public void LaOperacionSeNombraSegunLasCuentasQueParticipan()
    {
        Assert.Equal("Depósito de efectivo", FinanceTransferRules.KindLabel(FinancialAccountType.Cash, FinancialAccountType.Bank));
        Assert.Equal("Retiro de efectivo", FinanceTransferRules.KindLabel(FinancialAccountType.Bank, FinancialAccountType.Cash));
        Assert.Equal("Aporte al fondo de reserva", FinanceTransferRules.KindLabel(FinancialAccountType.Bank, FinancialAccountType.ReserveFund));
        Assert.Equal("Retiro del fondo de reserva", FinanceTransferRules.KindLabel(FinancialAccountType.ReserveFund, FinancialAccountType.Bank));
        Assert.Equal("Transferencia entre cuentas", FinanceTransferRules.KindLabel(FinancialAccountType.Bank, FinancialAccountType.Bank));
    }

    [Fact]
    public void LosDatosInvalidosSeRechazan()
    {
        var bank = Account(FinancialAccountType.Bank, "Itaú");
        var cash = Account(FinancialAccountType.Cash, "Caja");
        var fund = Account(FinancialAccountType.ReserveFund, "Fondo");
        var start = new DateOnly(2026, 8, 1);
        var today = new DateOnly(2026, 10, 15);

        string? Check(FinancialAccount? from, FinancialAccount? to, decimal amount = 1000m, DateOnly? date = null, string support = "") =>
            FinanceTransferRules.Validate(from, to, amount, date ?? Day, start, today, "", "", support);

        Assert.Null(Check(cash, bank));
        Assert.NotNull(Check(null, bank));
        Assert.NotNull(Check(bank, null));
        Assert.Contains("distintas", Check(bank, bank));
        Assert.Contains("mayor que cero", Check(cash, bank, 0m));
        Assert.Contains("mayor que cero", Check(cash, bank, -5m));
        Assert.Contains("decimales", Check(cash, bank, 10.555m));
        Assert.Contains("futura", Check(cash, bank, 1000m, today.AddDays(1)));
        Assert.Contains("anterior al arranque", Check(cash, bank, 1000m, start.AddDays(-1)));
        Assert.Contains("activas", Check(Account(FinancialAccountType.Cash, "Vieja", active: false), bank));
        Assert.Contains("mismo edificio", Check(cash, Account(FinancialAccountType.Bank, "Ajeno", building: Guid.NewGuid())));
    }

    [Fact]
    public void ElFondoDeReservaExigeRespaldoEnAmbosSentidos()
    {
        var bank = Account(FinancialAccountType.Bank, "Itaú");
        var fund = Account(FinancialAccountType.ReserveFund, "Fondo");
        var start = new DateOnly(2026, 8, 1);
        var today = new DateOnly(2026, 10, 15);

        Assert.Contains("respaldo", FinanceTransferRules.Validate(bank, fund, 1000m, Day, start, today, "", "", ""));
        Assert.Contains("respaldo", FinanceTransferRules.Validate(fund, bank, 1000m, Day, start, today, "", "", "   "));
        Assert.Null(FinanceTransferRules.Validate(bank, fund, 1000m, Day, start, today, "", "", "Acta 12/2026"));
        Assert.Null(FinanceTransferRules.Validate(fund, bank, 1000m, Day, start, today, "", "", "Acta 12/2026"));
    }

    // ── Asiento (puro) ───────────────────────────────────────────────────────

    private static readonly Guid BankId = Guid.NewGuid();
    private static readonly Guid CashId = Guid.NewGuid();
    private static readonly AccountingPlanAccount BankPlan = new(Guid.NewGuid(), "1.1.03", null, "Banco cuenta corriente", LedgerCategoryType.Asset);
    private static readonly AccountingPlanAccount CashPlan = new(Guid.NewGuid(), "1.1.01", null, "Caja administración", LedgerCategoryType.Asset);

    private static AccountingBuildInput Input(IReadOnlyDictionary<Guid, AccountingPlanAccount>? plans = null) => new()
    {
        FinancialPlan = plans ?? new Dictionary<Guid, AccountingPlanAccount> { [BankId] = BankPlan, [CashId] = CashPlan },
        FinancialNames = new Dictionary<Guid, string> { [BankId] = "Banco Itaú", [CashId] = "Caja" },
        RubroPlan = _ => throw new InvalidOperationException("Una transferencia no tiene rubro."),
        VatRates = new Dictionary<(LedgerSourceType Type, Guid Id), decimal>()
    };

    private static List<LedgerRow> TransferRows(Guid id, decimal amount, Guid from, Guid to) =>
    [
        new LedgerRow(Day, from, FinanceChartTemplate.TransferKey, LedgerDirection.Out, amount, LedgerSourceType.Transfer, id, "Depósito de efectivo: Caja → Banco", "", "B-1"),
        new LedgerRow(Day, to, FinanceChartTemplate.TransferKey, LedgerDirection.In, amount, LedgerSourceType.Transfer, id, "Depósito de efectivo: Caja → Banco", "", "B-1")
    ];

    [Fact]
    public void UnaTransferenciaEsUnSoloAsientoQueDebitaElDestinoYAcreditaElOrigen()
    {
        var id = Guid.NewGuid();

        var result = AccountingEntryBuilder.Build(TransferRows(id, 150_000m, CashId, BankId), Input());

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.IsComplete);
        Assert.Equal(LedgerSourceType.Transfer, entry.SourceType);
        Assert.Equal(id, entry.SourceId);
        Assert.Equal(CashId, entry.SourceAccountId);                                  // se identifica por la cuenta de la que sale
        Assert.Equal(150_000m, entry.Debit);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.Equal(150_000m, entry.Lines.Single(l => l.Code == "1.1.03").Debit);    // entra al banco
        Assert.Equal(150_000m, entry.Lines.Single(l => l.Code == "1.1.01").Credit);   // sale de la caja
        Assert.All(entry.Lines, l => Assert.Equal("Financial", l.Kind));              // sin rubro ni IVA
        Assert.Equal("B-1", entry.Reference);
        Assert.Equal(1, entry.Description.Split(" · ").Length);                        // una sola glosa, no dos
    }

    [Fact]
    public void SiFaltaLaCuentaDelPlanDeUnaDeLasDosElAsientoQuedaIncompleto()
    {
        var plans = new Dictionary<Guid, AccountingPlanAccount> { [BankId] = BankPlan };

        var entry = Assert.Single(AccountingEntryBuilder.Build(TransferRows(Guid.NewGuid(), 10_000m, CashId, BankId), Input(plans)).Entries);

        Assert.False(entry.IsComplete);
        Assert.Contains(entry.Lines, l => l.Missing && l.Name.Contains("Caja"));
        Assert.Equal(entry.Debit, entry.Credit);
    }

    [Fact]
    public void DosCuentasConLaMismaCuentaDelPlanNoGeneranAsiento()
    {
        var plans = new Dictionary<Guid, AccountingPlanAccount> { [BankId] = BankPlan, [CashId] = BankPlan };

        Assert.Empty(AccountingEntryBuilder.Build(TransferRows(Guid.NewGuid(), 10_000m, CashId, BankId), Input(plans)).Entries);
    }

    [Fact]
    public void ElFlujoYLosRubrosIgnoranLasTransferenciasAunqueLleguenEnLaLista()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var buckets = new List<LedgerBucket>
        {
            new(2026, 10, a, FinanceChartTemplate.TransferKey, LedgerDirection.Out, 500_000m),
            new(2026, 10, b, FinanceChartTemplate.TransferKey, LedgerDirection.In, 500_000m),
            new(2026, 10, a, "Collection.Ordinary", LedgerDirection.In, 200_000m),
            new(2026, 10, a, "Expense.Other", LedgerDirection.Out, 50_000m)
        };

        var flow = FinanceReportBuilder.Flow(buckets);

        Assert.Equal(200_000m, flow.In);
        Assert.Equal(50_000m, flow.Out);
        Assert.Equal(150_000m, flow.Net);
        Assert.True(buckets[0].IsTransfer);
        Assert.False(buckets[2].IsTransfer);
    }

    // ── Guardas de arquitectura ──────────────────────────────────────────────

    [Fact]
    public void ElLibroDevuelveSoloLoOperativoSalvoQueElConsumidorPidaLasTransferencias()
    {
        foreach (var name in new[] { nameof(FinanceLedgerService.GetBucketsAsync), nameof(FinanceLedgerService.GetRowsAsync) })
        {
            var parameter = typeof(FinanceLedgerService).GetMethod(name)!.GetParameters().Single(p => p.Name == "includeTransfers");
            Assert.True(parameter.HasDefaultValue);
            Assert.Equal(false, parameter.DefaultValue);                                // por defecto, ni ingresos ni egresos de mas
        }
    }

    // Solo estos archivos piden las transferencias al libro (los que hablan de saldos por cuenta). Si un reporte nuevo las necesita, hay que
    // agregarlo aca a proposito: asi nadie suma una transferencia como si fuera un ingreso o un egreso sin darse cuenta.
    private static readonly string[] MayIncludeTransfers =
    [
        "AccountingEntriesService.cs",     // el asiento de la transferencia
        "AssemblyReportService.cs",        // saldos por cuenta (el estado de resultados las filtra)
        "BankReconciliationService.cs",    // el deposito figura en el extracto del banco
        "FinanceLedgerController.cs",      // tablero: saldos por cuenta y fondo de reserva
        "FinanceReportService.cs",         // movimientos, fondo de reserva y saldos por cuenta
        "FinanceTransferService.cs"        // saldo de cada cuenta antes de operar
    ];

    [Fact]
    public void SoloLosConsumidoresDeSaldosPorCuentaPidenLasTransferencias()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Condo.Api"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var users = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "Condo.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("includeTransfers: true"))
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(MayIncludeTransfers.OrderBy(n => n).ToArray(), users);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Con la base
    // ═════════════════════════════════════════════════════════════════════════

    private sealed class Seeded(FinanceEnv env, FinancialAccount bank, FinancialAccount cash, FinancialAccount fund) : IDisposable
    {
        public FinanceEnv Env { get; } = env;
        public FinancialAccount Bank { get; } = bank;
        public FinancialAccount Cash { get; } = cash;
        public FinancialAccount Fund { get; } = fund;

        public void Dispose() => Env.Dispose();
    }

    // Banco 5.000.000, caja 200.000, fondo 3.000.000; un ingreso de 1.000.000 y un gasto de 110.000 (banco = 5.890.000, total 9.090.000).
    private static async Task<Seeded> Seed(bool sqlServer = false, bool assign = true)
    {
        var env = new FinanceEnv(sqlServer);
        var bank = env.Setup();
        bank.OpeningBalance = 5_000_000m;
        var cash = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Caja", Type = FinancialAccountType.Cash, OpeningBalance = 200_000m };
        var fund = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Fondo de reserva", Type = FinancialAccountType.ReserveFund, OpeningBalance = 3_000_000m };
        env.T.Db.FinancialAccounts.AddRange(cash, fund);
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = ExpensePeriodStatus.Published;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.AddIncome(null, 1_000_000m, FinanceEnv.Yesterday.AddDays(-10));
        var expense = env.AddExpense(null, 110_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-6));
        env.T.Db.BuildingExpenses.Single(x => x.Id == expense.Id).VatRate = 0m;
        env.T.Db.SaveChanges();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        if (assign)
        {
            var accounting = new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);
            var (_, error) = await accounting.UpdateRolesAsync(await Ctx(env), new UpdateAccountingRolesRequest
            {
                Accounts =
                [
                    new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id },
                    new AccountingRoleItemRequest { FinancialAccountId = cash.Id, LedgerCategoryId = env.Cat("1.1.01").Id },
                    new AccountingRoleItemRequest { FinancialAccountId = fund.Id, LedgerCategoryId = env.Cat("3.1.01").Id }
                ]
            }, env.Company.Id, default);
            Assert.Null(error);
        }

        return new Seeded(env, bank, cash, fund);
    }

    private static async Task<LedgerContext> Ctx(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static FinanceTransferService Service(FinanceEnv env, CondoDbContext? db = null) =>
        new(db ?? env.T.Db, new FinanceLedgerService(db ?? env.T.Db), env.Tenant);

    private static FinanceOperationsController Api(FinanceEnv env) =>
        new(env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), Service(env));

    private static CreateFinanceTransferRequest Req(FinancialAccount from, FinancialAccount to, decimal amount, DateOnly? date = null,
        string? support = null, bool confirm = false) =>
        new() { FromAccountId = from.Id, ToAccountId = to.Id, Amount = amount, TransferDate = date, Reference = "B-100", SupportReference = support, ConfirmNegativeBalance = confirm };

    private static async Task<FinanceTransferDto> Create(Seeded s, CreateFinanceTransferRequest request)
    {
        var result = await Service(s.Env).CreateAsync(await Ctx(s.Env), request, s.Env.Company.Id, default);
        Assert.True(result.Ok, result.Message);
        return result.Value!;
    }

    private static async Task<Dictionary<string, decimal>> Balances(Seeded s)
    {
        var list = await Service(s.Env).ListAsync(await Ctx(s.Env), true, default);
        return list.Accounts.ToDictionary(a => a.Name, a => a.Balance);
    }

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result.Result).StatusCode);

    [SqlServerFact]
    public async Task UnDepositoMueveLosSaldosDeCadaCuentaYElTotalNoCambia()
    {
        using var s = await Seed(sqlServer: true);
        var before = await Balances(s);
        Assert.Equal(5_890_000m, before["Banco"]);

        var created = await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday));

        Assert.Equal("Depósito de efectivo", created.Kind);
        Assert.Equal(FinanceTransferStatus.Active, created.Status);
        var after = await Balances(s);
        Assert.Equal(before["Banco"] + 150_000m, after["Banco"]);
        Assert.Equal(before["Caja"] - 150_000m, after["Caja"]);
        Assert.Equal(before["Fondo de reserva"], after["Fondo de reserva"]);
        Assert.Equal(before.Values.Sum(), after.Values.Sum());                       // el total del edificio no cambia

        var list = await Service(s.Env).ListAsync(await Ctx(s.Env), true, default);
        var item = Assert.Single(list.Items);
        Assert.Equal("Caja", item.FromAccountName);
        Assert.Equal("Banco", item.ToAccountName);
        Assert.Equal("B-100", item.Reference);
        Assert.Equal(s.Env.Admin.FullName, item.CreatedByName);
        Assert.Equal(before.Values.Sum(), list.TotalBalance);

        var audit = Assert.Single(s.Env.T.NewContext().FinanceAuditLogs.Where(x => x.EntityType == nameof(FinanceTransfer)).ToList());
        Assert.Equal(ConfigSectionKeys.Accounting, audit.Section);
        Assert.Contains("depósito de efectivo", audit.Summary);
    }

    // Todo lo operativo (lo que es ingreso y egreso del edificio) como texto: si una transferencia lo cambia, la prueba falla.
    private static async Task<string> Operating(Seeded s)
    {
        var env = s.Env;
        var ledger = new FinanceLedgerService(env.T.Db);
        var budgets = new FinanceBudgetService(env.T.Db, ledger);
        var reports = new FinanceReportService(env.T.Db, ledger, budgets);
        var payables = new FinancePayablesService(env.T.Db);
        var ctx = await Ctx(env);

        var dashboardApi = new FinanceLedgerController(
            env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), ledger, budgets, reports, payables);
        var dash = FinanceEnv.Ok(await dashboardApi.GetDashboard(env.Building.Id, FinanceEnv.Yesterday.Year, FinanceEnv.Yesterday.Month, default));
        var (cashFlow, cashError) = await reports.CashFlowAsync(ctx, null, default);
        Assert.Null(cashError);
        var (vsActual, vsError) = await reports.BudgetVsActualAsync(ctx, null, null, default);
        Assert.Null(vsError);
        var (assembly, asmError) = await new AssemblyReportService(env.T.Db, ledger, reports, payables)
            .BuildAsync(ctx, null, null, null, new DateTime(2026, 10, 19, 12, 0, 0), default);
        Assert.Null(asmError);
        var (movements, movError) = await reports.MovementsAsync(ctx, new FinanceReportService.MovementsQuery(ctx.StartDate, null, Unpaged: true), default);
        Assert.Null(movError);

        return JsonSerializer.Serialize(new
        {
            Dashboard = new { dash.MonthFlow, dash.MonthIn, dash.MonthOut, dash.FiscalYearToDate, dash.Series, dash.Budget, Total = dash.Balances.TotalBalance },
            CashFlow = new { cashFlow!.InLines, cashFlow.OutLines, cashFlow.TotalIn, cashFlow.TotalOut, cashFlow.Net, cashFlow.OpeningBalance, cashFlow.ClosingBalance },
            VsActual = new { vsActual!.IncomeLines, vsActual.ExpenseLines, vsActual.IncomeTotals, vsActual.ExpenseTotals },
            Assembly = new
            {
                assembly!.TotalIncome, assembly.TotalExpense, assembly.NetResult, assembly.IncomeLines, assembly.ExpenseLines,
                assembly.OpeningBalance, assembly.ClosingBalance
            },
            Movements = new { movements!.TotalIn, movements.TotalOut, movements.ClosingBalance }
        });
    }

    [SqlServerFact]
    public async Task UnaTransferenciaNoCambiaNingunTotalOperativo()
    {
        using var s = await Seed(sqlServer: true);
        var before = await Operating(s);

        await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday));
        await Create(s, Req(s.Bank, s.Fund, 500_000m, FinanceEnv.Yesterday.AddDays(-4), "Acta 12/2026"));
        await Create(s, Req(s.Fund, s.Bank, 100_000m, FinanceEnv.Yesterday.AddDays(-2), "Acta 13/2026"));

        Assert.Equal(before, await Operating(s));
    }

    [SqlServerFact]
    public async Task LaVistaDeUnaCuentaMuestraLasTransferenciasYElListadoCompletoNoLasSumaALosTotales()
    {
        using var s = await Seed(sqlServer: true);
        var ctx = await Ctx(s.Env);
        var reports = new FinanceReportService(s.Env.T.Db, new FinanceLedgerService(s.Env.T.Db), new FinanceBudgetService(s.Env.T.Db, new FinanceLedgerService(s.Env.T.Db)));
        var start = ctx.StartDate;
        var baseline = (await reports.MovementsAsync(ctx, new FinanceReportService.MovementsQuery(start, null, Unpaged: true), default)).Page!;
        await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday));

        var all = (await reports.MovementsAsync(ctx, new FinanceReportService.MovementsQuery(start, null, Unpaged: true), default)).Page!;
        Assert.Equal(baseline.TotalIn, all.TotalIn);
        Assert.Equal(baseline.TotalOut, all.TotalOut);
        Assert.Equal(2, all.Items.Count(m => m.SourceType == LedgerSourceType.Transfer));
        Assert.Equal(baseline.ClosingBalance, all.ClosingBalance);

        var bankView = (await reports.MovementsAsync(ctx, new FinanceReportService.MovementsQuery(start, null, AccountId: s.Bank.Id, Unpaged: true), default)).Page!;
        Assert.Single(bankView.Items, m => m.SourceType == LedgerSourceType.Transfer && m.Direction == LedgerDirection.In && m.Amount == 150_000m);
        Assert.Equal(baseline.TotalIn + 150_000m, bankView.TotalIn);   // en la cuenta si cuenta (mueve su saldo)
        Assert.Equal(5_890_000m + 150_000m, bankView.ClosingBalance);

        var incomeOnly = (await reports.MovementsAsync(ctx, new FinanceReportService.MovementsQuery(start, null, Direction: LedgerDirection.In, Unpaged: true), default)).Page!;
        Assert.DoesNotContain(incomeOnly.Items, m => m.SourceType == LedgerSourceType.Transfer);       // con un filtro de sentido no aparecen
    }

    [SqlServerFact]
    public async Task LaExportacionAExcelRotulaLaTransferenciaComoTal()
    {
        using var s = await Seed(sqlServer: true);
        await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday));
        var ctx = await Ctx(s.Env);
        var ledger = new FinanceLedgerService(s.Env.T.Db);
        var reports = new FinanceReportService(s.Env.T.Db, ledger, new FinanceBudgetService(s.Env.T.Db, ledger));
        var page = (await reports.MovementsAsync(ctx, new FinanceReportService.MovementsQuery(ctx.StartDate, null, Unpaged: true), default)).Page!;

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        Condo.Api.Documents.FinanceExcelExporter.AddMovements(workbook, ctx, page);

        var texts = workbook.Worksheets.SelectMany(w => w.CellsUsed()).Select(c => c.GetString()).ToList();
        Assert.True(texts.Count(t => t == "Transferencia entre cuentas") >= 2);        // sale de la caja y entra al banco (en el rubro y en el origen)
        Assert.Equal(1, texts.Count(t => t == "Ingreso del edificio"));                 // el ingreso de verdad
    }

    [SqlServerFact]
    public async Task ElFondoDeReservaYLaConciliacionVenLasTransferencias()
    {
        using var s = await Seed(sqlServer: true);
        await Create(s, Req(s.Bank, s.Fund, 500_000m, FinanceEnv.Yesterday, "Acta 12/2026"));
        var ctx = await Ctx(s.Env);
        var ledger = new FinanceLedgerService(s.Env.T.Db);
        var reports = new FinanceReportService(s.Env.T.Db, ledger, new FinanceBudgetService(s.Env.T.Db, ledger));

        var (fund, error) = await reports.ReserveFundAsync(ctx, null, null, default);
        Assert.Null(error);
        Assert.Equal(3_500_000m, fund!.Balance);
        Assert.Equal(500_000m, fund.Contributions);
        Assert.Contains(fund.Movements.Items, m => m.SourceType == LedgerSourceType.Transfer && m.Amount == 500_000m);

        var overview = await new BankReconciliationService(s.Env.T.Db, ledger, s.Env.Tenant).OverviewAsync(ctx, true, default);
        Assert.Equal(5_890_000m - 500_000m, overview.Accounts.Single(a => a.AccountId == s.Bank.Id).BookBalance);
    }

    // ── Reglas con la base ───────────────────────────────────────────────────

    [SqlServerFact]
    public async Task ElImporteLasCuentasYLaFechaSeValidan()
    {
        using var s = await Seed(sqlServer: true);
        var service = Service(s.Env);
        var ctx = await Ctx(s.Env);

        async Task<FinanceOpResult<FinanceTransferDto>> TryCreate(CreateFinanceTransferRequest r) => await service.CreateAsync(ctx, r, s.Env.Company.Id, default);

        Assert.Equal(400, (await TryCreate(Req(s.Bank, s.Bank, 1000m))).Status);
        Assert.Equal(400, (await TryCreate(Req(s.Cash, s.Bank, 0m))).Status);
        Assert.Equal(400, (await TryCreate(Req(s.Cash, s.Bank, 1000m, FinancePeriods.Today().AddDays(1)))).Status);
        Assert.Equal(400, (await TryCreate(Req(s.Cash, s.Bank, 1000m, ctx.StartDate.AddDays(-1)))).Status);
        Assert.Equal(400, (await TryCreate(Req(s.Cash, new FinancialAccount { Id = Guid.NewGuid() }, 1000m))).Status);       // cuenta ajena
        Assert.Empty(s.Env.T.NewContext().FinanceTransfers.ToList());
    }

    [SqlServerFact]
    public async Task ElFondoDeReservaExigeRespaldoYNoPuedeQuedarEnNegativo()
    {
        using var s = await Seed(sqlServer: true);
        var service = Service(s.Env);
        var ctx = await Ctx(s.Env);

        var noSupport = await service.CreateAsync(ctx, Req(s.Bank, s.Fund, 100_000m), s.Env.Company.Id, default);
        Assert.Equal(400, noSupport.Status);
        Assert.Contains("respaldo", noSupport.Message);

        var tooMuch = await service.CreateAsync(ctx, Req(s.Fund, s.Bank, 3_000_001m, support: "Acta 1", confirm: true), s.Env.Company.Id, default);
        Assert.Equal(409, tooMuch.Status);
        Assert.Equal(FinanceTransferService.FundNegativeCode, tooMuch.Code);          // confirmar no alcanza para el fondo

        Assert.Empty(s.Env.T.NewContext().FinanceTransfers.ToList());
        Assert.True((await service.CreateAsync(ctx, Req(s.Fund, s.Bank, 3_000_000m, support: "Acta 1"), s.Env.Company.Id, default)).Ok);   // justo el saldo: se puede
    }

    [SqlServerFact]
    public async Task BancoOCajaEnNegativoPidenConfirmacionYConfirmandoSeRegistra()
    {
        using var s = await Seed(sqlServer: true);
        var service = Service(s.Env);
        var ctx = await Ctx(s.Env);

        var preview = await service.PreviewAsync(ctx, Req(s.Cash, s.Bank, 300_000m), default);
        Assert.True(preview.Ok);
        Assert.True(preview.Value!.NeedsNegativeConfirmation);
        Assert.Contains(preview.Value.Warnings, w => w.Contains("negativo"));
        Assert.Equal(200_000m, preview.Value.FromBefore);
        Assert.Equal(-100_000m, preview.Value.FromAfter);

        var denied = await service.CreateAsync(ctx, Req(s.Cash, s.Bank, 300_000m), s.Env.Company.Id, default);
        Assert.Equal(409, denied.Status);
        Assert.Equal(FinanceTransferService.NegativeBalanceCode, denied.Code);
        Assert.Empty(s.Env.T.NewContext().FinanceTransfers.ToList());

        Assert.True((await service.CreateAsync(ctx, Req(s.Cash, s.Bank, 300_000m, confirm: true), s.Env.Company.Id, default)).Ok);
        Assert.Equal(-100_000m, (await Balances(s))["Caja"]);
    }

    [SqlServerFact]
    public async Task LaVistaPreviaMuestraSaldosCuentasDelPlanYNoGuardaNada()
    {
        using var s = await Seed(sqlServer: true);

        var preview = (await Service(s.Env).PreviewAsync(await Ctx(s.Env), Req(s.Cash, s.Bank, 100_000m), default)).Value!;

        Assert.Equal("Depósito de efectivo", preview.Kind);
        Assert.Equal(5_890_000m, preview.ToBefore);
        Assert.Equal(5_990_000m, preview.ToAfter);
        Assert.Equal(9_090_000m, preview.TotalBalance);
        Assert.Contains("1.1.03", preview.DebitAccount);                               // entra al banco
        Assert.Contains("1.1.01", preview.CreditAccount);                              // sale de la caja
        Assert.False(preview.RequiresSupport);
        Assert.Empty(preview.Warnings);
        Assert.Empty(s.Env.T.NewContext().FinanceTransfers.ToList());
    }

    [SqlServerFact]
    public async Task SinCuentaDelPlanLaVistaPreviaAvisaQueElAsientoQuedaraIncompleto()
    {
        using var s = await Seed(sqlServer: true, assign: false);

        var preview = (await Service(s.Env).PreviewAsync(await Ctx(s.Env), Req(s.Cash, s.Bank, 100_000m), default)).Value!;

        Assert.Equal(2, preview.Warnings.Count(w => w.Contains("no tiene cuenta del plan")));
    }

    [SqlServerFact]
    public async Task UnMesCerradoNoAdmiteNiAltaNiAnulacion()
    {
        using var s = await Seed(sqlServer: true);
        var created = await Create(s, Req(s.Cash, s.Bank, 50_000m, FinanceEnv.Yesterday));
        var closeMonth = new DateOnly(FinanceEnv.Yesterday.Year, FinanceEnv.Yesterday.Month, 1);
        s.Env.T.Db.FinanceSettings.Single(x => x.BuildingId == s.Env.Building.Id).PeriodClosingEnabled = true;
        s.Env.T.Db.FinancePeriodClosures.Add(new FinancePeriodClosure
        {
            CompanyId = s.Env.Company.Id, BuildingId = s.Env.Building.Id, Year = closeMonth.Year, Month = closeMonth.Month, ClosedByUserId = s.Env.Admin.Id
        });
        s.Env.T.Db.SaveChanges();
        var service = Service(s.Env);

        var add = await service.CreateAsync(await Ctx(s.Env), Req(s.Cash, s.Bank, 1000m, FinanceEnv.Yesterday), s.Env.Company.Id, default);
        var cancel = await service.VoidAsync(await Ctx(s.Env), created.Id, "Se cargó mal", s.Env.Company.Id, default);

        Assert.Equal(FinancePeriodGuard.ClosedCode, add.Code);
        Assert.Equal(409, add.Status);
        Assert.Equal(FinancePeriodGuard.ClosedCode, cancel.Code);
        Assert.Equal(FinanceTransferStatus.Active, s.Env.T.NewContext().FinanceTransfers.Single().Status);
    }

    [SqlServerFact]
    public async Task UnEjercicioCerradoNoAdmiteNiAltaNiAnulacionHastaReabrirlo()
    {
        using var s = await Seed(sqlServer: true);
        var created = await Create(s, Req(s.Cash, s.Bank, 50_000m, FinanceEnv.Yesterday));
        var ctx = await Ctx(s.Env);
        var fiscalYear = FinancePeriods.FiscalYearOf(FinanceEnv.Yesterday, ctx.FiscalYearStartMonth);
        var closing = new JournalEntry
        {
            CompanyId = s.Env.Company.Id, BuildingId = s.Env.Building.Id, FiscalYear = fiscalYear, Number = 1, Kind = JournalEntryKind.Closing,
            Date = FinanceEnv.Yesterday, OperationDate = FinanceEnv.Yesterday, Description = "Cierre"
        };
        s.Env.T.Db.JournalEntries.Add(closing);
        s.Env.T.Db.SaveChanges();
        var service = Service(s.Env);

        var add = await service.CreateAsync(ctx, Req(s.Cash, s.Bank, 1000m, FinanceEnv.Yesterday), s.Env.Company.Id, default);
        var cancel = await service.VoidAsync(ctx, created.Id, "Se cargó mal", s.Env.Company.Id, default);
        Assert.Equal(FinanceTransferService.FiscalYearClosedCode, add.Code);
        Assert.Equal(FinanceTransferService.FiscalYearClosedCode, cancel.Code);

        // Reabrir el ejercicio (el cierre queda anulado por un contraasiento) habilita de nuevo las operaciones.
        s.Env.T.Db.JournalEntries.Add(new JournalEntry
        {
            CompanyId = s.Env.Company.Id, BuildingId = s.Env.Building.Id, FiscalYear = fiscalYear, Number = 2, Kind = JournalEntryKind.Reversal,
            ReversesEntryId = closing.Id, Date = FinanceEnv.Yesterday, OperationDate = FinanceEnv.Yesterday, Description = "Reapertura"
        });
        s.Env.T.Db.SaveChanges();
        Assert.True((await service.CreateAsync(ctx, Req(s.Cash, s.Bank, 1000m, FinanceEnv.Yesterday), s.Env.Company.Id, default)).Ok);
    }

    // ── Anulacion ────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task AnularDevuelveLosSaldosYExigeMotivo()
    {
        using var s = await Seed(sqlServer: true);
        var before = await Balances(s);
        var created = await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday));
        var service = Service(s.Env);

        var noReason = await service.VoidAsync(await Ctx(s.Env), created.Id, "  ", s.Env.Company.Id, default);
        Assert.Equal(400, noReason.Status);
        Assert.Equal(FinanceTransferStatus.Active, s.Env.T.NewContext().FinanceTransfers.Single().Status);

        var voided = await service.VoidAsync(await Ctx(s.Env), created.Id, "Se depositó en otro banco", s.Env.Company.Id, default);

        Assert.True(voided.Ok, voided.Message);
        Assert.Equal(FinanceTransferStatus.Voided, voided.Value!.Status);
        Assert.Equal("Se depositó en otro banco", voided.Value.VoidReason);
        Assert.Equal(s.Env.Admin.FullName, voided.Value.VoidedByName);
        Assert.Equal(before, await Balances(s));                                       // vuelve todo como estaba
        Assert.Single(s.Env.T.NewContext().FinanceTransfers.ToList());                   // y no se borra: queda el rastro
        Assert.Equal(2, s.Env.T.NewContext().FinanceAuditLogs.Count(x => x.EntityType == nameof(FinanceTransfer)));

        var again = await service.VoidAsync(await Ctx(s.Env), created.Id, "Otra vez", s.Env.Company.Id, default);
        Assert.Equal(FinanceTransferService.AlreadyVoidedCode, again.Code);
        Assert.Equal(404, (await service.VoidAsync(await Ctx(s.Env), Guid.NewGuid(), "No existe", s.Env.Company.Id, default)).Status);
    }

    [SqlServerFact]
    public async Task NoSePuedeAnularUnAporteSiElFondoQuedariaEnNegativo()
    {
        using var s = await Seed(sqlServer: true);
        var created = await Create(s, Req(s.Bank, s.Fund, 500_000m, FinanceEnv.Yesterday, "Acta 12/2026"));
        await Create(s, Req(s.Fund, s.Bank, 3_400_000m, FinanceEnv.Yesterday, "Acta 13/2026"));       // el fondo se gasta casi todo
        var service = Service(s.Env);

        var cancel = await service.VoidAsync(await Ctx(s.Env), created.Id, "Error de carga", s.Env.Company.Id, default);

        Assert.Equal(FinanceTransferService.FundNegativeCode, cancel.Code);
        Assert.Equal(2, s.Env.T.NewContext().FinanceTransfers.Count(x => x.Status == FinanceTransferStatus.Active));
    }

    [SqlServerFact]
    public async Task NoSePuedeAnularUnaTransferenciaMarcadaEnLaConciliacion()
    {
        using var s = await Seed(sqlServer: true);
        var created = await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday));
        var reconciliation = new BankReconciliation
        {
            CompanyId = s.Env.Company.Id, BuildingId = s.Env.Building.Id, AccountId = s.Bank.Id, StatementDate = FinancePeriods.Today(),
            StatementBalance = 6_040_000m, CreatedByUserId = s.Env.Admin.Id
        };
        s.Env.T.Db.BankReconciliations.Add(reconciliation);
        s.Env.T.Db.BankReconciledMovements.Add(new BankReconciledMovement
        {
            CompanyId = s.Env.Company.Id, BuildingId = s.Env.Building.Id, AccountId = s.Bank.Id, ReconciliationId = reconciliation.Id,
            SourceType = (int)LedgerSourceType.Transfer, SourceId = created.Id, Date = FinanceEnv.Yesterday, Amount = 150_000m,
            Description = "Depósito", MarkedByUserId = s.Env.Admin.Id
        });
        s.Env.T.Db.SaveChanges();

        var cancel = await Service(s.Env).VoidAsync(await Ctx(s.Env), created.Id, "Error de carga", s.Env.Company.Id, default);

        Assert.Equal(FinanceTransferService.ReconciledCode, cancel.Code);
        Assert.Equal(409, cancel.Status);
        Assert.Contains("conciliación", cancel.Message);
    }

    // ── Permisos ─────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task LoVenLosCuatroRolesPeroCarganYAnulanSoloSuperAdminYAdministrador()
    {
        using var s = await Seed(sqlServer: true);
        var api = Api(s.Env);
        var request = Req(s.Cash, s.Bank, 10_000m, FinanceEnv.Yesterday);

        foreach (var role in new[] { "SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            s.Env.LoginAs(role);
            Assert.Equal(role is "SuperAdmin" or "CompanyAdmin", FinanceEnv.Ok(await api.List(s.Env.Building.Id, default)).CanEdit);
        }

        foreach (var role in new[] { "CompanyOperator", "BuildingManager" })
        {
            s.Env.LoginAs(role);
            AssertStatus(await api.Create(s.Env.Building.Id, request, default), 403);
            AssertStatus(await api.Preview(s.Env.Building.Id, request, default), 403);
            AssertStatus(await api.Void(Guid.NewGuid(), s.Env.Building.Id, new VoidFinanceTransferRequest { Reason = "Error de carga" }, default), 403);
        }

        Assert.Empty(s.Env.T.NewContext().FinanceTransfers.ToList());

        s.Env.LoginAs("CompanyAdmin");
        var created = FinanceEnv.Ok(await api.Create(s.Env.Building.Id, request, default));
        s.Env.LoginAs("SuperAdmin");
        FinanceEnv.Ok(await api.Void(created.Id, s.Env.Building.Id, new VoidFinanceTransferRequest { Reason = "Error de carga" }, default));
    }

    [SqlServerFact]
    public async Task UnEdificioAjenoOSinFinanzasNoSeOperaNiSeLee()
    {
        using var s = await Seed(sqlServer: true);
        var api = Api(s.Env);
        s.Env.LoginAs("CompanyAdmin");

        s.Env.Access.Buildings.Clear();
        Assert.IsNotType<OkObjectResult>((await api.List(s.Env.Building.Id, default)).Result);
        Assert.IsNotType<OkObjectResult>((await api.Create(s.Env.Building.Id, Req(s.Cash, s.Bank, 1000m), default)).Result);

        s.Env.Access.Buildings.Add(s.Env.Building.Id);
        s.Env.T.Db.Buildings.Single(x => x.Id == s.Env.Building.Id).FinanceModuleEnabled = false;
        s.Env.T.Db.SaveChanges();
        AssertStatus(await api.Create(s.Env.Building.Id, Req(s.Cash, s.Bank, 1000m), default), 403);
        Assert.Empty(s.Env.T.NewContext().FinanceTransfers.ToList());
    }

    [SqlServerFact]
    public async Task UnaCuentaDeOtroEdificioNoSePuedeUsar()
    {
        using var s = await Seed(sqlServer: true);
        var other = s.Env.T.AddBuilding(s.Env.Company, "Otro edificio");
        var foreign = new FinancialAccount { CompanyId = s.Env.Company.Id, BuildingId = other.Id, Name = "Banco ajeno", Type = FinancialAccountType.Bank };
        s.Env.T.Db.FinancialAccounts.Add(foreign);
        s.Env.T.Db.SaveChanges();

        var result = await Service(s.Env).CreateAsync(await Ctx(s.Env), Req(s.Cash, foreign, 1000m), s.Env.Company.Id, default);

        Assert.Equal(400, result.Status);
        Assert.Empty(s.Env.T.NewContext().FinanceTransfers.ToList());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Libro Diario y base de datos (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    private static JournalPostingService Journal(FinanceEnv env) =>
        new(env.T.Db, new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant), env.Tenant);

    private static List<JournalEntry> Saved(FinanceEnv env) =>
        env.T.NewContext().JournalEntries.Include(x => x.Lines).Where(x => x.BuildingId == env.Building.Id).OrderBy(x => x.Number).ToList();

    private static FinanceJournalController JournalApi(FinanceEnv env) =>
        new(env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), Journal(env),
            new JournalBooksService(env.T.Db, new AccountingBooksService(env.T.Db, env.Tenant)));

    [SqlServerFact]
    public async Task LaTransferenciaSeAsientaComoUnSoloAsientoYAnularlaGeneraElContraasiento()
    {
        using var s = await Seed(sqlServer: true);
        var created = await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday.AddDays(-2)));
        var api = JournalApi(s.Env);

        FinanceEnv.Ok(await api.Post(s.Env.Building.Id, new PostJournalRequest { Through = FinanceEnv.Yesterday }, default));

        var transferEntries = Saved(s.Env).Where(e => e.SourceType == (int)LedgerSourceType.Transfer).ToList();
        var entry = Assert.Single(transferEntries);
        Assert.Equal(150_000m, entry.TotalDebit);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit);
        Assert.Equal(created.Id, entry.SourceId);
        Assert.Equal(s.Cash.Id, entry.SourceAccountId);
        Assert.Equal(150_000m, entry.Lines.Single(l => l.Code == "1.1.03").Debit);
        Assert.Equal(150_000m, entry.Lines.Single(l => l.Code == "1.1.01").Credit);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(5_890_000m + 150_000m,
            Saved(s.Env).SelectMany(e => e.Lines).Where(l => l.Code == "1.1.03").Sum(l => l.Debit - l.Credit));   // el banco del Mayor = el banco real

        // Antes de anular, el aviso de impacto cuenta el asiento vigente.
        var impact = await Journal(s.Env).ImpactAsync([s.Env.Building.Id], LedgerSourceType.Transfer, [created.Id], default);
        Assert.True(impact.Posted);
        Assert.Equal(1, impact.PostedCount);
        var list = await Service(s.Env).ListAsync(await Ctx(s.Env), true, default);
        Assert.Equal([JournalPostingService.LabelOf(entry.FiscalYear, entry.Number)], list.Items.Single().JournalLabels);

        // Se anula: aparece como diferencia «Transferencia anulada» con el motivo, y al asentar sale el contraasiento.
        var cancelled = await Service(s.Env).VoidAsync(await Ctx(s.Env), created.Id, "Se depositó en otro banco", s.Env.Company.Id, default);
        Assert.True(cancelled.Ok, cancelled.Message);
        var diffs = FinanceEnv.Ok(await api.Differences(s.Env.Building.Id, default));
        var diff = Assert.Single(diffs.Items);
        Assert.Equal(JournalDifferenceKind.Removed, diff.Kind);
        Assert.Equal("Transferencia anulada", diff.Cause);
        Assert.Equal("Se depositó en otro banco", diff.Reason);

        FinanceEnv.Ok(await api.Post(s.Env.Building.Id, new PostJournalRequest { Through = FinanceEnv.Yesterday }, default));
        var after = Saved(s.Env);
        var reversal = Assert.Single(after, e => e.Kind == JournalEntryKind.Reversal);
        Assert.Equal(entry.Id, reversal.ReversesEntryId);
        Assert.Contains("Se depositó en otro banco", reversal.Description);
        Assert.Equal(200_000m, after.SelectMany(e => e.Lines).Where(l => l.Code == "1.1.01").Sum(l => l.Debit - l.Credit));   // la caja vuelve a su saldo inicial
        Assert.Equal(5_890_000m, after.SelectMany(e => e.Lines).Where(l => l.Code == "1.1.03").Sum(l => l.Debit - l.Credit));
        Assert.Empty(FinanceEnv.Ok(await api.Differences(s.Env.Building.Id, default)).Items);
        Assert.Empty((await Journal(s.Env).ImpactAsync([s.Env.Building.Id], LedgerSourceType.Transfer, [created.Id], default)).Labels);
    }

    [SqlServerFact]
    public async Task LaBaseNoDejaBorrarNiCambiarImporteCuentasOFechaPeroSiAnular()
    {
        using var s = await Seed(sqlServer: true);
        var created = await Create(s, Req(s.Cash, s.Bank, 150_000m, FinanceEnv.Yesterday));
        var db = s.Env.T.Db;
        db.Database.ExecuteSqlRaw(FinanceTransferProtection.CreateDeleteSql());
        db.Database.ExecuteSqlRaw(FinanceTransferProtection.CreateUpdateSql());

        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("DELETE FROM FinanceTransfers"));
        var ex = Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("UPDATE FinanceTransfers SET Amount = 1"));
        Assert.Contains("no se puede modificar", ex.ToString());
        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("UPDATE FinanceTransfers SET FromAccountId = ToAccountId"));
        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("UPDATE FinanceTransfers SET TransferDate = '2020-01-01'"));
        Assert.Equal(150_000m, s.Env.T.NewContext().FinanceTransfers.Single().Amount);

        var cancelled = await Service(s.Env).VoidAsync(await Ctx(s.Env), created.Id, "Error de carga", s.Env.Company.Id, default);   // anular sigue andando
        Assert.True(cancelled.Ok, cancelled.Message);
        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("UPDATE FinanceTransfers SET Status = 1"));                 // y no se reactiva
        Assert.Equal(FinanceTransferStatus.Voided, s.Env.T.NewContext().FinanceTransfers.Single().Status);
    }

    [SqlServerFact]
    public async Task DosRetirosSimultaneosDelFondoNoLoDejanEnNegativo()
    {
        using var s = await Seed(sqlServer: true);
        var ctx = await Ctx(s.Env);

        // Cada retiro por si solo cabe (2.000.000 de 3.000.000), los dos juntos no.
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = s.Env.T.NewContext();
            return await Service(s.Env, db).CreateAsync(ctx, Req(s.Fund, s.Bank, 2_000_000m, FinanceEnv.Yesterday, "Acta 1"), s.Env.Company.Id, default);
        })).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, s.Env.T.NewContext().FinanceTransfers.Count());
        Assert.Equal(1_000_000m, (await Balances(s))["Fondo de reserva"]);
    }
}
