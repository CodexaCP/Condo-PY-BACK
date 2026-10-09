using Condo.Api.Controllers;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Condo.Tests.Finance;

/// <summary>
/// Finanzas, informe para la asamblea: los numeros tienen que coincidir con las pantallas de origen (saldos, movimientos, morosidad y cuentas por
/// pagar), el rango y las notas se validan, y el PDF se genera con cualquier combinacion de secciones. El libro suma importes en la base: las
/// pruebas que lo leen usan SQL Server real y se omiten solas sin la variable CONDO_TEST_SQLSERVER.
/// </summary>
public class ConfigCenterPhase7Tests
{
    static ConfigCenterPhase7Tests() => QuestPDF.Settings.License = LicenseType.Community;

    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();

    private static bool IsPdf(byte[] bytes) => bytes.Length > 1000 && bytes.Take(4).SequenceEqual(PdfMagic);

    // ═════════════════════════════════════════════════════════════════════════
    // PDF con datos armados a mano (sin base de datos)
    // ═════════════════════════════════════════════════════════════════════════

    private static AssemblyReportDto Sample(bool budget = true, bool reserve = true, string? notes = "Se aprobó el cambio de bomba.\nPendiente cotización de pintura.") => new()
    {
        BuildingId = Guid.NewGuid(),
        BuildingName = "Edificio Ñandutí",
        From = new DateOnly(2026, 1, 1),
        To = new DateOnly(2026, 9, 30),
        GeneratedAt = new DateTime(2026, 10, 9, 10, 30, 0),
        Notes = notes,
        OpeningBalance = 5_000_000m,
        TotalIncome = 90_000_000m,
        TotalExpense = 70_000_000m,
        NetResult = 20_000_000m,
        ClosingBalance = 25_000_000m,
        Accounts =
        [
            new AssemblyReportAccountDto { Name = "Banco Itaú", Type = FinancialAccountType.Bank, Opening = 4_000_000m, Inflows = 80_000_000m, Outflows = 60_000_000m, Closing = 24_000_000m },
            new AssemblyReportAccountDto { Name = "Caja", Type = FinancialAccountType.Cash, Opening = 1_000_000m, Inflows = 10_000_000m, Outflows = 10_000_000m, Closing = 1_000_000m },
            new AssemblyReportAccountDto { Name = "Sin cuenta asignada", IsUnassigned = true }
        ],
        IncomeLines = [new AssemblyReportRubroDto { Code = "4.1.01", Name = "Expensas ordinarias", Amount = 90_000_000m }],
        ExpenseLines =
        [
            new AssemblyReportRubroDto { Code = "5.01.01", Name = "Mantenimiento", Amount = 40_000_000m },
            new AssemblyReportRubroDto { Code = string.Empty, Name = "Sin clasificar", Amount = 30_000_000m }
        ],
        Budget = budget
            ? new FinanceBudgetVsActualDto
            {
                BuildingId = Guid.NewGuid(), BuildingName = "x", Year = 2026, Month = 9, FiscalYear = 2026,
                FiscalYearStart = new DateOnly(2026, 1, 1), FiscalYearEnd = new DateOnly(2026, 12, 31), AsOf = new DateOnly(2026, 9, 30),
                ExpenseLines =
                [
                    new FinanceBudgetVsActualLineDto
                    {
                        Code = "5.01.01", Name = "Mantenimiento", YtdBudget = 30_000_000m, YtdActual = 40_000_000m, YtdVariance = 10_000_000m,
                        YtdVariancePct = 33.3m, YtdStatus = BudgetStatus.Red
                    },
                    new FinanceBudgetVsActualLineDto { Code = "5.01.02", Name = "Sin uso", YtdBudget = 0m, YtdActual = 0m }
                ],
                ExpenseTotals = new FinanceBudgetTotalsDto { YtdBudget = 30_000_000m, YtdActual = 40_000_000m }
            }
            : null,
        Reserve = reserve
            ? new AssemblyReportReserveDto { AccountName = "Fondo de reserva", ReserveFundPercentage = 5m, Opening = 1m, Contributions = 2m, Uses = 1m, Closing = 2m }
            : null,
        SnapshotDate = new DateOnly(2026, 10, 9),
        Receivables = new ReceivablesSummaryDto
        {
            TotalCharged = 100_000_000m, TotalCollected = 90_000_000m, TotalPending = 10_000_000m, CollectionRatePercentage = 90m,
            OverdueAmount = 6_000_000m, OverdueUnits = 4,
            Aging = [new PayableAgingBucketDto { Label = "1-30 días", Count = 3, Total = 4_000_000m }, new PayableAgingBucketDto { Label = "Más de 90 días", Count = 1, Total = 2_000_000m }]
        },
        Payables = new PayablesSummaryDto
        {
            PendingCount = 2, PendingTotal = 5_000_000m, OverdueCount = 1, OverdueTotal = 2_000_000m, DueNext7DaysTotal = 1_000_000m,
            Aging = [new PayableAgingBucketDto { Label = "1-30 días", Count = 1, Total = 2_000_000m }]
        },
        Warnings = ["Hay movimientos sin cuenta asignada: figuran en una fila aparte de los saldos."]
    };

    [Fact]
    public void ElPdfSeGeneraConTodasLasSecciones()
    {
        var bytes = new AssemblyReportPdfDocument(Sample()).GeneratePdf();

        Assert.True(IsPdf(bytes));
    }

    [Theory]
    [InlineData(false, true, "Notas")]
    [InlineData(true, false, "Notas")]
    [InlineData(false, false, null)]
    [InlineData(true, true, "")]
    public void ElPdfSeGeneraSinPresupuestoSinFondoYSinNotas(bool budget, bool reserve, string? notes)
    {
        var bytes = new AssemblyReportPdfDocument(Sample(budget, reserve, notes)).GeneratePdf();

        Assert.True(IsPdf(bytes));
    }

    [Fact]
    public void ElPdfSeGeneraConElInformeVacioYConNotasLargas()
    {
        var empty = new AssemblyReportDto
        {
            BuildingName = "Vacío", From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 31), GeneratedAt = DateTime.Now,
            SnapshotDate = new DateOnly(2026, 2, 1)
        };
        Assert.True(IsPdf(new AssemblyReportPdfDocument(empty).GeneratePdf()));

        var notes = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Punto {i}: " + new string('x', 150)));
        var big = Sample(notes: notes);
        big.IncomeLines = Enumerable.Range(1, 120).Select(i => new AssemblyReportRubroDto { Code = $"4.{i}", Name = $"Rubro {i}", Amount = i * 1000m }).ToList();
        Assert.True(IsPdf(new AssemblyReportPdfDocument(big).GeneratePdf()));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Validaciones (SQLite alcanza: no llegan a leer el libro)
    // ═════════════════════════════════════════════════════════════════════════

    private static AssemblyReportService Service(FinanceEnv env)
    {
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        var ledger = new FinanceLedgerService(env.T.Db);
        var budgets = new FinanceBudgetService(env.T.Db, ledger);
        return new AssemblyReportService(env.T.Db, ledger, new FinanceReportService(env.T.Db, ledger, budgets), new FinancePayablesService(env.T.Db));
    }

    private static FinanceAssemblyReportController Api(FinanceEnv env)
    {
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        return new FinanceAssemblyReportController(
            env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), Service(env));
    }

    private static async Task<LedgerContext> Context(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static Task<(AssemblyReportDto? Report, string? Error)> Build(
        FinanceEnv env, LedgerContext ctx, DateOnly? from = null, DateOnly? to = null, string? notes = null) =>
        Service(env).BuildAsync(ctx, from, to, notes, new DateTime(2026, 10, 9, 10, 0, 0), default);

    [Fact]
    public async Task ElRangoYLasNotasSeValidanAntesDeLeerElLibro()
    {
        using var env = new FinanceEnv();
        env.Setup();
        var today = FinancePeriods.Today();
        env.T.Db.FinanceSettings.Single().FinanceStartDate = today.AddYears(-2);       // para que el limite de 400 dias se pueda alcanzar
        env.T.Db.SaveChanges();
        var ctx = await Context(env);

        Assert.Contains("futura", (await Build(env, ctx, to: today.AddDays(1))).Error);
        Assert.Contains("posterior", (await Build(env, ctx, from: today, to: today.AddDays(-1))).Error);
        Assert.Contains("400", (await Build(env, ctx, from: today.AddDays(-401), to: today)).Error);
        Assert.Contains("arranque", (await Build(env, ctx, from: ctx.StartDate.AddDays(-30), to: ctx.StartDate.AddDays(-5))).Error);
        Assert.Contains("4000", (await Build(env, ctx, notes: new string('x', AssemblyReportService.NotesMaxLength + 1))).Error);
    }

    [Fact]
    public async Task ElControladorDevuelve400ConElMensajeYNoDejaPasarAUnEdificioAjeno()
    {
        using var env = new FinanceEnv();
        env.Setup();
        var api = Api(env);
        var tomorrow = FinancePeriods.Today().AddDays(1);

        var bad = await api.Preview(new AssemblyReportRequest { BuildingId = env.Building.Id, To = tomorrow }, default);
        Assert.Contains("futura", Assert.IsType<BadRequestObjectResult>(bad.Result).Value!.ToString());
        Assert.IsType<BadRequestObjectResult>(await api.Pdf(new AssemblyReportRequest { BuildingId = env.Building.Id, To = tomorrow }, default));

        env.LoginAs("CompanyAdmin");
        env.Access.Buildings.Clear();
        Assert.IsNotType<OkObjectResult>((await api.Preview(new AssemblyReportRequest { BuildingId = env.Building.Id }, default)).Result);
        Assert.IsNotType<FileContentResult>(await api.Pdf(new AssemblyReportRequest { BuildingId = env.Building.Id }, default));
    }

    [Fact]
    public async Task SinLaConfiguracionInicialDeFinanzasNoSeGeneraNada()
    {
        using var env = new FinanceEnv();                                      // sin Setup(): no hay fecha de arranque
        var api = Api(env);

        var preview = await api.Preview(new AssemblyReportRequest { BuildingId = env.Building.Id }, default);
        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(preview.Result).StatusCode);
        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(await api.Pdf(new AssemblyReportRequest { BuildingId = env.Building.Id }, default)).StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Informe con el libro real (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    // Un banco, una caja y un fondo con saldo inicial; ingresos y gastos antes y dentro del rango.
    private sealed record Fixture(FinancialAccount Bank, FinancialAccount Fund, DateOnly RangeFrom, DateOnly RangeTo);

    private static Fixture Seed(FinanceEnv env)
    {
        var bank = env.Setup();
        bank.OpeningBalance = 1_000_000m;
        var fund = new FinancialAccount
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Fondo de reserva", Type = FinancialAccountType.ReserveFund, OpeningBalance = 500_000m
        };
        env.T.Db.FinancialAccounts.Add(fund);
        env.T.Db.SaveChanges();
        env.SeedTemplate();

        var to = FinanceEnv.Yesterday;
        env.AddIncome(null, 400_000m, to.AddDays(-30));                       // antes del rango: entra en el saldo inicial
        env.AddExpense(null, 100_000m, BuildingExpenseCategory.Other, to.AddDays(-28));
        env.AddIncome(null, 1_000_000m, to.AddDays(-10));                     // dentro del rango
        env.AddIncome(null, 250_000m, to.AddDays(-5));
        env.AddExpense(null, 300_000m, BuildingExpenseCategory.Other, to.AddDays(-8));
        var byFund = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, to.AddDays(-3));
        env.T.Db.BuildingExpenses.Single(x => x.Id == byFund.Id).PaidByReserveFund = true;
        env.T.Db.SaveChanges();
        return new Fixture(bank, fund, to.AddDays(-15), to);
    }

    [SqlServerFact]
    public async Task LosNumerosCoincidenConLasPantallasDeFinanzas()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var ctx = await Context(env);
        var ledger = new FinanceLedgerService(env.T.NewContext());
        var budgets = new FinanceBudgetService(env.T.NewContext(), ledger);
        var reports = new FinanceReportService(env.T.NewContext(), ledger, budgets);

        var (report, error) = await Build(env, ctx, f.RangeFrom, f.RangeTo);

        Assert.Null(error);
        Assert.Equal(f.RangeFrom, report!.From);
        Assert.Equal(f.RangeTo, report.To);
        Assert.Equal(env.Building.Name, report.BuildingName);

        // Saldos: el de cierre es el de la pantalla de saldos y el de apertura, el del dia anterior al rango.
        var closing = await reports.BalancesAsync(ctx, f.RangeTo, default);
        var opening = await reports.BalancesAsync(ctx, f.RangeFrom.AddDays(-1), default);
        Assert.Equal(closing.TotalBalance, report.ClosingBalance);
        Assert.Equal(opening.TotalBalance, report.OpeningBalance);
        Assert.Equal(report.ClosingBalance, report.OpeningBalance + report.NetResult);
        foreach (var account in report.Accounts.Where(a => !a.IsUnassigned))
        {
            Assert.Equal(closing.Accounts.Single(a => a.Name == account.Name).Balance, account.Closing);
            Assert.Equal(opening.Accounts.Single(a => a.Name == account.Name).Balance, account.Opening);
        }

        // Movimientos del rango: los mismos totales que el libro de movimientos.
        var (page, _) = await reports.MovementsAsync(ctx, new FinanceReportService.MovementsQuery(f.RangeFrom, f.RangeTo, Unpaged: true), default);
        Assert.Equal(page!.TotalIn, report.TotalIncome);
        Assert.Equal(page.TotalOut, report.TotalExpense);
        Assert.Equal(1_250_000m, report.TotalIncome);
        Assert.Equal(380_000m, report.TotalExpense);
        Assert.Equal(870_000m, report.NetResult);
        Assert.Equal(report.TotalIncome, report.IncomeLines.Sum(l => l.Amount));
        Assert.Equal(report.TotalExpense, report.ExpenseLines.Sum(l => l.Amount));

        // Lo de antes del rango no cuenta como movimiento pero si en el saldo inicial: 1.000.000 + 500.000 + 400.000 - 100.000.
        Assert.Equal(1_800_000m, report.OpeningBalance);
    }

    [SqlServerFact]
    public async Task ElFondoDeReservaTomaSuSaldoInicialYLosUsosDelRango()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);

        var (report, _) = await Build(env, await Context(env), f.RangeFrom, f.RangeTo);

        var reserve = report!.Reserve!;
        Assert.Equal("Fondo de reserva", reserve.AccountName);
        Assert.Equal(500_000m, reserve.Opening);
        Assert.Equal(0m, reserve.Contributions);
        Assert.Equal(80_000m, reserve.Uses);
        Assert.Equal(420_000m, reserve.Closing);
        Assert.Equal(420_000m, report.Accounts.Single(a => a.Name == "Fondo de reserva").Closing);
    }

    [SqlServerFact]
    public async Task SinCuentaDeFondoNoHaySeccionDeFondoYSinPresupuestoSeAvisa()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        env.SeedTemplate();
        env.AddIncome(null, 100_000m, FinanceEnv.Yesterday.AddDays(-2));

        var (report, error) = await Build(env, await Context(env), FinanceEnv.Yesterday.AddDays(-5), FinanceEnv.Yesterday);

        Assert.Null(error);
        Assert.Null(report!.Reserve);
        Assert.Null(report.Budget);
        Assert.Contains(report.Warnings, w => w.Contains("presupuesto"));
        Assert.Equal(100_000m, report.TotalIncome);
    }

    [SqlServerFact]
    public async Task ConPresupuestoTraeLaEjecucionAcumuladaDelEjercicio()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        var plan = env.SeedTemplate();
        var parents = plan.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        var leaf = plan.Where(c => c.Type == LedgerCategoryType.Expense && !parents.Contains(c.Id)).OrderBy(c => c.Code, StringComparer.Ordinal).First();
        env.AddBudget(leaf.Id, 300_000m);
        env.AddExpense(leaf.Id, 120_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday);

        var ctx = await Context(env);
        var (report, _) = await Build(env, ctx, FinanceEnv.Yesterday.AddDays(-3), FinanceEnv.Yesterday);

        var budget = report!.Budget!;
        Assert.Equal(FinanceEnv.Yesterday.Year, budget.Year);
        var line = budget.ExpenseLines.Single(l => l.CategoryId == leaf.Id);
        Assert.Equal(300_000m, line.YtdBudget);
        Assert.Equal(120_000m, line.YtdActual);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("presupuesto"));

        // Es la misma ejecucion que muestra la pantalla de presupuesto vs. real.
        var ledger = new FinanceLedgerService(env.T.NewContext());
        var reports = new FinanceReportService(env.T.NewContext(), ledger, new FinanceBudgetService(env.T.NewContext(), ledger));
        var (screen, _) = await reports.BudgetVsActualAsync(ctx, FinanceEnv.Yesterday.Year, FinanceEnv.Yesterday.Month, default);
        Assert.Equal(screen!.ExpenseTotals.YtdBudget, budget.ExpenseTotals.YtdBudget);
        Assert.Equal(screen.ExpenseTotals.YtdActual, budget.ExpenseTotals.YtdActual);
    }

    [SqlServerFact]
    public async Task LaMorosidadYLasCuentasPorPagarSonLasDeLasPantallasYNoIdentificanANadie()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        env.SeedTemplate();
        var day = FinanceEnv.Yesterday;
        var due = env.AddExpense(null, 200_000m, BuildingExpenseCategory.Other, day.AddDays(-20));
        env.T.Db.BuildingExpenses.Single(x => x.Id == due.Id).DueDate = day.AddDays(-5);          // factura vencida y sin pagar
        env.T.Db.SaveChanges();

        var (report, _) = await Build(env, await Context(env), day.AddDays(-30), day);

        var payables = new FinancePayablesService(env.T.NewContext());
        var today = FinancePeriods.Today();
        var expectedPayables = await payables.SummaryAsync(env.Building.Id, today, default);
        var expectedReceivables = await payables.ReceivablesAsync(env.Building.Id, today, default);
        Assert.Equal(today, report!.SnapshotDate);
        Assert.Equal(expectedPayables.PendingTotal, report.Payables.PendingTotal);
        Assert.Equal(200_000m, report.Payables.OverdueTotal);
        Assert.Equal(expectedReceivables.TotalPending, report.Receivables.TotalPending);
        Assert.Equal(expectedReceivables.OverdueAmount, report.Receivables.OverdueAmount);
        Assert.Equal(expectedReceivables.Aging.Count, report.Receivables.Aging.Count);

        // La morosidad solo trae totales y tramos: ningun dato que identifique unidades o propietarios.
        var names = typeof(ReceivablesSummaryDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Owner") || n.Contains("Tenant") || n.Contains("Name"));
        Assert.Equal(["Label", "Count", "Total"], typeof(PayableAgingBucketDto).GetProperties().Select(p => p.Name).ToArray());
    }

    [SqlServerFact]
    public async Task LasNotasSeLimpianYSinNotasQuedaNulo()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        var ctx = await Context(env);

        var (cleaned, _) = await Build(env, ctx, notes: "  Linea uno\r\nLinea dos\u0007\rLinea tres\t ok  ");
        Assert.Equal("Linea uno\nLinea dos\nLinea tres\t ok", cleaned!.Notes);

        Assert.Null((await Build(env, ctx, notes: "   \n  ")).Report!.Notes);
        Assert.Null((await Build(env, ctx, notes: null)).Report!.Notes);
    }

    [SqlServerFact]
    public async Task SinFechasElInformeVaDelComienzoDelEjercicioAHoyYUnDesdeAnteriorAlArranqueSeAcota()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        var ctx = await Context(env);
        var today = FinancePeriods.Today();
        var fiscalStart = FinancePeriods.FiscalYearRange(FinancePeriods.FiscalYearOf(today, ctx.FiscalYearStartMonth), ctx.FiscalYearStartMonth).Start;

        var (byDefault, _) = await Build(env, ctx);
        Assert.Equal(today, byDefault!.To);
        Assert.Equal(fiscalStart < ctx.StartDate ? ctx.StartDate : fiscalStart, byDefault.From);

        var (clamped, error) = await Build(env, ctx, from: ctx.StartDate.AddDays(-10), to: ctx.StartDate.AddDays(20));
        Assert.Null(error);
        Assert.Equal(ctx.StartDate, clamped!.From);
    }

    [SqlServerFact]
    public async Task ElControladorGeneraElPdfYLosCuatroRolesPueden()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var api = Api(env);
        var request = new AssemblyReportRequest { BuildingId = env.Building.Id, From = f.RangeFrom, To = f.RangeTo, Notes = "Se aprobó el presupuesto." };

        foreach (var role in new[] { "SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            var preview = FinanceEnv.Ok(await api.Preview(request, default));
            Assert.Equal("Se aprobó el presupuesto.", preview.Notes);
            Assert.Equal(1_250_000m, preview.TotalIncome);

            var file = Assert.IsType<FileContentResult>(await api.Pdf(request, default));
            Assert.Equal("application/pdf", file.ContentType);
            Assert.True(IsPdf(file.FileContents));
            Assert.Equal($"informe_asamblea_{f.RangeFrom:yyyyMMdd}_{f.RangeTo:yyyyMMdd}.pdf", file.FileDownloadName);
        }
    }
}
