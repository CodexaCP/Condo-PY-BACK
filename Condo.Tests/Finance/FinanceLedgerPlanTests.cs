using ClosedXML.Excel;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;

namespace Condo.Tests.Finance;

/// <summary>
/// El libro, el flujo de caja y el presupuesto vs. real con un plan nuevo: lo cargado sin elegir rubro cae en la cuenta por defecto de su
/// categoria (o, si el plan no la tiene, se rotula), los reportes por grupo incluyen todo lo que cuelga y la exportacion muestra la ruta.
/// Estas pruebas no usan base de datos (la logica es pura); las consultas agrupadas reales estan en <see cref="FinanceLedgerPlanSqlServerTests"/>.
/// </summary>
public class FinanceLedgerPlanTests
{
    private static PlanRowSpec Spec(string code, string name, string? parent, LedgerCategoryType type,
        BuildingExpenseCategory? expense = null, BuildingIncomeCategory? income = null, string? key = null) =>
        new(code, name, parent, type, null, true, key, expense, income);

    private static LedgerContext Ctx(IReadOnlyList<LedgerCategory> cats) => new()
    {
        BuildingId = Guid.NewGuid(),
        BuildingName = "Edificio",
        StartDate = new DateOnly(2026, 1, 1),
        FiscalYearStartMonth = 1,
        IncomeTreatment = IncomeTreatment.CreditToOwners,
        Accounts = [],
        Resolved = new LedgerAccounts(null, null, null),
        Categories = cats
    };

    // Un plan importado: las cuentas existen pero ninguna tiene funcion especial.
    private static List<LedgerCategory> ImportedPlan(params PlanRowSpec[] specs)
    {
        var building = Guid.NewGuid();
        var company = Guid.NewGuid();
        var byCode = specs.ToDictionary(
            s => s.Code,
            s => new LedgerCategory
            {
                CompanyId = company, BuildingId = building, Code = s.Code, Name = s.Name, Type = s.Type, IsActive = s.IsActive,
                SystemKey = s.SystemKey, ExpenseCategory = s.ExpenseCategory, IncomeCategory = s.IncomeCategory
            });
        foreach (var s in specs.Where(s => s.ParentCode is not null)) byCode[s.Code].ParentId = byCode[s.ParentCode!].Id;
        return byCode.Values.ToList();
    }

    [Fact]
    public void ElGastoSinRubroUsaLaCuentaConLaFuncionDeSuCategoria()
    {
        var cats = FinanceChartTemplate.CreateEntities(Guid.NewGuid(), Guid.NewGuid()).ToList();
        var ctx = Ctx(cats);
        var ande = cats.Single(c => c.Code == "5.02.01");
        var electricidad = cats.Single(c => c.Code == "5.04.01");

        Assert.Equal("Expense.Ande", ctx.ExpenseKey(null, BuildingExpenseCategory.Ande));   // la cuenta 5.02.01 tiene la funcion
        Assert.Equal(ande.Id, ctx.CategoryFor(ctx.ExpenseKey(null, BuildingExpenseCategory.Ande))!.Id);
        Assert.Equal(electricidad.RubroKey, ctx.ExpenseKey(electricidad.Id, BuildingExpenseCategory.Maintenance));   // eligio cuenta: manda
        Assert.Equal("Income.Other", ctx.IncomeKey(null, BuildingIncomeCategory.Other));
        Assert.Equal("4.3.07", ctx.CategoryFor(ctx.IncomeKey(null, BuildingIncomeCategory.Other))!.Code);
    }

    [Fact]
    public void ConUnPlanImportadoSinFunciones_ElGastoSinRubroCaeEnLaPrimeraCuentaActivaDeSuCategoria()
    {
        var cats = ImportedPlan(
            Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
            Spec("5.1", "Servicios", "5", LedgerCategoryType.Expense),
            Spec("5.1.2", "Luz del edificio", "5.1", LedgerCategoryType.Expense, BuildingExpenseCategory.Ande),
            Spec("5.1.1", "Luz de áreas comunes", "5.1", LedgerCategoryType.Expense, BuildingExpenseCategory.Ande),
            Spec("5.1.3", "Agua", "5.1", LedgerCategoryType.Expense, BuildingExpenseCategory.Essap));
        cats.Single(c => c.Code == "5.1.1").IsActive = false;   // la primera por codigo esta inactiva: se prefiere una activa
        var ctx = Ctx(cats);

        var key = ctx.ExpenseKey(null, BuildingExpenseCategory.Ande);
        Assert.Equal("5.1.2", ctx.CategoryFor(key)!.Code);
        Assert.Equal("5.1.3", ctx.CategoryFor(ctx.ExpenseKey(null, BuildingExpenseCategory.Essap))!.Code);

        // Sin ninguna cuenta de esa categoria queda la clave generica (el reporte la rotula).
        Assert.Equal("Expense.Elevator", ctx.ExpenseKey(null, BuildingExpenseCategory.Elevator));
        Assert.Null(ctx.CategoryFor("Expense.Elevator"));
        // Los grupos no se eligen como cuenta por defecto.
        Assert.Equal("Expense.Other", ctx.ExpenseKey(null, BuildingExpenseCategory.Other));
    }

    [Fact]
    public void ElFlujoDeCajaMuestraLaCuentaPorDefectoYRotulaLoQueNoTieneCuenta()
    {
        var cats = ImportedPlan(
            Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
            Spec("5.1", "Servicios", "5", LedgerCategoryType.Expense),
            Spec("5.1.1", "Agua", "5.1", LedgerCategoryType.Expense, BuildingExpenseCategory.Essap));
        var ctx = Ctx(cats);
        var essap = cats.Single(c => c.Code == "5.1.1");

        var buckets = new List<LedgerBucket>
        {
            Bucket(ctx, null, BuildingExpenseCategory.Essap, 10_000m),     // sin rubro -> la cuenta de agua
            Bucket(ctx, essap.Id, BuildingExpenseCategory.Essap, 5_000m),  // eligio la cuenta
            Bucket(ctx, null, BuildingExpenseCategory.Ande, 40_000m)       // el plan no tiene cuenta de ANDE
        };

        var flow = FinanceReportBuilder.CashFlow(ctx, buckets, 2026, new DateOnly(2026, 9, 30));
        Assert.Equal(55_000m, flow.OutLines.Sum(l => l.Total));
        var water = flow.OutLines.Single(l => l.Code == "5.1.1");
        Assert.Equal(15_000m, water.Total);                     // las dos formas de cargar suman en la misma linea
        Assert.Equal("5.1", water.GroupCode);
        var orphan = flow.OutLines.Single(l => l.CategoryId is null);
        Assert.Equal("ANDE (sin rubro)", orphan.Name);
        Assert.Equal(40_000m, orphan.Total);
    }

    private static LedgerBucket Bucket(LedgerContext ctx, Guid? categoryId, BuildingExpenseCategory category, decimal amount)
    {
        var rule = FinanceLedgerRules.ForExpense(category, false, ctx.Resolved, ctx.ExpenseKey(categoryId, category))!;
        return new LedgerBucket(2026, 9, rule.AccountId, rule.RubroKey, rule.Direction, amount);
    }

    [Fact]
    public void ElPresupuestoVsRealNoDejaAfueraLoQueNoTieneCuentaEnElPlan()
    {
        var cats = ImportedPlan(
            Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
            Spec("5.1", "Servicios", "5", LedgerCategoryType.Expense),
            Spec("5.1.1", "Agua", "5.1", LedgerCategoryType.Expense, BuildingExpenseCategory.Essap));
        var ctx = Ctx(cats);
        var essap = cats.Single(c => c.Code == "5.1.1");

        var actuals = new Dictionary<(int Year, int Month, string RubroKey), decimal>
        {
            [(2026, 9, essap.RubroKey)] = 10_000m,
            [(2026, 9, "Expense.Ande")] = 40_000m              // gasto sin rubro de una categoria sin cuenta
        };
        var cells = new[] { new BudgetCell(essap.Id, 2026, 9, 15_000m) };

        var report = FinanceBudgetCalculator.BuildVsActual(ctx, 2026, 9, new DateOnly(2026, 9, 30), cells, actuals);

        Assert.Equal(50_000m, report.ExpenseTotals.MonthActual);   // no queda por debajo de lo gastado
        Assert.Equal(15_000m, report.ExpenseTotals.MonthBudget);
        var orphan = report.ExpenseLines.Single(l => l.CategoryId == Guid.Empty);
        Assert.Equal("ANDE (sin rubro)", orphan.Name);
        Assert.Equal("Sin cuenta en el plan", orphan.GroupName);
        Assert.Equal(BudgetStatus.Red, orphan.MonthStatus);        // gasto sin presupuesto
        Assert.Equal(10_000m, report.ExpenseLines.Single(l => l.Code == "5.1.1").MonthActual);
        Assert.Equal(BudgetStatus.Green, report.ExpenseLines.Single(l => l.Code == "5.1.1").MonthStatus);
    }

    [Fact]
    public void LaCobranzaSinCuentaAsignadaIgualCuentaComoIngresoEnElPresupuestoVsReal()
    {
        // Plan generico al que se le quito la cuenta de cobranza ordinaria.
        var cats = FinanceChartTemplate.CreateEntities(Guid.NewGuid(), Guid.NewGuid()).Where(c => c.SystemKey != "Collection.Ordinary").ToList();
        var ctx = Ctx(cats);
        var buckets = new[]
        {
            new LedgerBucket(2026, 9, null, "Collection.Ordinary", LedgerDirection.In, 1_000_000m),
            new LedgerBucket(2026, 9, null, "Expense.Ande", LedgerDirection.Out, 5m)
        };

        var actuals = FinanceBudgetService.IncomeActuals(ctx, buckets);
        Assert.Equal(1_000_000m, actuals[(2026, 9, "Collection.Ordinary")]);
        Assert.DoesNotContain(actuals.Keys, k => k.RubroKey == "Expense.Ande");

        var report = FinanceBudgetCalculator.BuildVsActual(ctx, 2026, 9, new DateOnly(2026, 9, 30), [], actuals);
        var line = Assert.Single(report.IncomeLines);
        Assert.Equal("Cobro de expensas ordinarias", line.Name);
        Assert.Equal(1_000_000m, report.IncomeTotals.MonthActual);
    }

    [Fact]
    public void ElFiltroPorGrupoIncluyeTodoLoQueCuelgaALoLargoDeLosNiveles()
    {
        var cats = FinanceChartTemplate.CreateEntities(Guid.NewGuid(), Guid.NewGuid()).ToList();
        var ctx = Ctx(cats);
        var ande = cats.Single(c => c.Code == "5.02.01");
        var electricidad = cats.Single(c => c.Code == "5.04.01");
        var cobro = cats.Single(c => c.Code == "4.1.01");

        var rows = new List<LedgerRow>
        {
            new(new DateOnly(2026, 9, 1), null, ande.RubroKey, LedgerDirection.Out, 10m, LedgerSourceType.BuildingExpense, Guid.NewGuid(), "ANDE", "", ""),
            new(new DateOnly(2026, 9, 2), null, electricidad.RubroKey, LedgerDirection.Out, 20m, LedgerSourceType.BuildingExpense, Guid.NewGuid(), "Electricidad", "", ""),
            new(new DateOnly(2026, 9, 3), null, cobro.RubroKey, LedgerDirection.In, 30m, LedgerSourceType.OwnerPayment, Guid.NewGuid(), "Cobro", "", "")
        };

        int Count(string code) => FinanceReportBuilder.MovementsPage(
            ctx, rows, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, false, cats.Single(c => c.Code == code).Id, null, false, 1, 50, null).TotalCount;

        Assert.Equal(1, Count("5.02.01"));   // la cuenta
        Assert.Equal(1, Count("5.02"));      // su grupo
        Assert.Equal(2, Count("5"));         // la clase entera (dos niveles abajo)
        Assert.Equal(1, Count("4"));
        Assert.Equal(0, Count("1"));
    }

    [Fact]
    public void LaExportacionDelPlanMuestraLaRutaYLaFuncion()
    {
        var cats = FinanceChartTemplate.CreateEntities(Guid.NewGuid(), Guid.NewGuid()).ToList();
        var ctx = Ctx(cats);
        using var wb = new XLWorkbook();
        FinanceExcelExporter.AddChart(wb, ctx);
        var ws = wb.Worksheet("Plan de cuentas");

        IXLRow RowOf(string code) => ws.RowsUsed().Single(r => r.Cell(1).GetString() == code);

        var ande = RowOf("5.02.01");
        Assert.Equal("5 · EGRESOS › 5.02 · Servicios públicos", ande.Cell(4).GetString());
        Assert.Equal("Gastos", ande.Cell(3).GetString());
        Assert.Contains("ANDE", ande.Cell(6).GetString());
        Assert.Equal("ANDE", ande.Cell(8).GetString());
        Assert.Equal(3, (int)ande.Cell(9).GetDouble());

        Assert.Equal("Activo", RowOf("1.1.01").Cell(3).GetString());
        Assert.Equal("Pasivo", RowOf("2.1.01").Cell(3).GetString());
        Assert.Equal("Patrimonio / Fondos", RowOf("3.1.01").Cell(3).GetString());
        Assert.Equal("Grupo", RowOf("5.02").Cell(6).GetString());
        Assert.Equal(1, (int)RowOf("5").Cell(9).GetDouble());
        Assert.Equal("Inactivo", RowOf("5.03.07").Cell(7).GetString());
        Assert.Equal(cats.Count, ws.RowsUsed().Count(r => r.RowNumber() >= 5));
    }
}

/// <summary>
/// Lo mismo contra las consultas agrupadas reales (suma de importes por mes y cuenta): necesitan SQL Server porque SQLite no suma decimales.
/// Se omiten solas sin la variable CONDO_TEST_SQLSERVER.
/// </summary>
public class FinanceLedgerPlanSqlServerTests
{
    private static (FinanceReportService Reports, FinanceLedgerService Ledger) Services(FinanceEnv env)
    {
        var db = env.T.NewContext();
        var ledger = new FinanceLedgerService(db);
        var budgets = new FinanceBudgetService(db, ledger);
        return (new FinanceReportService(db, ledger, budgets), ledger);
    }

    private static PlanRowSpec Spec(string code, string name, string? parent, BuildingExpenseCategory? expense = null) =>
        new(code, name, parent, LedgerCategoryType.Expense, null, true, null, expense, null);

    [SqlServerFact]
    public async Task ConElPlanGenerico_ElGastoSinRubroCaeEnLaCuentaPorDefectoDeSuCategoria()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.SeedTemplate();
        env.Setup();
        var electricidad = env.Cat("5.04.01");
        env.AddExpense(null, 200_000m, BuildingExpenseCategory.Ande);                       // sin rubro: la cuenta por defecto de ANDE
        env.AddExpense(electricidad.Id, 70_000m, BuildingExpenseCategory.Maintenance);      // eligio la cuenta

        var (reports, ledger) = Services(env);
        var ctx = (await ledger.LoadContextAsync(env.Building.Id, default))!;
        var flow = (await reports.CashFlowAsync(ctx, null, default)).Dto!;

        var totals = flow.OutLines.ToDictionary(l => l.Code, l => l.Total);
        Assert.Equal(200_000m, totals["5.02.01"]);
        Assert.Equal(70_000m, totals["5.04.01"]);
        Assert.Equal("Servicios públicos", flow.OutLines.Single(l => l.Code == "5.02.01").GroupName);
    }

    [SqlServerFact]
    public async Task ConUnPlanImportadoSinFunciones_ElGastoSinRubroCaeEnLaPrimeraCuentaDeSuCategoria()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await env.PlanService().ReplaceAsync(env.Building.Id, env.Company.Id,
        [
            Spec("5", "EGRESOS", null),
            Spec("5.1", "Servicios", "5"),
            Spec("5.1.2", "Luz del edificio", "5.1", BuildingExpenseCategory.Ande),
            Spec("5.1.1", "Luz de áreas comunes", "5.1", BuildingExpenseCategory.Ande),
            Spec("5.1.3", "Agua", "5.1", BuildingExpenseCategory.Essap)
        ], default);
        env.Setup();
        env.AddExpense(null, 100_000m, BuildingExpenseCategory.Ande);

        var (reports, ledger) = Services(env);
        var ctx = (await ledger.LoadContextAsync(env.Building.Id, default))!;
        var flow = (await reports.CashFlowAsync(ctx, null, default)).Dto!;

        var line = Assert.Single(flow.OutLines);
        Assert.Equal("5.1.1", line.Code);
        Assert.Equal(100_000m, line.Total);
    }

    [SqlServerFact]
    public async Task SiElPlanNoTieneCuentaParaLaCategoria_ElRenglonSeRotulaYElPresupuestoNoQuedaDeMenos()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await env.PlanService().ReplaceAsync(env.Building.Id, env.Company.Id,
        [
            Spec("5", "EGRESOS", null),
            Spec("5.1", "Servicios", "5"),
            Spec("5.1.1", "Agua", "5.1", BuildingExpenseCategory.Essap)
        ], default);
        env.Setup();
        env.AddExpense(null, 40_000m, BuildingExpenseCategory.Ande);   // el plan no tiene cuenta de ANDE
        var essap = env.Cat("5.1.1");
        env.AddExpense(essap.Id, 10_000m, BuildingExpenseCategory.Essap);
        env.AddBudget(essap.Id, 15_000m);

        var (reports, ledger) = Services(env);
        var ctx = (await ledger.LoadContextAsync(env.Building.Id, default))!;

        var flow = (await reports.CashFlowAsync(ctx, null, default)).Dto!;
        Assert.Equal(50_000m, flow.OutLines.Sum(l => l.Total));
        Assert.Equal("ANDE (sin rubro)", flow.OutLines.Single(l => l.CategoryId is null).Name);

        var vs = (await reports.BudgetVsActualAsync(ctx, null, null, default)).Dto!;
        Assert.Equal(50_000m, vs.ExpenseTotals.MonthActual);
        Assert.Equal(40_000m, vs.ExpenseLines.Single(l => l.CategoryId == Guid.Empty).MonthActual);
        Assert.Equal(10_000m, vs.ExpenseLines.Single(l => l.Code == "5.1.1").MonthActual);
    }

    [SqlServerFact]
    public async Task ElIngresoPropioSinRubroCaeEnLaCuentaPorDefectoDeSuCategoria()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.SeedTemplate();
        env.Setup();
        env.AddIncome(null, 90_000m);   // categoria Otro: cuenta por defecto 4.3.07

        var (reports, ledger) = Services(env);
        var ctx = (await ledger.LoadContextAsync(env.Building.Id, default))!;
        var flow = (await reports.CashFlowAsync(ctx, null, default)).Dto!;
        var line = Assert.Single(flow.InLines);
        Assert.Equal("4.3.07", line.Code);
        Assert.Equal(90_000m, line.Total);
    }

    [SqlServerFact]
    public async Task ReemplazarElPlanContraSqlServer_ConIndicesUnicosYTransaccion()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.SeedTemplate();
        var ande = env.Cat("5.02.01");
        var expense = env.AddExpense(ande.Id, category: BuildingExpenseCategory.Ande);
        env.AddBudget(ande.Id);

        var result = await env.PlanService().ReplaceAsync(env.Building.Id, env.Company.Id, FinanceChartTemplate.Specs, default);

        Assert.Equal(FinanceChartTemplate.Nodes.Count, result.Created);
        Assert.Equal(1, result.UnlinkedExpenses);
        Assert.Equal(1, result.DeletedBudgetLines);
        var db = env.T.NewContext();
        Assert.Null(db.BuildingExpenses.Single(e => e.Id == expense.Id).LedgerCategoryId);
        Assert.Equal(FinanceChartTemplate.Nodes.Count, env.AllCats().Count);

        // Todo o nada: un codigo repetido deshace la operacion entera.
        var broken = new[] { Spec("9", "Uno", null), Spec("9", "Dos", null) };
        await Assert.ThrowsAnyAsync<Exception>(() => env.PlanService().ReplaceAsync(env.Building.Id, env.Company.Id, broken, default));
        Assert.Equal(FinanceChartTemplate.Nodes.Count, env.AllCats().Count);
    }
}
