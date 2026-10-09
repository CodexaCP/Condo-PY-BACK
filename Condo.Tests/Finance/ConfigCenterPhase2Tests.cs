using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Services;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Finance;

/// <summary>
/// Centro de configuracion, fase 2: cierre de periodo. La pantalla del cierre (encender, cerrar en orden, reabrir con motivo) y cada punto
/// que escribe lo que alimenta el libro: gastos, ingresos, gastos recurrentes, clonado de periodo, notas de credito de proveedor,
/// pagos (registrar, aprobar, revertir) y presupuesto.
/// </summary>
public class ConfigCenterPhase2Tests : IDisposable
{
    private readonly FinanceEnv _env = new();
    private readonly DateOnly _start;   // primer dia del mes de arranque de Finanzas (hace dos meses)

    public ConfigCenterPhase2Tests()
    {
        _env.Setup();
        var s = FinancePeriods.Today().AddMonths(-2);
        _start = new DateOnly(s.Year, s.Month, 1);

        // El periodo de prueba del entorno cae en un mes fijo: se lo aparta a un mes lejano para que no choque con los de estas pruebas.
        var period = _env.T.Db.ExpensePeriods.Single(x => x.Id == _env.Period.Id);
        period.Year = 2020; period.Month = 1;
        period.StartDate = new DateOnly(2020, 1, 1); period.EndDate = new DateOnly(2020, 1, 31); period.DueDate = new DateOnly(2020, 2, 10);
        _env.T.Db.SaveChanges();
    }

    public void Dispose() => _env.Dispose();

    // Mes relativo al arranque: 0 = mes de arranque, 1 = el siguiente, 2 = el mes en curso.
    private DateOnly M(int offset) => _start.AddMonths(offset);

    // ── Armado ───────────────────────────────────────────────────────────────

    private BuildingConfigController Api() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new BuildingConfigOverviewService(_env.T.Db, new FinanceModuleGate(_env.T.Db)));

    private MovementRubroResolver Rubros() => new(_env.T.Db, new FinanceModuleGate(_env.T.Db));

    private BuildingExpensesController Expenses() => new(
        _env.T.Db, _env.Access, _env.Tenant, new StubWebHostEnvironment(Path.GetTempPath()), new ConfigurationBuilder().Build(), Rubros());

    private BuildingIncomesController Incomes() => new(_env.T.Db, _env.Access, Rubros());

    private RecurringBuildingExpensesController Recurring() => new(_env.T.Db, _env.Access, Rubros());

    private ExpensePeriodsController Periods() => new(
        _env.T.Db, _env.Access, _env.Tenant, new ExpenseSettlementDistributionService(_env.T.Db), new StubWebHostEnvironment(Path.GetTempPath()),
        new OwnerCreditService(_env.T.Db), new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance),
        NullLogger<ExpensePeriodsController>.Instance);

    private BuildingExpenseCreditNotesController CreditNotes() => new(
        _env.T.Db, _env.Access, _env.Tenant, new OwnerCreditService(_env.T.Db),
        new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));

    private FinanceBudgetController Budget()
    {
        var gate = new FinanceModuleGate(_env.T.Db);
        var ledger = new FinanceLedgerService(_env.T.Db);
        var budgets = new FinanceBudgetService(_env.T.Db, ledger);
        return new FinanceBudgetController(_env.T.Db, _env.Access, _env.Tenant, gate, ledger, budgets, new FinanceReportService(_env.T.Db, ledger, budgets));
    }

    private PaymentsController LegacyPayments() => new(_env.T.Db, _env.Access);

    private OwnerPaymentsController OwnerPayments()
    {
        var credits = new OwnerCreditService(_env.T.Db);
        return new OwnerPaymentsController(
            _env.T.Db, _env.Tenant, _env.Access, credits, new ComprobanteService(_env.T.Db, credits), new InvoiceDraftService(_env.T.Db, _env.Access),
            new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));
    }

    private ExpensePeriod AddPeriod(DateOnly month, ExpensePeriodStatus status = ExpensePeriodStatus.Draft)
    {
        var period = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = month.Year, Month = month.Month, Name = $"{month:MM/yyyy}",
            StartDate = month, EndDate = month.AddMonths(1).AddDays(-1), DueDate = month.AddDays(20), Status = status
        };
        _env.T.Db.ExpensePeriods.Add(period);
        _env.T.Db.SaveChanges();
        return period;
    }

    private BuildingExpense AddExpenseAt(ExpensePeriod period, DateOnly date, decimal amount = 100_000m)
    {
        var expense = new BuildingExpense
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, ExpensePeriodId = period.Id, Category = BuildingExpenseCategory.Other,
            SupplierName = "Proveedor", Description = "Gasto", ExpenseDate = date, Amount = amount
        };
        _env.T.Db.BuildingExpenses.Add(expense);
        _env.T.Db.SaveChanges();
        return expense;
    }

    private BuildingIncome AddIncomeAt(ExpensePeriod period, DateOnly date, BuildingIncomeCategory category = BuildingIncomeCategory.Other, decimal amount = 50_000m)
    {
        var income = new BuildingIncome
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, ExpensePeriodId = period.Id, Category = category,
            Description = "Ingreso", IncomeDate = date, Amount = amount
        };
        _env.T.Db.BuildingIncomes.Add(income);
        _env.T.Db.SaveChanges();
        return income;
    }

    private BuildingExpenseUpsertRequest ExpenseReq(ExpensePeriod period, DateOnly date, decimal amount = 100_000m) => new()
    {
        BuildingId = _env.Building.Id, ExpensePeriodId = period.Id, Category = BuildingExpenseCategory.Other, SupplierName = "Proveedor",
        Description = "Gasto", ExpenseDate = date, Amount = amount
    };

    private BuildingIncomeUpsertRequest IncomeReq(ExpensePeriod period, DateOnly date, BuildingIncomeCategory category = BuildingIncomeCategory.Other, decimal amount = 50_000m) => new()
    {
        BuildingId = _env.Building.Id, ExpensePeriodId = period.Id, Category = category, Description = "Ingreso", IncomeDate = date, Amount = amount
    };

    private void EnableClosing(bool enabled = true)
    {
        _env.T.Db.FinanceSettings.Single(x => x.BuildingId == _env.Building.Id).PeriodClosingEnabled = enabled;
        _env.T.Db.SaveChanges();
    }

    private async Task Close(int offset)
    {
        var month = M(offset);
        FinanceEnv.Ok(await Api().CloseMonth(_env.Building.Id, month.Year, month.Month, default));
    }

    private async Task<PeriodClosingDto> Closing() => FinanceEnv.Ok(await Api().GetClosing(_env.Building.Id, default));

    /// <summary>Enciende el cierre y cierra los meses 0 hasta <paramref name="lastOffset"/> (en orden).</summary>
    private async Task CloseThrough(int lastOffset)
    {
        EnableClosing();
        for (var i = 0; i <= lastOffset; i++) await Close(i);
    }

    // ── Lectura de resultados ────────────────────────────────────────────────

    private static string? ErrorCode(object? value) =>
        value?.GetType().GetProperty("error")?.GetValue(value) as string;

    private static void AssertClosed(IActionResult? result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(409, obj.StatusCode);
        Assert.Equal(FinancePeriodGuard.ClosedCode, ErrorCode(obj.Value));
    }

    private static void AssertClosed<T>(ActionResult<T> result) => AssertClosed(result.Result);

    // Los altas responden 201 (CreatedAtAction): se espera que el alta haya salido bien.
    private static void Created<T>(ActionResult<T> result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(201, obj.StatusCode);
    }

    private static void AssertConflict<T>(ActionResult<T> result, string code)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(409, obj.StatusCode);
        Assert.Equal(code, ErrorCode(obj.Value));
    }

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);

    // ═════════════════════════════════════════════════════════════════════════
    // Pantalla del cierre
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SinFinanzasLaPantallaDelCierreNoEstaDisponible()
    {
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();

        var closing = await Closing();

        Assert.False(closing.FinanceAvailable);
        Assert.Empty(closing.Months);
    }

    [Fact]
    public async Task ListaLosMesesDesdeElArranqueHastaElMesEnCurso()
    {
        var closing = await Closing();

        Assert.True(closing.FinanceAvailable);
        Assert.False(closing.Enabled);
        Assert.Equal(3, closing.Months.Count);
        Assert.Equal([ClosingMonthStatus.Open, ClosingMonthStatus.Open, ClosingMonthStatus.Current], closing.Months.Select(m => m.Status).ToArray());
        Assert.All(closing.Months, m => Assert.False(m.CanClose));                       // cierre apagado
        Assert.Contains("apagado", closing.Months[0].CannotCloseReason);
        Assert.Contains("todavía no terminó", closing.Months[2].CannotCloseReason);
    }

    [Fact]
    public async Task EncendidoSoloSePuedeCerrarElPrimerMesAbierto()
    {
        EnableClosing();

        var closing = await Closing();

        Assert.True(closing.Months[0].CanClose);
        Assert.False(closing.Months[1].CanClose);
        Assert.Contains("en orden", closing.Months[1].CannotCloseReason);
        Assert.False(closing.Months[2].CanClose);
    }

    [Fact]
    public async Task EncenderElCierreQuedaEnElHistorial()
    {
        FinanceEnv.Ok(await Api().SetClosingEnabled(_env.Building.Id, new SetPeriodClosingRequest { Enabled = true }, default));

        Assert.True((await Closing()).Enabled);
        var entry = _env.T.Db.FinanceAuditLogs.Single(x => x.Section == ConfigSectionKeys.Closing);
        Assert.Equal("Enabled", entry.Action);
    }

    [Fact]
    public async Task CerrarUnMesLoMarcaCerradoYQuedaEnElHistorial()
    {
        EnableClosing();

        await Close(0);

        var closing = await Closing();
        Assert.Equal(ClosingMonthStatus.Closed, closing.Months[0].Status);
        Assert.NotNull(closing.Months[0].ClosedAtUtc);
        Assert.True(closing.Months[0].CanReopen);
        Assert.True(closing.Months[1].CanClose);                                         // ahora el primero abierto es el siguiente
        var row = Assert.Single(closing.History);
        Assert.Equal(M(0).Month, row.Month);
        Assert.Null(row.ReopenedAtUtc);
        var entry = _env.T.Db.FinanceAuditLogs.Single(x => x.Action == "Closed");
        Assert.Contains($"{M(0).Month:00}/{M(0).Year}", entry.Summary);
    }

    [Fact]
    public async Task SeCierraEnOrden()
    {
        EnableClosing();

        var result = await Api().CloseMonth(_env.Building.Id, M(1).Year, M(1).Month, default);

        AssertConflict(result, "previous_month_open");
        Assert.Contains($"{M(0).Month:00}/{M(0).Year}", (string)((ObjectResult)result.Result!).Value!.GetType().GetProperty("message")!.GetValue(((ObjectResult)result.Result!).Value)!);
    }

    [Fact]
    public async Task NoSeCierraElMesEnCursoNiUnoFuturoNiUnoAnteriorAlArranque()
    {
        EnableClosing();
        await Close(0);
        await Close(1);

        var current = await Api().CloseMonth(_env.Building.Id, M(2).Year, M(2).Month, default);
        Assert.IsType<BadRequestObjectResult>(current.Result);

        var future = M(3);
        Assert.IsType<BadRequestObjectResult>((await Api().CloseMonth(_env.Building.Id, future.Year, future.Month, default)).Result);

        var before = M(-1);
        Assert.IsType<BadRequestObjectResult>((await Api().CloseMonth(_env.Building.Id, before.Year, before.Month, default)).Result);
    }

    [Fact]
    public async Task NoSeCierraDosVecesElMismoMes()
    {
        EnableClosing();
        await Close(0);

        AssertConflict(await Api().CloseMonth(_env.Building.Id, M(0).Year, M(0).Month, default), "month_already_closed");
    }

    [Fact]
    public async Task ConElCierreApagadoNoSeCierra()
    {
        AssertConflict(await Api().CloseMonth(_env.Building.Id, M(0).Year, M(0).Month, default), "closing_disabled");
    }

    [Fact]
    public async Task NoSeApagaMientrasHayaMesesCerradosYSiDespuesDeReabrirlos()
    {
        await CloseThrough(0);

        AssertConflict(await Api().SetClosingEnabled(_env.Building.Id, new SetPeriodClosingRequest { Enabled = false }, default), "closing_has_closed_months");

        FinanceEnv.Ok(await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = "Corregir un gasto" }, default));
        var after = FinanceEnv.Ok(await Api().SetClosingEnabled(_env.Building.Id, new SetPeriodClosingRequest { Enabled = false }, default));
        Assert.False(after.Enabled);
    }

    [Fact]
    public async Task ReabrirExigeMotivoYLoDejaRegistrado()
    {
        await CloseThrough(0);

        Assert.IsType<BadRequestObjectResult>((await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = "  " }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = new string('x', 501) }, default)).Result);

        var closing = FinanceEnv.Ok(await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = "Falto cargar una factura" }, default));

        Assert.Equal(ClosingMonthStatus.Open, closing.Months[0].Status);
        var row = Assert.Single(closing.History);
        Assert.NotNull(row.ReopenedAtUtc);
        Assert.Equal("Falto cargar una factura", row.ReopenReason);
        Assert.Contains("Falto cargar una factura", _env.T.Db.FinanceAuditLogs.Single(x => x.Action == "Reopened").Summary);
    }

    [Fact]
    public async Task ReabrirUnMesQueNoEstaCerradoResponde404()
    {
        EnableClosing();

        var result = await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = "x" }, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UnMesReabiertoSePuedeVolverACerrarYElHistorialGuardaLasDosFilas()
    {
        await CloseThrough(0);
        FinanceEnv.Ok(await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = "Ajuste" }, default));

        await Close(0);

        var closing = await Closing();
        Assert.Equal(ClosingMonthStatus.Closed, closing.Months[0].Status);
        Assert.Equal(2, closing.History.Count);
        Assert.Single(closing.History, h => h.ReopenedAtUtc != null);
    }

    [Fact]
    public async Task SePuedeReabrirUnMesAnteriorConLosPosterioresCerrados()
    {
        await CloseThrough(1);

        var closing = FinanceEnv.Ok(await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = "Ajuste" }, default));

        Assert.Equal(ClosingMonthStatus.Open, closing.Months[0].Status);
        Assert.Equal(ClosingMonthStatus.Closed, closing.Months[1].Status);
        Assert.True(closing.Months[0].CanClose);
    }

    [Fact]
    public async Task SoloSuperAdminYAdministradorCierranYReabren()
    {
        _env.Access.Buildings.Add(_env.Building.Id);
        EnableClosing();

        foreach (var role in new[] { "CompanyOperator", "BuildingManager" })
        {
            _env.LoginAs(role);
            AssertStatus(await Api().CloseMonth(_env.Building.Id, M(0).Year, M(0).Month, default), 403);
            AssertStatus(await Api().SetClosingEnabled(_env.Building.Id, new SetPeriodClosingRequest { Enabled = false }, default), 403);
            var view = await Closing();                                                     // pero lo ven
            Assert.False(view.CanEdit);
            Assert.False(view.Months[0].CanClose);
        }

        _env.LoginAs("CompanyAdmin");
        await Close(0);
        Assert.Equal(ClosingMonthStatus.Closed, (await Closing()).Months[0].Status);
    }

    [Fact]
    public async Task ElCierreDependeDeQueFinanzasEsteDisponible()
    {
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();

        AssertStatus(await Api().SetClosingEnabled(_env.Building.Id, new SetPeriodClosingRequest { Enabled = true }, default), 403);
        AssertStatus(await Api().CloseMonth(_env.Building.Id, M(0).Year, M(0).Month, default), 403);
    }

    [Fact]
    public async Task SinFechaDeArranqueNoSeEnciendeElCierre()
    {
        var settings = _env.T.Db.FinanceSettings.Single(x => x.BuildingId == _env.Building.Id);
        settings.FinanceStartDate = null;
        _env.T.Db.SaveChanges();

        AssertConflict(await Api().SetClosingEnabled(_env.Building.Id, new SetPeriodClosingRequest { Enabled = true }, default), "finance_setup_incomplete");
    }

    [Fact]
    public async Task ElResumenDelCentroMuestraLaSeccionDelCierre()
    {
        var off = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Closing);
        Assert.Equal(ConfigSectionStatus.Optional, off.Status);
        Assert.Equal("self", off.LinkKind);

        await CloseThrough(0);
        var on = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Closing);
        Assert.Equal(ConfigSectionStatus.Complete, on.Status);
        Assert.Contains(on.Summary, i => i.Label == "Meses cerrados" && i.Value == "1");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // La guarda: que bloquea y que no
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConElCierreApagadoNadaSeBloqueaAunqueHayaUnaFilaDeCierre()
    {
        var period = AddPeriod(M(0));
        _env.T.Db.FinancePeriodClosures.Add(new FinancePeriodClosure
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = M(0).Year, Month = M(0).Month, ClosedByUserId = Guid.NewGuid()
        });
        _env.T.Db.SaveChanges();

        Created(await Expenses().Create(ExpenseReq(period, M(0).AddDays(3)), default));
    }

    [Fact]
    public async Task ConElModuloFinanzasApagadoElCierreNoRige()
    {
        var period = AddPeriod(M(0));
        await CloseThrough(0);
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();

        Created(await Expenses().Create(ExpenseReq(period, M(0).AddDays(3)), default));
    }

    [Fact]
    public async Task UnMesReabiertoVuelveAAdmitirMovimientos()
    {
        var period = AddPeriod(M(0));
        await CloseThrough(0);
        AssertClosed(await Expenses().Create(ExpenseReq(period, M(0).AddDays(3)), default));

        FinanceEnv.Ok(await Api().ReopenMonth(_env.Building.Id, M(0).Year, M(0).Month, new ReopenPeriodRequest { Reason = "Ajuste" }, default));

        Created(await Expenses().Create(ExpenseReq(period, M(0).AddDays(3)), default));
    }

    [Fact]
    public async Task ElMensajeNombraElMesCerradoYDiceDondeReabrirlo()
    {
        var period = AddPeriod(M(0));
        await CloseThrough(0);

        var result = await Expenses().Create(ExpenseReq(period, M(0).AddDays(3)), default);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        var message = (string)obj.Value!.GetType().GetProperty("message")!.GetValue(obj.Value)!;
        Assert.Contains($"{M(0).Month:00}/{M(0).Year}", message);
        Assert.Contains("Período y cierre", message);
    }

    // ── Gastos ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GastoFechadoEnUnMesCerradoNoSeAgrega_PeroEnUnoAbiertoSi()
    {
        var closedPeriod = AddPeriod(M(0));
        var openPeriod = AddPeriod(M(1));
        await CloseThrough(0);

        AssertClosed(await Expenses().Create(ExpenseReq(closedPeriod, M(0).AddDays(4)), default));
        Created(await Expenses().Create(ExpenseReq(openPeriod, M(1).AddDays(4)), default));
    }

    [Fact]
    public async Task GastoDeUnMesCerradoNoSeEditaNiSeElimina()
    {
        var closedPeriod = AddPeriod(M(0));
        var expense = AddExpenseAt(closedPeriod, M(0).AddDays(4));
        await CloseThrough(0);

        AssertClosed(await Expenses().Update(expense.Id, ExpenseReq(closedPeriod, M(0).AddDays(4), 250_000m), default));
        AssertClosed(await Expenses().Delete(expense.Id, default));

        var saved = _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id);
        Assert.False(saved.IsDeleted);
        Assert.Equal(100_000m, saved.Amount);
    }

    [Fact]
    public async Task NoSePuedeMoverUnGastoAbiertoAUnaFechaDeUnMesCerrado()
    {
        var closedPeriod = AddPeriod(M(0));
        var openPeriod = AddPeriod(M(1));
        var expense = AddExpenseAt(openPeriod, M(1).AddDays(4));
        await CloseThrough(0);

        // El destino cae en el periodo del mes cerrado: se rechaza aunque el gasto de origen este abierto.
        var request = ExpenseReq(closedPeriod, M(0).AddDays(4));
        AssertClosed(await Expenses().Update(expense.Id, request, default));
    }

    [Fact]
    public async Task UnGastoAbiertoSigueEditandoseConElCierreEncendido()
    {
        var openPeriod = AddPeriod(M(1));
        var expense = AddExpenseAt(openPeriod, M(1).AddDays(4));
        await CloseThrough(0);

        FinanceEnv.Ok(await Expenses().Update(expense.Id, ExpenseReq(openPeriod, M(1).AddDays(4), 300_000m), default));
        Assert.IsType<NoContentResult>(await Expenses().Delete(expense.Id, default));
    }

    // ── Ingresos ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngresoEnUnMesCerradoNoSeAgregaEditaNiElimina()
    {
        var closedPeriod = AddPeriod(M(0));
        var income = AddIncomeAt(closedPeriod, M(0).AddDays(2));
        await CloseThrough(0);

        AssertClosed(await Incomes().Create(IncomeReq(closedPeriod, M(0).AddDays(5)), default));
        AssertClosed(await Incomes().Update(income.Id, IncomeReq(closedPeriod, M(0).AddDays(2), amount: 99_000m), default));
        AssertClosed(await Incomes().Delete(income.Id, default));
    }

    [Fact]
    public async Task ElArrastreDeSaldoYElFondoOperativoNoSeBloquean_PorqueNoSonPlataDelLibro()
    {
        var closedPeriod = AddPeriod(M(0));
        var balance = AddIncomeAt(closedPeriod, M(0), BuildingIncomeCategory.AccumulatedBalance);
        var fund = AddIncomeAt(closedPeriod, M(0), BuildingIncomeCategory.OperationalFund);
        await CloseThrough(0);

        FinanceEnv.Ok(await Incomes().Update(balance.Id, IncomeReq(closedPeriod, M(0), BuildingIncomeCategory.AccumulatedBalance, 77_000m), default));
        Created(await Incomes().Create(IncomeReq(closedPeriod, M(0), BuildingIncomeCategory.OperationalFund), default));
        Assert.IsType<NoContentResult>(await Incomes().Delete(fund.Id, default));
    }

    // ── Gastos recurrentes y clonado de periodo ──────────────────────────────

    [Fact]
    public async Task AplicarGastosRecurrentesAUnPeriodoDeUnMesCerradoSeRechaza()
    {
        var closedPeriod = AddPeriod(M(0));
        var openPeriod = AddPeriod(M(1));
        _env.T.Db.RecurringBuildingExpenses.Add(new RecurringBuildingExpense
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Category = BuildingExpenseCategory.Other,
            SupplierName = "Proveedor", Description = "Alquiler", Amount = 1_000_000m, IsActive = true
        });
        _env.T.Db.SaveChanges();
        await CloseThrough(0);

        AssertClosed(await Recurring().Apply(new ApplyRecurringExpensesRequest { ExpensePeriodId = closedPeriod.Id }, default));
        FinanceEnv.Ok(await Recurring().Apply(new ApplyRecurringExpensesRequest { ExpensePeriodId = openPeriod.Id }, default));
    }

    [Fact]
    public async Task ClonarUnPeriodoHaciaUnMesCerradoSeRechaza()
    {
        var before = AddPeriod(M(-1));       // el clon caeria en el mes de arranque, que se cierra
        await CloseThrough(0);

        AssertClosed(await Periods().Clone(before.Id, default));
    }

    // ── Notas de credito de proveedor ────────────────────────────────────────

    private static CreateBuildingExpenseCreditNoteRequest NoteReq(DateOnly issued, string numero = "001-001-0000001") => new()
    {
        Numero = numero, IssueDate = issued, Amount = 10_000m, Reason = "Descuento", DocumentUrl = "/uploads/nc.pdf"
    };

    [Fact]
    public async Task NotaDeCreditoSobreUnGastoDeUnMesCerradoSeRechaza_PeroEnUnoAbiertoSi()
    {
        var closedPeriod = AddPeriod(M(0));
        var openPeriod = AddPeriod(M(1));
        var closedExpense = AddExpenseAt(closedPeriod, M(0).AddDays(2), 500_000m);
        var openExpense = AddExpenseAt(openPeriod, M(1).AddDays(2), 500_000m);
        await CloseThrough(0);

        AssertClosed(await CreditNotes().Create(closedExpense.Id, NoteReq(M(1).AddDays(1), "001-001-0000001"), default));
        FinanceEnv.Ok(await CreditNotes().Create(openExpense.Id, NoteReq(M(1).AddDays(1), "001-001-0000002"), default));
    }

    [Fact]
    public async Task AnularUnaNotaDeCreditoDeUnGastoDeUnMesCerradoSeRechaza()
    {
        var closedPeriod = AddPeriod(M(0));
        var expense = AddExpenseAt(closedPeriod, M(0).AddDays(2), 500_000m);
        var note = FinanceEnv.Ok(await CreditNotes().Create(expense.Id, NoteReq(M(0).AddDays(3)), default));   // antes de cerrar
        await CloseThrough(0);

        var result = await CreditNotes().Void(note.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error" }, default);

        AssertClosed(result);
    }

    // ── Pagos ────────────────────────────────────────────────────────────────

    private sealed record PaymentFixture(Unit Unit, ApplicationUser Owner, ExpensePeriod Published, ExpenseCharge Charge);

    private PaymentFixture AddOwnerWithDebt(decimal amount = 100_000m)
    {
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Access.Buildings.Add(_env.Building.Id);
        var unit = _env.T.AddUnit(_env.Building);
        var owner = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.AddOwner(unit, owner);
        var published = AddPeriod(M(1), ExpensePeriodStatus.Published);
        var charge = new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = published.Id, UnitId = unit.Id, ChargeType = ExpenseChargeType.Ordinary,
            Concept = "Expensas", Amount = amount
        };
        _env.T.Db.ExpenseCharges.Add(charge);
        _env.T.Db.SaveChanges();
        return new PaymentFixture(unit, owner, published, charge);
    }

    [Fact]
    public async Task RegistrarUnPagoConFechaEnUnMesCerradoSeRechazaSinDejarRastro()
    {
        var f = AddOwnerWithDebt();
        await CloseThrough(0);

        var result = await OwnerPayments().Register(
            new OwnerPaymentRegisterRequest { OwnerId = f.Owner.Id, PaymentDate = M(0).AddDays(5), Amount = 100_000m }, default);

        AssertClosed(result);
        Assert.Empty(_env.T.NewContext().OwnerPayments.IgnoreQueryFilters().Where(x => x.OwnerId == f.Owner.Id));
        Assert.Empty(_env.T.NewContext().Payments.Where(x => x.UnitId == f.Unit.Id));
    }

    [Fact]
    public async Task AprobarUnPagoConFechaEnUnMesCerradoSeRechazaYQuedaEnRevision()
    {
        var f = AddOwnerWithDebt();
        var ownerPayment = new OwnerPayment
        {
            CompanyId = _env.Company.Id, OwnerId = f.Owner.Id, PaymentDate = M(0).AddDays(5), DeclaredAmount = 100_000m, ReviewedAmount = 100_000m,
            Status = OwnerPaymentStatus.UnderReview, Reference = "PAY-1", Channel = OwnerPaymentChannel.App
        };
        ownerPayment.Units.Add(new OwnerPaymentUnit { CompanyId = _env.Company.Id, UnitId = f.Unit.Id });
        _env.T.Db.OwnerPayments.Add(ownerPayment);
        _env.T.Db.SaveChanges();
        await CloseThrough(0);

        AssertClosed(await OwnerPayments().Approve(ownerPayment.Id, default));

        Assert.Equal(OwnerPaymentStatus.UnderReview, _env.T.NewContext().OwnerPayments.Single(x => x.Id == ownerPayment.Id).Status);
        Assert.Empty(_env.T.NewContext().Payments.Where(x => x.UnitId == f.Unit.Id));
    }

    private (OwnerPayment OwnerPayment, Payment Payment) AddApprovedWebPayment(PaymentFixture f, DateOnly date, string reference = "PAY-9")
    {
        var ownerPayment = new OwnerPayment
        {
            CompanyId = _env.Company.Id, OwnerId = f.Owner.Id, PaymentDate = date, DeclaredAmount = 100_000m, ReviewedAmount = 100_000m,
            Status = OwnerPaymentStatus.Approved, Reference = reference, Channel = OwnerPaymentChannel.Web
        };
        ownerPayment.Units.Add(new OwnerPaymentUnit { CompanyId = _env.Company.Id, UnitId = f.Unit.Id, AllocatedAmount = 100_000m });
        var payment = new Payment
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = f.Published.Id, UnitId = f.Unit.Id, PaymentDate = date, Amount = 100_000m,
            Method = PaymentMethod.BankTransfer, Reference = reference
        };
        _env.T.Db.OwnerPayments.Add(ownerPayment);
        _env.T.Db.Payments.Add(payment);
        _env.T.Db.SaveChanges();
        return (ownerPayment, payment);
    }

    [Fact]
    public async Task RevertirUnPagoDeUnMesCerradoSeRechaza_PeroUnoDeUnMesAbiertoSi()
    {
        var f = AddOwnerWithDebt();
        var inClosedMonth = AddApprovedWebPayment(f, M(0).AddDays(5));
        await CloseThrough(0);

        AssertClosed(await OwnerPayments().Reverse(inClosedMonth.OwnerPayment.Id, new OwnerPaymentReverseRequest { Reason = "Error" }, default));
        Assert.False(_env.T.NewContext().Payments.Single(x => x.Id == inClosedMonth.Payment.Id).IsReversed);

        // Un pago de un mes abierto sigue pudiendose revertir.
        var open = AddApprovedWebPayment(f, M(2), "PAY-10");
        FinanceEnv.Ok(await OwnerPayments().Reverse(open.OwnerPayment.Id, new OwnerPaymentReverseRequest { Reason = "Error" }, default));
        Assert.True(_env.T.NewContext().Payments.Single(x => x.Id == open.Payment.Id).IsReversed);
    }

    [Fact]
    public async Task ElCaminoAntiguoDeReversaTambienRespetaElCierre()
    {
        var f = AddOwnerWithDebt();
        var payment = new Payment
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = f.Published.Id, UnitId = f.Unit.Id, PaymentDate = M(0).AddDays(5), Amount = 100_000m,
            Method = PaymentMethod.Cash, Reference = "VIEJO-1"
        };
        _env.T.Db.Payments.Add(payment);
        _env.T.Db.SaveChanges();
        await CloseThrough(0);

        AssertClosed(await LegacyPayments().Delete(payment.Id, default));
        Assert.False(_env.T.NewContext().Payments.Single(x => x.Id == payment.Id).IsReversed);
    }

    [Fact]
    public async Task ElCaminoAntiguoDeReversaFuncionaEnUnMesAbierto()
    {
        var f = AddOwnerWithDebt();
        var payment = new Payment
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = f.Published.Id, UnitId = f.Unit.Id, PaymentDate = M(2), Amount = 100_000m,
            Method = PaymentMethod.Cash, Reference = "VIEJO-2"
        };
        _env.T.Db.Payments.Add(payment);
        _env.T.Db.SaveChanges();
        await CloseThrough(0);

        Assert.IsType<NoContentResult>(await LegacyPayments().Delete(payment.Id, default));
    }

    // ── Presupuesto ──────────────────────────────────────────────────────────

    private (Guid CategoryId, int FiscalYear) BudgetTarget()
    {
        _env.SeedTemplate();
        var all = _env.AllCats();
        var category = FinanceBudgetCalculator.BudgetableCategories(all).Where(c => c.Type == LedgerCategoryType.Expense && c.IsActive).First();
        return (category.Id, FinancePeriods.FiscalYearOf(M(0), 1));
    }

    [Fact]
    public async Task CambiarElPresupuestoDeUnMesCerradoSeRechaza_PeroUnMesAbiertoSi()
    {
        var (categoryId, fiscalYear) = BudgetTarget();
        await CloseThrough(0);

        var closedCell = new FinanceBudgetCellDto { CategoryId = categoryId, Year = M(0).Year, Month = M(0).Month, Amount = 500_000m };
        var openCell = new FinanceBudgetCellDto { CategoryId = categoryId, Year = M(1).Year, Month = M(1).Month, Amount = 500_000m };

        AssertClosed(await Budget().UpdateBudget(_env.Building.Id, fiscalYear, new FinanceBudgetUpdateRequest { Cells = [closedCell] }, default));
        Assert.Empty(_env.T.NewContext().BudgetLines.Where(x => x.Year == M(0).Year && x.Month == M(0).Month));

        FinanceEnv.Ok(await Budget().UpdateBudget(_env.Building.Id, fiscalYear, new FinanceBudgetUpdateRequest { Cells = [openCell] }, default));
    }

    [Fact]
    public async Task UnaCeldaDeUnMesCerradoSinCambioDeImporteSeAcepta()
    {
        var (categoryId, fiscalYear) = BudgetTarget();
        _env.T.Db.BudgetLines.Add(new BudgetLine { CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, CategoryId = categoryId, Year = M(0).Year, Month = M(0).Month, Amount = 300_000m });
        _env.T.Db.SaveChanges();
        await CloseThrough(0);

        var same = new FinanceBudgetCellDto { CategoryId = categoryId, Year = M(0).Year, Month = M(0).Month, Amount = 300_000m };
        var other = new FinanceBudgetCellDto { CategoryId = categoryId, Year = M(1).Year, Month = M(1).Month, Amount = 100_000m };

        FinanceEnv.Ok(await Budget().UpdateBudget(_env.Building.Id, fiscalYear, new FinanceBudgetUpdateRequest { Cells = [same, other] }, default));
    }

    [Fact]
    public async Task CopiarElEjercicioAnteriorNoTocaLosMesesCerrados()
    {
        var (categoryId, fiscalYear) = BudgetTarget();
        // Presupuesto del ejercicio anterior en los mismos meses que M(0) y M(1).
        foreach (var offset in new[] { 0, 1 })
        {
            var month = M(offset).AddYears(-1);
            _env.T.Db.BudgetLines.Add(new BudgetLine { CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, CategoryId = categoryId, Year = month.Year, Month = month.Month, Amount = 200_000m });
        }
        _env.T.Db.SaveChanges();
        await CloseThrough(0);

        FinanceEnv.Ok(await Budget().CopyPrevious(_env.Building.Id, fiscalYear, false, default));

        var db = _env.T.NewContext();
        Assert.DoesNotContain(db.BudgetLines, x => x.Year == M(0).Year && x.Month == M(0).Month);          // el mes cerrado no se copio
        Assert.Contains(db.BudgetLines, x => x.Year == M(1).Year && x.Month == M(1).Month && x.Amount == 200_000m);
    }

    // ── Cosas que cambiarian los numeros de los meses cerrados ───────────────

    [Fact]
    public async Task ConMesesCerradosNoSeCambiaLaFechaDeArranqueNiElEjercicio()
    {
        await CloseThrough(0);
        var api = new FinanceController(_env.T.Db, _env.Access, _env.Tenant, new FinanceModuleGate(_env.T.Db));
        var current = _env.T.Db.FinanceSettings.Single(x => x.BuildingId == _env.Building.Id);

        AssertClosed(await api.UpdateSettings(_env.Building.Id,
            new FinanceSettingsUpdateRequest { FinanceStartDate = current.FinanceStartDate!.Value.AddMonths(-1), FiscalYearStartMonth = current.FiscalYearStartMonth }, default));
        AssertClosed(await api.UpdateSettings(_env.Building.Id,
            new FinanceSettingsUpdateRequest { FinanceStartDate = current.FinanceStartDate, FiscalYearStartMonth = current.FiscalYearStartMonth == 12 ? 1 : current.FiscalYearStartMonth + 1 }, default));

        // Mandar lo mismo que ya hay no es un cambio.
        FinanceEnv.Ok(await api.UpdateSettings(_env.Building.Id,
            new FinanceSettingsUpdateRequest { FinanceStartDate = current.FinanceStartDate, FiscalYearStartMonth = current.FiscalYearStartMonth }, default));
    }

    [Fact]
    public async Task ConMesesCerradosNoSeCambiaElSaldoInicialNiSeEliminaUnaCuenta()
    {
        var bank = _env.T.Db.FinancialAccounts.Single(x => x.BuildingId == _env.Building.Id);
        await CloseThrough(0);
        var api = new FinanceAccountsController(_env.T.Db, _env.Access, _env.Tenant, new FinanceModuleGate(_env.T.Db));

        var changeBalance = new FinancialAccountUpsertRequest { BuildingId = _env.Building.Id, Name = bank.Name, Type = bank.Type, OpeningBalance = bank.OpeningBalance + 1, IsActive = true };
        AssertClosed(await api.Update(bank.Id, changeBalance, default));
        AssertClosed(await api.Delete(bank.Id, default));

        // Renombrar si se puede: no cambia ningun numero.
        var rename = new FinancialAccountUpsertRequest { BuildingId = _env.Building.Id, Name = "Banco renombrado", Type = bank.Type, OpeningBalance = bank.OpeningBalance, IsActive = true };
        FinanceEnv.Ok(await api.Update(bank.Id, rename, default));
    }

    [Fact]
    public async Task ConMesesCerradosNoSeReemplazaElPlanDeCuentas_PeroSiSeAgregaLoQueFalta()
    {
        _env.SeedTemplate();
        await CloseThrough(0);
        var api = _env.Controller();

        AssertClosed(await api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace, ConfirmReplace = true }, default));
        FinanceEnv.Ok(await api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.AddMissing }, default));
    }

    [Fact]
    public async Task SinMesesCerradosTodoSeCambiaComoSiempre()
    {
        EnableClosing();                       // encendido pero sin cerrar ningun mes
        var api = new FinanceAccountsController(_env.T.Db, _env.Access, _env.Tenant, new FinanceModuleGate(_env.T.Db));
        var bank = _env.T.Db.FinancialAccounts.Single(x => x.BuildingId == _env.Building.Id);

        FinanceEnv.Ok(await api.Update(bank.Id,
            new FinancialAccountUpsertRequest { BuildingId = _env.Building.Id, Name = bank.Name, Type = bank.Type, OpeningBalance = bank.OpeningBalance + 5, IsActive = true }, default));
    }

    [Fact]
    public async Task LaGuardaNoMezclaEdificios()
    {
        var other = _env.T.AddBuilding(_env.Company, "Otro edificio");
        await CloseThrough(0);

        var guard = new FinancePeriodGuard(_env.T.Db);

        Assert.NotNull(await guard.FindClosedAsync(_env.Building.Id, M(0).AddDays(3), default));
        Assert.Null(await guard.FindClosedAsync(other.Id, M(0).AddDays(3), default));
    }

    [Fact]
    public async Task LaGuardaDevuelveElMesCerradoMasAntiguoDeLasFechasPedidas()
    {
        await CloseThrough(1);
        var guard = new FinancePeriodGuard(_env.T.Db);

        var found = await guard.FindClosedAsync(_env.Building.Id, [M(1).AddDays(2), M(2).AddDays(1), M(0).AddDays(9)], default);

        Assert.Equal(new ClosedMonth(M(0).Year, M(0).Month), found);
        Assert.Null(await guard.FindClosedAsync(_env.Building.Id, [M(2).AddDays(1)], default));
    }
}
