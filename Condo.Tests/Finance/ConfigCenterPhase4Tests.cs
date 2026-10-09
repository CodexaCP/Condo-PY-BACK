using ClosedXML.Excel;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Finance;

/// <summary>
/// Centro de configuracion, fase 4: proveedores de la empresa, cuentas por pagar (la factura con vencimiento entra a la caja al pagarse),
/// IVA de las compras (por cuenta y por gasto) y libro de compras.
/// </summary>
public class ConfigCenterPhase4Tests : IDisposable
{
    private readonly FinanceEnv _env = new();

    public ConfigCenterPhase4Tests()
    {
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Tenant.UserId = _env.Admin.Id;
        _env.Access.Buildings.Add(_env.Building.Id);
    }

    public void Dispose() => _env.Dispose();

    // ── Armado ───────────────────────────────────────────────────────────────

    private static DateOnly Today => FinancePeriods.Today();

    private MovementRubroResolver Rubros() => new(_env.T.Db, new FinanceModuleGate(_env.T.Db));

    private BuildingExpensesController Expenses() => new(
        _env.T.Db, _env.Access, _env.Tenant, new StubWebHostEnvironment(Path.GetTempPath()), new ConfigurationBuilder().Build(), Rubros());

    // Los proveedores son de la empresa del usuario: el SuperAdmin del entorno actua como Administrador de la empresa (sin empresa propia no ve ninguna).
    private SuppliersController Suppliers() => new(_env.T.Db, new FakeTenantContext
    {
        Role = string.Equals(_env.Tenant.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase) ? "CompanyAdmin" : _env.Tenant.Role,
        CompanyId = _env.Company.Id, UserId = _env.Admin.Id
    });

    // Un RUC con digito verificador valido a partir de su base.
    private static string Ruc(string baseNumber = "80019876") => $"{baseNumber}-{BuildingProfile.ComputeRucCheckDigit(baseNumber)}";

    private BuildingConfigPoliciesController Policies() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));

    private BuildingConfigController Config() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new BuildingConfigOverviewService(_env.T.Db, new FinanceModuleGate(_env.T.Db)));

    private FinancePayablesController Payables() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new FinanceModuleGate(_env.T.Db), new FinancePayablesService(_env.T.Db));

    private FinanceVatController Vat() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new FinanceModuleGate(_env.T.Db), new FinanceVatService(_env.T.Db));

    private ExpensePeriod Period(int month = 6, int year = 2026)
    {
        // El periodo de prueba del entorno es de octubre de 2026; para otros meses se crea uno propio.
        var existing = _env.T.Db.ExpensePeriods.FirstOrDefault(x => x.BuildingId == _env.Building.Id && x.Year == year && x.Month == month);
        if (existing is not null) return existing;
        var first = new DateOnly(year, month, 1);
        var period = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = year, Month = month, Name = $"{month:00}/{year}",
            StartDate = first, EndDate = first.AddMonths(1).AddDays(-1), DueDate = first.AddDays(20), Status = ExpensePeriodStatus.Draft
        };
        _env.T.Db.ExpensePeriods.Add(period);
        _env.T.Db.SaveChanges();
        return period;
    }

    // Un gasto cargado de entrada en un periodo en borrador de un mes pasado (las fechas del gasto tienen que caer dentro del periodo).
    private (ExpensePeriod Period, DateOnly Date) PastMonth(int monthsAgo = 1)
    {
        var d = Today.AddMonths(-monthsAgo);
        var period = Period(d.Month, d.Year);
        return (period, new DateOnly(d.Year, d.Month, 10));
    }

    private BuildingExpenseUpsertRequest Req(ExpensePeriod period, DateOnly date, decimal amount = 110_000m, Action<BuildingExpenseUpsertRequest>? tweak = null)
    {
        var request = new BuildingExpenseUpsertRequest
        {
            BuildingId = _env.Building.Id, ExpensePeriodId = period.Id, Category = BuildingExpenseCategory.Other, SupplierName = "Proveedor libre",
            Description = "Factura", ExpenseDate = date, Amount = amount
        };
        tweak?.Invoke(request);
        return request;
    }

    private async Task<BuildingExpenseDto> CreateExpense(BuildingExpenseUpsertRequest request)
    {
        var result = await Expenses().Create(request, default);
        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.True(obj.StatusCode is 200 or 201, $"Se esperaba 201 y llego {FinanceEnv.Describe(result.Result)}.");
        return (BuildingExpenseDto)obj.Value!;
    }

    private static string BadRequestText<T>(ActionResult<T> result) => FinanceEnv.BadRequestText(result);

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);

    private Supplier AddSupplier(string name = "Ferretería Central", string? ruc = null, bool active = true, Company? company = null)
    {
        var supplier = new Supplier { CompanyId = (company ?? _env.Company).Id, Name = name, Ruc = ruc, IsActive = active };
        _env.T.Db.Suppliers.Add(supplier);
        _env.T.Db.SaveChanges();
        return supplier;
    }

    private BuildingExpense AddExpenseRow(ExpensePeriod period, DateOnly date, decimal amount = 100_000m, DateOnly? due = null, DateOnly? paidAt = null,
        decimal? vatRate = null, Guid? supplierId = null, string supplierName = "Proveedor", Guid? paidFrom = null, Guid? categoryId = null)
    {
        var expense = new BuildingExpense
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, ExpensePeriodId = period.Id, Category = BuildingExpenseCategory.Other,
            SupplierName = supplierName, SupplierId = supplierId, Description = "Gasto", ExpenseDate = date, Amount = amount, DueDate = due,
            PaidAt = paidAt, VatRate = vatRate, PaidFromAccountId = paidFrom, LedgerCategoryId = categoryId
        };
        _env.T.Db.BuildingExpenses.Add(expense);
        _env.T.Db.SaveChanges();
        return expense;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // IVA: la cuenta del IVA incluido
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(110_000, 10, 10_000, 100_000)]
    [InlineData(105_000, 5, 5_000, 100_000)]
    [InlineData(80_000, 0, 0, 80_000)]
    [InlineData(1_000_000, 10, 90_909.09, 909_090.91)]
    public void ElIvaEstaIncluidoEnElMonto(decimal total, decimal rate, decimal vat, decimal @base)
    {
        Assert.Equal(vat, VatMath.VatOf(total, rate));
        Assert.Equal(@base, VatMath.BaseOf(total, rate));
    }

    [Fact]
    public void SinTasaNoHayIvaYLasTasasSoloSonDiezCincoOCero()
    {
        Assert.Null(VatMath.VatOf(100m, null));
        Assert.Null(VatMath.BaseOf(100m, null));
        Assert.True(VatMath.IsValidRate(10m) && VatMath.IsValidRate(5m) && VatMath.IsValidRate(0m));
        Assert.False(VatMath.IsValidRate(7m));
        Assert.Equal(10m, VatMath.RateOf(VatTreatment.Vat10));
        Assert.Equal(5m, VatMath.RateOf(VatTreatment.Vat5));
        Assert.Equal(0m, VatMath.RateOf(VatTreatment.Exempt));
        Assert.Null(VatMath.RateOf(VatTreatment.NotApplicable));
        Assert.Null(VatMath.RateOf(null));
    }

    [Fact]
    public void ElIvaDeUnaNotaDeCreditoEsNegativo()
    {
        Assert.Equal(-1_000m, VatMath.VatOf(-11_000m, 10m));
    }

    [Fact]
    public void ElEstadoDeLaCuentaPorPagarSaleDeLasFechas()
    {
        var today = new DateOnly(2026, 6, 15);
        Assert.Equal(ExpensePayables.None, ExpensePayables.StatusOf(null, null, today));
        Assert.Equal(ExpensePayables.Pending, ExpensePayables.StatusOf(new DateOnly(2026, 6, 15), null, today));
        Assert.Equal(ExpensePayables.Overdue, ExpensePayables.StatusOf(new DateOnly(2026, 6, 14), null, today));
        Assert.Equal(ExpensePayables.Paid, ExpensePayables.StatusOf(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10), today));
        Assert.Equal(ExpensePayables.Paid, ExpensePayables.StatusOf(null, new DateOnly(2026, 6, 10), today));
        Assert.True(ExpensePayables.IsUnpaid(new DateOnly(2026, 6, 20), null));
        Assert.False(ExpensePayables.IsUnpaid(null, null));
        Assert.False(ExpensePayables.IsUnpaid(new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 21)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Gastos: proveedor, factura, vencimiento, pago e IVA
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnGastoSinNadaNuevoSeCargaComoSiempre_YNoEsUnaCuentaPorPagar()
    {
        var (period, date) = PastMonth();

        var dto = await CreateExpense(Req(period, date));

        Assert.Null(dto.SupplierId);
        Assert.Equal("Proveedor libre", dto.SupplierName);
        Assert.Null(dto.DueDate);
        Assert.Null(dto.PaidAt);
        Assert.Equal(ExpensePayables.None, dto.PayableStatus);
        Assert.Null(dto.VatRate);
        Assert.Null(dto.VatAmount);
    }

    [Fact]
    public async Task ElProveedorElegidoPonePorNombreEnElGasto()
    {
        var (period, date) = PastMonth();
        var supplier = AddSupplier("Ascensores del Sur");

        var dto = await CreateExpense(Req(period, date, tweak: r => { r.SupplierId = supplier.Id; r.SupplierName = "ignorado"; }));

        Assert.Equal(supplier.Id, dto.SupplierId);
        Assert.Equal("Ascensores del Sur", dto.SupplierName);
    }

    [Fact]
    public async Task UnProveedorAjenoInexistenteODesactivadoSeRechaza_PeroElQueElGastoYaTeniaSeConserva()
    {
        var (period, date) = PastMonth();
        var other = _env.T.AddCompany("Otra empresa");
        var foreign = AddSupplier("De otra empresa", company: other);
        var inactive = AddSupplier("Desactivado", active: false);

        AssertStatus(await Expenses().Create(Req(period, date, tweak: r => r.SupplierId = foreign.Id), default), 400);
        AssertStatus(await Expenses().Create(Req(period, date, tweak: r => r.SupplierId = Guid.NewGuid()), default), 400);
        Assert.Contains("desactivado", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => r.SupplierId = inactive.Id), default)));

        // Un gasto que ya tenia el proveedor lo conserva aunque se haya desactivado despues.
        var active = AddSupplier("Luego se desactiva");
        var dto = await CreateExpense(Req(period, date, tweak: r => r.SupplierId = active.Id));
        _env.T.Db.Suppliers.Single(x => x.Id == active.Id).IsActive = false;
        _env.T.Db.SaveChanges();
        var updated = FinanceEnv.Ok(await Expenses().Update(dto.Id, Req(period, date, 120_000m, r => r.SupplierId = active.Id), default));
        Assert.Equal(active.Id, updated.SupplierId);
    }

    [Fact]
    public async Task LaFacturaDelProveedorGuardaNumeroYTimbrado()
    {
        var (period, date) = PastMonth();

        var dto = await CreateExpense(Req(period, date, tweak: r => { r.InvoiceNumber = " 001-001-0000123 "; r.InvoiceTimbrado = "12345678"; }));

        Assert.Equal("001-001-0000123", dto.InvoiceNumber);
        Assert.Equal("12345678", dto.InvoiceTimbrado);
        AssertStatus(await Expenses().Create(Req(period, date, tweak: r => r.InvoiceNumber = new string('1', 51)), default), 400);
        AssertStatus(await Expenses().Create(Req(period, date, tweak: r => r.InvoiceTimbrado = new string('1', 21)), default), 400);
    }

    [Fact]
    public async Task ConVencimientoElGastoQuedaAPagarYSePuedeCargarYaPagado()
    {
        var (period, date) = PastMonth();

        var pending = await CreateExpense(Req(period, date, tweak: r => r.DueDate = Today.AddDays(10)));
        var overdue = await CreateExpense(Req(period, date, tweak: r => r.DueDate = Today.AddDays(-2)));
        var paid = await CreateExpense(Req(period, date, tweak: r => { r.DueDate = Today.AddDays(5); r.PaidAt = Today; }));

        Assert.Equal(ExpensePayables.Pending, pending.PayableStatus);
        Assert.Equal(ExpensePayables.Overdue, overdue.PayableStatus);
        Assert.Equal(ExpensePayables.Paid, paid.PayableStatus);
        Assert.Equal(Today, paid.PaidAt);
    }

    [Fact]
    public async Task LasFechasDeVencimientoYPagoSeValidan()
    {
        var (period, date) = PastMonth();

        Assert.Contains("vencimiento", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => r.DueDate = date.AddDays(-1)), default)));
        Assert.Contains("vencimiento", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => r.DueDate = date.AddYears(6)), default)));
        Assert.Contains("futura", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => r.PaidAt = Today.AddDays(1)), default)));
        Assert.Contains("anterior", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => r.PaidAt = date.AddDays(-1)), default)));
    }

    [Fact]
    public async Task LaCuentaDePagoExigeFechaDePagoYSerDelEdificioYEstarActiva()
    {
        var bank = _env.Setup();
        var (period, date) = PastMonth();
        var otherBuilding = _env.T.AddBuilding(_env.Company, "Otro edificio");
        var foreignAccount = new FinancialAccount { CompanyId = _env.Company.Id, BuildingId = otherBuilding.Id, Name = "Banco ajeno", Type = FinancialAccountType.Bank };
        var inactive = new FinancialAccount { CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Name = "Banco viejo", Type = FinancialAccountType.Bank, IsActive = false };
        _env.T.Db.FinancialAccounts.AddRange(foreignAccount, inactive);
        _env.T.Db.SaveChanges();

        Assert.Contains("fecha de pago", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => r.PaidFromAccountId = bank.Id), default)));
        AssertStatus(await Expenses().Create(Req(period, date, tweak: r => { r.PaidAt = Today; r.PaidFromAccountId = foreignAccount.Id; }), default), 400);
        Assert.Contains("desactivada", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => { r.PaidAt = Today; r.PaidFromAccountId = inactive.Id; }), default)));

        var ok = await CreateExpense(Req(period, date, tweak: r => { r.PaidAt = Today; r.PaidFromAccountId = bank.Id; }));
        Assert.Equal(bank.Id, ok.PaidFromAccountId);
        Assert.Equal(bank.Name, FinanceEnv.Ok(await Expenses().GetById(ok.Id, default)).PaidFromAccountName);
    }

    [Fact]
    public async Task ElIvaTomaLaTasaPedidaOLaDelRubro_YSoloAceptaDiezCincoOCero()
    {
        _env.Setup();
        _env.SeedTemplate();
        var (period, date) = PastMonth();
        var leaf = FinanceBudgetCalculator.BudgetableCategories(_env.AllCats()).First(c => c.Type == LedgerCategoryType.Expense && c.IsActive);
        var db = _env.T.Db.LedgerCategories.Single(x => x.Id == leaf.Id);
        db.VatTreatment = VatTreatment.Vat5;
        _env.T.Db.SaveChanges();

        var byRubro = await CreateExpense(Req(period, date, 105_000m, r => r.LedgerCategoryId = leaf.Id));
        Assert.Equal(5m, byRubro.VatRate);
        Assert.Equal(5_000m, byRubro.VatAmount);

        var explicitRate = await CreateExpense(Req(period, date, 110_000m, r => { r.LedgerCategoryId = leaf.Id; r.VatRate = 10m; }));
        Assert.Equal(10m, explicitRate.VatRate);
        Assert.Equal(10_000m, explicitRate.VatAmount);

        var exempt = await CreateExpense(Req(period, date, 50_000m, r => { r.LedgerCategoryId = leaf.Id; r.VatRate = 0m; }));
        Assert.Equal(0m, exempt.VatRate);
        Assert.Equal(0m, exempt.VatAmount);

        Assert.Contains("10, 5 o 0", BadRequestText(await Expenses().Create(Req(period, date, tweak: r => r.VatRate = 7m), default)));

        db.VatTreatment = VatTreatment.NotApplicable;                          // no corresponde: el gasto queda sin tasa
        _env.T.Db.SaveChanges();
        Assert.Null((await CreateExpense(Req(period, date, tweak: r => r.LedgerCategoryId = leaf.Id))).VatRate);
    }

    [Fact]
    public async Task ElIvaAcompanaAlMontoVigenteCuandoUnaNotaDeCreditoBajaElGasto()
    {
        var (period, date) = PastMonth();
        var expense = AddExpenseRow(period, date, 110_000m, vatRate: 10m);
        expense.OriginalAmount = 220_000m;                                    // el proveedor facturo 220.000 y una nota de credito lo bajo a 110.000
        _env.T.Db.SaveChanges();

        var dto = FinanceEnv.Ok(await Expenses().GetById(expense.Id, default));

        Assert.Equal(10_000m, dto.VatAmount);                                 // IVA del monto neto vigente
    }

    [Fact]
    public async Task EditarUnGastoActualizaProveedorVencimientoEIva()
    {
        var (period, date) = PastMonth();
        var dto = await CreateExpense(Req(period, date, tweak: r => r.DueDate = Today.AddDays(20)));
        var supplier = AddSupplier();

        var updated = FinanceEnv.Ok(await Expenses().Update(dto.Id, Req(period, date, 110_000m, r =>
        {
            r.SupplierId = supplier.Id;
            r.DueDate = Today.AddDays(30);
            r.VatRate = 10m;
            r.InvoiceNumber = "001-002-0000009";
        }), default));

        Assert.Equal(supplier.Id, updated.SupplierId);
        Assert.Equal(Today.AddDays(30), updated.DueDate);
        Assert.Equal(10m, updated.VatRate);
        Assert.Equal("001-002-0000009", updated.InvoiceNumber);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Pagar una factura de proveedor
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RegistrarElPagoDejaLaFacturaPagadaConSuFechaYCuenta()
    {
        var bank = _env.Setup();
        var (period, date) = PastMonth();
        var expense = AddExpenseRow(period, date, due: Today.AddDays(5));

        var dto = FinanceEnv.Ok(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = Today, AccountId = bank.Id }, default));

        Assert.Equal(Today, dto.PaidAt);
        Assert.Equal(bank.Id, dto.PaidFromAccountId);
        Assert.Equal(ExpensePayables.Paid, dto.PayableStatus);
    }

    [Fact]
    public async Task SoloSePuedePagarUnaFacturaConVencimiento()
    {
        var (period, date) = PastMonth();
        var legacy = AddExpenseRow(period, date);                              // sin vencimiento: ya cuenta en su fecha

        var result = await Expenses().RegisterPayment(legacy.Id, new RegisterExpensePaymentRequest { PaidAt = Today }, default);

        Assert.Contains("no es una factura a pagar", BadRequestText(result));
        Assert.Null(_env.T.NewContext().BuildingExpenses.Single(x => x.Id == legacy.Id).PaidAt);
    }

    [Fact]
    public async Task ElPagoSeValidaFechaYCuenta()
    {
        var bank = _env.Setup();
        var (period, date) = PastMonth();
        var expense = AddExpenseRow(period, date, due: Today.AddDays(5));

        Assert.Contains("futura", BadRequestText(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = Today.AddDays(1) }, default)));
        Assert.Contains("anterior", BadRequestText(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = date.AddDays(-1) }, default)));
        AssertStatus(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = Today, AccountId = Guid.NewGuid() }, default), 400);
        FinanceEnv.Ok(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = Today, AccountId = bank.Id }, default));
    }

    [Fact]
    public async Task PagarDeNuevoCambiaLaFechaYDeshacerElPagoLaDejaPendiente()
    {
        var (period, date) = PastMonth();
        var expense = AddExpenseRow(period, date, due: Today.AddDays(5));
        FinanceEnv.Ok(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = Today.AddDays(-1) }, default));

        var repaid = FinanceEnv.Ok(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = Today }, default));
        Assert.Equal(Today, repaid.PaidAt);

        var undone = FinanceEnv.Ok(await Expenses().UndoPayment(expense.Id, default));
        Assert.Null(undone.PaidAt);
        Assert.Null(undone.PaidFromAccountId);
        Assert.Equal(ExpensePayables.Pending, undone.PayableStatus);

        Assert.Contains("no tiene un pago", BadRequestText(await Expenses().UndoPayment(expense.Id, default)));
    }

    [Fact]
    public async Task ElPagoNoDependeDelEstadoDelPeriodo()
    {
        var (period, date) = PastMonth();
        var expense = AddExpenseRow(period, date, due: Today.AddDays(5));
        _env.T.Db.ExpensePeriods.Single(x => x.Id == period.Id).Status = ExpensePeriodStatus.Published;
        _env.T.Db.SaveChanges();

        FinanceEnv.Ok(await Expenses().RegisterPayment(expense.Id, new RegisterExpensePaymentRequest { PaidAt = Today }, default));
    }

    [Fact]
    public async Task SoloLosRolesQueCarganGastosPaganFacturas_YSoloDeSusEdificios()
    {
        var (period, date) = PastMonth();
        var expense = AddExpenseRow(period, date, due: Today.AddDays(5));
        var request = new RegisterExpensePaymentRequest { PaidAt = Today };

        _env.LoginAs("Owner");
        AssertStatus(await Expenses().RegisterPayment(expense.Id, request, default), 403);
        AssertStatus(await Expenses().UndoPayment(expense.Id, default), 403);

        foreach (var role in new[] { "CompanyOperator", "BuildingManager", "CompanyAdmin" })
        {
            _env.LoginAs(role);
            FinanceEnv.Ok(await Expenses().RegisterPayment(expense.Id, request, default));
        }

        _env.LoginAs("CompanyAdmin");
        _env.Access.Buildings.Clear();
        Assert.IsType<ForbidResult>((await Expenses().RegisterPayment(expense.Id, request, default)).Result);
        Assert.IsType<NotFoundResult>((await Expenses().RegisterPayment(Guid.NewGuid(), request, default)).Result);
    }

    [Fact]
    public async Task ElCierreDePeriodoAlcanzaAlPagoYASuDeshacer()
    {
        _env.Setup();
        var start = Today.AddMonths(-2);
        var closedMonth = new DateOnly(start.Year, start.Month, 1);
        var (period, _) = PastMonth(2);
        var expense = AddExpenseRow(period, closedMonth.AddDays(2), due: closedMonth.AddDays(20), paidAt: closedMonth.AddDays(5));
        var open = AddExpenseRow(period, closedMonth.AddDays(1), due: closedMonth.AddDays(25));
        var settings = _env.T.Db.FinanceSettings.Single(x => x.BuildingId == _env.Building.Id);
        settings.PeriodClosingEnabled = true;
        _env.T.Db.FinancePeriodClosures.Add(new FinancePeriodClosure
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = closedMonth.Year, Month = closedMonth.Month, ClosedByUserId = _env.Admin.Id
        });
        _env.T.Db.SaveChanges();

        AssertClosed(await Expenses().UndoPayment(expense.Id, default));                                                  // deshacer un pago de un mes cerrado
        AssertClosed(await Expenses().RegisterPayment(open.Id, new RegisterExpensePaymentRequest { PaidAt = closedMonth.AddDays(7) }, default));
        AssertClosed(await Expenses().Create(Req(period, closedMonth.AddDays(3), tweak: r => r.PaidAt = closedMonth.AddDays(8)), default));
        FinanceEnv.Ok(await Expenses().RegisterPayment(open.Id, new RegisterExpensePaymentRequest { PaidAt = Today }, default));  // un mes abierto si
    }

    private static void AssertClosed<T>(ActionResult<T> result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(409, obj.StatusCode);
        Assert.Equal(FinancePeriodGuard.ClosedCode, obj.Value!.GetType().GetProperty("error")!.GetValue(obj.Value));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Caja: la factura con vencimiento entra al pagarse (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    [SqlServerFact]
    public async Task ElLibroCuentaElGastoSinVencimientoEnSuFecha_ElPendienteNoEntra_ElPagadoEntraEnSuFechaYCuenta()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        var other = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Banco dos", Type = FinancialAccountType.Bank };
        env.T.Db.FinancialAccounts.Add(other);
        env.T.Db.SaveChanges();
        var day = FinanceEnv.Yesterday;

        BuildingExpense Add(decimal amount, DateOnly? due, DateOnly? paidAt, Guid? paidFrom) =>
            AddExpenseTo(env, day.AddDays(-1), amount, due, paidAt, paidFrom);

        Add(100_000m, null, null, null);                                   // sin vencimiento: cuenta en su fecha, como siempre
        Add(200_000m, day.AddDays(10), null, null);                        // a pagar: todavia no entra a la caja
        Add(300_000m, day.AddDays(10), day, other.Id);                     // pagada ayer desde "Banco dos"

        var ledger = new FinanceLedgerService(env.T.NewContext());
        var ctx = (await ledger.LoadContextAsync(env.Building.Id, default))!;
        var rows = await ledger.GetRowsAsync(ctx, day.AddDays(-5), day, default);
        var expenses = rows.Where(r => r.SourceType == LedgerSourceType.BuildingExpense).ToList();

        Assert.Equal(2, expenses.Count);
        Assert.Contains(expenses, r => r.Amount == 100_000m && r.Date == day.AddDays(-1));
        var paid = expenses.Single(r => r.Amount == 300_000m);
        Assert.Equal(day, paid.Date);                                       // en la fecha del pago, no de la factura
        Assert.Equal(other.Id, paid.AccountId);                            // desde la cuenta elegida
        Assert.Contains("factura del", paid.Description);
        Assert.DoesNotContain(expenses, r => r.Amount == 200_000m);

        var balances = await ledger.GetBucketsAsync(ctx, day.AddDays(-5), day, default);
        Assert.Equal(300_000m, balances.Where(b => b.AccountId == other.Id && b.Direction == LedgerDirection.Out).Sum(b => b.Amount));
        Assert.Equal(100_000m, balances.Where(b => b.AccountId != other.Id && b.Direction == LedgerDirection.Out).Sum(b => b.Amount));
        _ = bank;
    }

    [SqlServerFact]
    public async Task AlPagarLaFacturaElGastoPasaAEntrarYAlDeshacerSeVa()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        var day = FinanceEnv.Yesterday;
        var expense = AddExpenseTo(env, day.AddDays(-2), 400_000m, day.AddDays(7), null, null);

        async Task<decimal> OutOnLedger()
        {
            var ledger = new FinanceLedgerService(env.T.NewContext());
            var ctx = (await ledger.LoadContextAsync(env.Building.Id, default))!;
            return (await ledger.GetBucketsAsync(ctx, day.AddDays(-10), day, default)).Where(b => b.Direction == LedgerDirection.Out).Sum(b => b.Amount);
        }

        Assert.Equal(0m, await OutOnLedger());

        var row = env.T.Db.BuildingExpenses.Single(x => x.Id == expense.Id);
        row.PaidAt = day;
        env.T.Db.SaveChanges();
        Assert.Equal(400_000m, await OutOnLedger());

        row.PaidAt = null;
        env.T.Db.SaveChanges();
        Assert.Equal(0m, await OutOnLedger());
    }

    private static BuildingExpense AddExpenseTo(FinanceEnv env, DateOnly date, decimal amount, DateOnly? due, DateOnly? paidAt, Guid? paidFrom)
    {
        var expense = new BuildingExpense
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, ExpensePeriodId = env.Period.Id, Category = BuildingExpenseCategory.Other,
            SupplierName = "Proveedor", Description = "Factura", ExpenseDate = date, Amount = amount, DueDate = due, PaidAt = paidAt, PaidFromAccountId = paidFrom
        };
        env.T.Db.BuildingExpenses.Add(expense);
        env.T.Db.SaveChanges();
        return expense;
    }

    [SqlServerFact]
    public async Task ElTableroSumaCuentasPorPagarYPorCobrar()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        env.SeedTemplate();
        env.Tenant.CompanyId = env.Company.Id;
        var day = FinanceEnv.Yesterday;
        AddExpenseTo(env, day.AddDays(-5), 100_000m, day.AddDays(-1), null, null);       // vencida
        AddExpenseTo(env, day.AddDays(-5), 50_000m, day.AddDays(3), null, null);          // por vencer

        var db = env.T.NewContext();
        var gate = new FinanceModuleGate(db);
        var ledger = new FinanceLedgerService(db);
        var budgets = new FinanceBudgetService(db, ledger);
        var controller = new FinanceLedgerController(db, env.Access, env.Tenant, gate, ledger, budgets, new FinanceReportService(db, ledger, budgets), new FinancePayablesService(db));

        var dashboard = FinanceEnv.Ok(await controller.GetDashboard(env.Building.Id, null, null, default));

        Assert.Equal(150_000m, dashboard.Payables.PendingTotal);
        Assert.Equal(100_000m, dashboard.Payables.OverdueTotal);
        Assert.Equal(0m, dashboard.Receivables.TotalCharged);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Cuentas por pagar y por cobrar (consultas)
    // ═════════════════════════════════════════════════════════════════════════

    private void SeedPayables()
    {
        var (period, date) = PastMonth(3);
        var supplierA = AddSupplier("Proveedor A", "80000001-0".Length > 0 ? null : null);
        var supplierB = AddSupplier("Proveedor B");
        AddExpenseRow(period, date, 100_000m, due: Today.AddDays(-45), supplierId: supplierA.Id, supplierName: supplierA.Name);          // vencida 31 a 60
        AddExpenseRow(period, date, 200_000m, due: Today.AddDays(-5), supplierId: supplierA.Id, supplierName: supplierA.Name);           // vencida 1 a 30
        AddExpenseRow(period, date, 300_000m, due: Today.AddDays(3), supplierId: supplierB.Id, supplierName: supplierB.Name);            // vence en 3 dias
        AddExpenseRow(period, date, 400_000m, due: Today.AddDays(40), supplierId: supplierB.Id, supplierName: supplierB.Name);           // lejos
        AddExpenseRow(period, date, 500_000m, due: Today.AddDays(-10), paidAt: Today.AddDays(-9), supplierId: supplierB.Id);              // ya pagada
        AddExpenseRow(period, date, 999_000m);                                                                                            // sin vencimiento: no es una cuenta por pagar
    }

    [Fact]
    public async Task ElResumenDeCuentasPorPagarSeparaVencidoPorVencerYAntiguedad()
    {
        _env.Setup();
        SeedPayables();

        var summary = FinanceEnv.Ok(await Payables().GetSummary(_env.Building.Id, default));

        Assert.Equal(4, summary.PendingCount);
        Assert.Equal(1_000_000m, summary.PendingTotal);
        Assert.Equal(2, summary.OverdueCount);
        Assert.Equal(300_000m, summary.OverdueTotal);
        Assert.Equal(300_000m, summary.DueNext7DaysTotal);
        Assert.Equal(["1 a 30 días", "31 a 60 días", "61 a 90 días", "Más de 90 días"], summary.Aging.Select(a => a.Label).ToArray());
        Assert.Equal(200_000m, summary.Aging[0].Total);
        Assert.Equal(100_000m, summary.Aging[1].Total);
        Assert.Equal(0m, summary.Aging[2].Total);
    }

    [Fact]
    public async Task ElListadoFiltraPorEstadoProveedorYFechas_YOrdenaLoPendientePrimero()
    {
        _env.Setup();
        SeedPayables();
        var supplierB = _env.T.Db.Suppliers.Single(x => x.Name == "Proveedor B");

        var all = FinanceEnv.Ok(await Payables().GetAll(_env.Building.Id, null, null, null, null, 1, 50, default));
        Assert.Equal(5, all.TotalCount);                                         // el gasto sin vencimiento no figura
        Assert.Equal(PayableStatusOrder(all), all.Items.Select(i => i.Status).ToArray());
        Assert.Equal("Paid", all.Items[^1].Status);                                // lo pagado, al final
        Assert.True(all.Items[0].DueDate <= all.Items[1].DueDate);

        Assert.Equal(4, FinanceEnv.Ok(await Payables().GetAll(_env.Building.Id, "pending", null, null, null, 1, 50, default)).TotalCount);
        var overdue = FinanceEnv.Ok(await Payables().GetAll(_env.Building.Id, "overdue", null, null, null, 1, 50, default));
        Assert.Equal(2, overdue.TotalCount);
        Assert.All(overdue.Items, i => Assert.True(i.DaysOverdue > 0));
        Assert.Equal(1, FinanceEnv.Ok(await Payables().GetAll(_env.Building.Id, "paid", null, null, null, 1, 50, default)).TotalCount);
        Assert.Equal(3, FinanceEnv.Ok(await Payables().GetAll(_env.Building.Id, "all", supplierB.Id, null, null, 1, 50, default)).TotalCount);
        Assert.Equal(3, FinanceEnv.Ok(await Payables().GetAll(_env.Building.Id, "all", null, Today.AddDays(-10), Today.AddDays(5), 1, 50, default)).TotalCount);

        var page2 = FinanceEnv.Ok(await Payables().GetAll(_env.Building.Id, "all", null, null, null, 2, 2, default));
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page2.TotalCount);
    }

    private static string[] PayableStatusOrder(PayablesPageDto page) =>
        page.Items.OrderBy(i => i.Status == "Paid").ThenBy(i => i.DueDate).Select(i => i.Status).ToArray();

    [Fact]
    public async Task ElListadoRechazaFiltrosInvalidosYNoMezclaEdificios()
    {
        _env.Setup();
        SeedPayables();

        Assert.Contains("estado", BadRequestText(await Payables().GetAll(_env.Building.Id, "otro", null, null, null, 1, 50, default)));
        Assert.Contains("desde", BadRequestText(await Payables().GetAll(_env.Building.Id, null, null, Today, Today.AddDays(-1), 1, 50, default)));

        var other = _env.T.AddBuilding(_env.Company, "Otro edificio");
        _env.Access.Buildings.Add(other.Id);
        other.FinanceModuleEnabled = true;
        _env.T.Db.BuildingPlans.Add(new BuildingPlan
        {
            PlanId = _env.T.Db.Plans.Single(p => p.Name == "Plan con finanzas").Id, BuildingId = other.Id, ScopeEntityId = other.Id,
            StartDate = DateTime.UtcNow.AddDays(-10), EndDate = DateTime.UtcNow.AddDays(300), AssignedById = _env.Admin.Id
        });
        _env.T.Db.SaveChanges();
        Assert.Equal(0, FinanceEnv.Ok(await Payables().GetAll(other.Id, null, null, null, null, 1, 50, default)).TotalCount);
    }

    [Fact]
    public async Task LasCuentasPorPagarSonDeConsultaParaLosCuatroRolesYExigenFinanzas()
    {
        _env.Setup();
        foreach (var role in new[] { "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            _env.LoginAs(role);
            Assert.NotNull(FinanceEnv.Ok(await Payables().GetSummary(_env.Building.Id, default)));
        }

        _env.LoginAs("Owner");
        AssertStatus(await Payables().GetSummary(_env.Building.Id, default), 403);

        _env.LoginAs("SuperAdmin");
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();
        AssertStatus(await Payables().GetSummary(_env.Building.Id, default), 403);
    }

    [Fact]
    public async Task LaDeudaDeLosPropietariosSeCalculaPorLoDevengadoSinContarPagosRevertidos()
    {
        var published = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = 2026, Month = 1, Name = "01/2026", StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 28), DueDate = Today.AddDays(-40), Status = ExpensePeriodStatus.Published
        };
        var draft = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = 2026, Month = 2, Name = "02/2026", StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 2, 28), DueDate = Today.AddDays(-5), Status = ExpensePeriodStatus.Draft
        };
        _env.T.Db.ExpensePeriods.AddRange(published, draft);
        var unitA = _env.T.AddUnit(_env.Building);
        var unitB = _env.T.AddUnit(_env.Building);
        var unitC = _env.T.AddUnit(_env.Building);
        foreach (var (unit, amount) in new[] { (unitA, 1_000_000m), (unitB, 1_000_000m), (unitC, 1_000_000m) })
        {
            _env.T.Db.ExpenseCharges.Add(new ExpenseCharge { CompanyId = _env.Company.Id, ExpensePeriodId = published.Id, UnitId = unit.Id, Concept = "Expensa", Amount = amount });
        }

        _env.T.Db.ExpenseCharges.Add(new ExpenseCharge { CompanyId = _env.Company.Id, ExpensePeriodId = draft.Id, UnitId = unitA.Id, Concept = "Borrador", Amount = 777_000m });
        _env.T.Db.Payments.AddRange(
            new Payment { CompanyId = _env.Company.Id, ExpensePeriodId = published.Id, UnitId = unitA.Id, PaymentDate = Today, Amount = 1_000_000m, Method = PaymentMethod.Cash, Reference = "A" },
            new Payment { CompanyId = _env.Company.Id, ExpensePeriodId = published.Id, UnitId = unitB.Id, PaymentDate = Today, Amount = 400_000m, Method = PaymentMethod.Cash, Reference = "B" },
            new Payment { CompanyId = _env.Company.Id, ExpensePeriodId = published.Id, UnitId = unitC.Id, PaymentDate = Today, Amount = 1_000_000m, Method = PaymentMethod.Cash, Reference = "C", IsReversed = true });
        _env.T.Db.SaveChanges();

        var summary = await new FinancePayablesService(_env.T.Db).ReceivablesAsync(_env.Building.Id, Today, default);

        Assert.Equal(3_000_000m, summary.TotalCharged);                   // el borrador no cuenta
        Assert.Equal(1_400_000m, summary.TotalCollected);                 // el pago revertido tampoco
        Assert.Equal(1_600_000m, summary.TotalPending);
        Assert.Equal(46.7m, summary.CollectionRatePercentage);
        Assert.Equal(1_600_000m, summary.OverdueAmount);                  // todo vencido hace 40 dias
        Assert.Equal(2, summary.OverdueUnits);
        Assert.Equal(1_600_000m, summary.Aging[1].Total);                 // 31 a 60 dias
    }

    [Fact]
    public async Task SinPeriodosPublicadosLaDeudaEsCero()
    {
        var summary = await new FinancePayablesService(_env.T.Db).ReceivablesAsync(_env.Building.Id, Today, default);

        Assert.Equal(0m, summary.TotalCharged);
        Assert.Equal(0m, summary.CollectionRatePercentage);
        Assert.Equal(4, summary.Aging.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Proveedores
    // ═════════════════════════════════════════════════════════════════════════

    private static SupplierUpsertRequest SupplierReq(string name = "Ferretería Central", string? ruc = null) => new() { Name = name, Ruc = ruc };

    [Fact]
    public async Task SeCreaUnProveedorYQuedaConSusDatosNormalizados()
    {
        var created = Assert.IsAssignableFrom<ObjectResult>((await Suppliers().Create(new SupplierUpsertRequest
        {
            Name = "  Ferretería Central  ", Ruc = " " + Ruc() + " ", Phone = " 021 555 123 ", Email = " Ventas@Ferreteria.COM ", Address = "Av. España 123", PaymentTermDays = 30
        }, default)).Result);
        var dto = (SupplierDto)created.Value!;

        Assert.Equal(201, created.StatusCode);
        Assert.Equal("Ferretería Central", dto.Name);
        Assert.Equal(Ruc(), dto.Ruc);
        Assert.Equal("021 555 123", dto.Phone);
        Assert.Equal("ventas@ferreteria.com", dto.Email);
        Assert.Equal(30, dto.PaymentTermDays);
        Assert.True(dto.IsActive);
        Assert.Equal(_env.Company.Id, dto.CompanyId);
    }

    [Theory]
    [InlineData("", null, null, null)]
    [InlineData("Proveedor", "12345", null, null)]
    [InlineData("Proveedor", null, "no-es-un-correo", null)]
    [InlineData("Proveedor", null, null, 400)]
    [InlineData("Proveedor", null, null, -1)]
    public async Task ElProveedorValidaNombreRucCorreoYPlazo(string name, string? ruc, string? email, int? term)
    {
        var result = await Suppliers().Create(new SupplierUpsertRequest { Name = name, Ruc = ruc, Email = email, PaymentTermDays = term }, default);

        AssertStatus(result, 400);
        Assert.Empty(_env.T.NewContext().Suppliers.ToList());
    }

    [Fact]
    public async Task UnRucConElDigitoVerificadorIncorrectoSeRechaza()
    {
        var valid = Ruc();
        var wrong = valid[..^1] + ((valid[^1] - '0' + 1) % 10);

        AssertStatus(await Suppliers().Create(SupplierReq("Proveedor", wrong), default), 400);
        Assert.Equal(201, Assert.IsAssignableFrom<ObjectResult>((await Suppliers().Create(SupplierReq("Proveedor", valid), default)).Result).StatusCode);
    }

    [Fact]
    public async Task ElRucYElNombreNoSeRepitenDentroDeLaEmpresa_PeroSiEntreEmpresas()
    {
        Assert.Equal(201, Assert.IsAssignableFrom<ObjectResult>((await Suppliers().Create(SupplierReq("Uno", Ruc()), default)).Result).StatusCode);

        AssertStatus(await Suppliers().Create(SupplierReq("Otro nombre", Ruc()), default), 409);
        AssertStatus(await Suppliers().Create(SupplierReq("UNO", null), default), 409);               // el nombre tampoco, sin importar mayusculas

        var otherCompany = _env.T.AddCompany("Otra empresa");
        var asSuperAdmin = new SuppliersController(_env.T.Db, new FakeTenantContext { Role = "SuperAdmin" });
        var other = await asSuperAdmin.Create(new SupplierUpsertRequest { CompanyId = otherCompany.Id, Name = "Uno", Ruc = Ruc() }, default);
        Assert.Equal(201, Assert.IsAssignableFrom<ObjectResult>(other.Result).StatusCode);              // mismo RUC y nombre en otra empresa: permitido
    }

    [Fact]
    public async Task EditarUnProveedorNoChocaConSiMismoYSePuedeDesactivar()
    {
        var supplier = AddSupplier("Uno", Ruc());

        var updated = FinanceEnv.Ok(await Suppliers().Update(supplier.Id, new SupplierUpsertRequest { Name = "Uno SA", Ruc = Ruc(), IsActive = false }, default));

        Assert.Equal("Uno SA", updated.Name);
        Assert.False(updated.IsActive);
        Assert.False(_env.T.NewContext().Suppliers.Single(x => x.Id == supplier.Id).IsActive);
    }

    [Fact]
    public async Task CambiarElNombreDeUnProveedorNoReescribeLosGastosYaCargados()
    {
        var (period, date) = PastMonth();
        var supplier = AddSupplier("Nombre viejo");
        var expense = AddExpenseRow(period, date, supplierId: supplier.Id, supplierName: "Nombre viejo");

        FinanceEnv.Ok(await Suppliers().Update(supplier.Id, new SupplierUpsertRequest { Name = "Nombre nuevo" }, default));

        Assert.Equal("Nombre viejo", _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).SupplierName);
        Assert.Equal(1, FinanceEnv.Ok(await Suppliers().GetById(supplier.Id, default)).ExpenseCount);
    }

    [Fact]
    public async Task ElListadoBuscaPorNombreORucYFiltraPorEstado()
    {
        AddSupplier("Ascensores del Sur", Ruc());
        AddSupplier("Limpieza Total");
        AddSupplier("Seguros Paraguay", active: false);

        var all = FinanceEnv.Ok(await Suppliers().GetAll(null, null, null, 1, 50, default));
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["Ascensores del Sur", "Limpieza Total", "Seguros Paraguay"], all.Items.Select(i => i.Name).ToArray());
        Assert.True(all.CanEdit);

        Assert.Single(FinanceEnv.Ok(await Suppliers().GetAll(null, "Limpieza", null, 1, 50, default)).Items);
        Assert.Single(FinanceEnv.Ok(await Suppliers().GetAll(null, "80019876", null, 1, 50, default)).Items);
        Assert.Equal(2, FinanceEnv.Ok(await Suppliers().GetAll(null, null, true, 1, 50, default)).TotalCount);
        Assert.Single(FinanceEnv.Ok(await Suppliers().GetAll(null, null, false, 1, 50, default)).Items);
        Assert.Single(FinanceEnv.Ok(await Suppliers().GetAll(null, null, null, 3, 1, default)).Items);
    }

    [Fact]
    public async Task LosProveedoresSonDeLaEmpresaDelUsuarioYNoSeVenEntreEmpresas()
    {
        var mine = AddSupplier("Mio");
        var other = _env.T.AddCompany("Otra empresa");
        var theirs = AddSupplier("Ajeno", company: other);

        Assert.Equal(["Mio"], FinanceEnv.Ok(await Suppliers().GetAll(null, null, null, 1, 50, default)).Items.Select(i => i.Name).ToArray());
        Assert.IsType<NotFoundResult>((await Suppliers().GetById(theirs.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await Suppliers().Update(theirs.Id, SupplierReq("Cambio"), default)).Result);
        Assert.NotNull(FinanceEnv.Ok(await Suppliers().GetById(mine.Id, default)));
        // Un pedido de otra empresa por parametro no cambia de empresa a quien no es SuperAdmin.
        Assert.Equal(["Mio"], FinanceEnv.Ok(await Suppliers().GetAll(other.Id, null, null, 1, 50, default)).Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task ElSuperAdminIndicaLaEmpresa()
    {
        AddSupplier("Mio");
        var superAdmin = new SuppliersController(_env.T.Db, new FakeTenantContext { Role = "SuperAdmin" });

        AssertStatus(await superAdmin.GetAll(null, null, null, 1, 50, default), 400);
        Assert.Single(FinanceEnv.Ok(await superAdmin.GetAll(_env.Company.Id, null, null, 1, 50, default)).Items);
        AssertStatus(await superAdmin.Create(new SupplierUpsertRequest { CompanyId = Guid.NewGuid(), Name = "X" }, default), 400);
    }

    [Fact]
    public async Task LosCuatroRolesVenProveedoresYSoloQuienesCarganGastosLosEditan()
    {
        var supplier = AddSupplier("Uno");

        foreach (var role in new[] { "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            _env.LoginAs(role);
            Assert.NotNull(FinanceEnv.Ok(await Suppliers().GetAll(null, null, null, 1, 50, default)));
            FinanceEnv.Ok(await Suppliers().Update(supplier.Id, SupplierReq("Uno"), default));
        }

        _env.LoginAs("Owner");
        AssertStatus(await Suppliers().GetAll(null, null, null, 1, 50, default), 403);
        AssertStatus(await Suppliers().Create(SupplierReq("Dos"), default), 403);
        AssertStatus(await Suppliers().Update(supplier.Id, SupplierReq("Uno"), default), 403);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Impuestos: IVA por cuenta
    // ═════════════════════════════════════════════════════════════════════════

    private List<LedgerCategory> ExpenseLeaves() =>
        FinanceBudgetCalculator.BudgetableCategories(_env.AllCats()).Where(c => c.Type == LedgerCategoryType.Expense && c.IsActive).ToList();

    [Fact]
    public async Task LaPantallaDeIvaListaLasCuentasFinalesDeEgresosSinTratamiento()
    {
        _env.Setup();
        _env.SeedTemplate();

        var dto = FinanceEnv.Ok(await Policies().GetVatTreatments(_env.Building.Id, default));

        Assert.True(dto.FinanceAvailable);
        Assert.True(dto.CanEdit);
        Assert.False(dto.Applies);                                 // sin regimen de IVA definido
        Assert.Equal(0, dto.DefinedCount);
        Assert.Equal(ExpenseLeaves().Count, dto.TotalCount);
        Assert.All(dto.Items, i => Assert.Null(i.Treatment));
        Assert.All(dto.Items, i => Assert.False(string.IsNullOrEmpty(i.GroupName)));
    }

    [Fact]
    public async Task SeEstableceYSeLimpiaElTratamientoDeIvaDeUnaCuentaYQuedaRegistrado()
    {
        _env.Setup();
        _env.SeedTemplate();
        var leaves = ExpenseLeaves();

        var dto = FinanceEnv.Ok(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest
        {
            Items =
            [
                new() { CategoryId = leaves[0].Id, Treatment = VatTreatment.Vat10 },
                new() { CategoryId = leaves[1].Id, Treatment = VatTreatment.Exempt },
                new() { CategoryId = leaves[2].Id, Treatment = VatTreatment.NotApplicable }
            ]
        }, default));

        Assert.Equal(3, dto.DefinedCount);
        Assert.Equal(VatTreatment.Vat10, dto.Items.Single(i => i.CategoryId == leaves[0].Id).Treatment);
        var entry = Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Taxes).ToList());
        Assert.Contains("3 cuentas", entry.Summary);
        Assert.Equal(3, System.Text.Json.JsonDocument.Parse(entry.ChangesJson!).RootElement.GetArrayLength());

        // Mandar lo mismo no deja otra entrada; vaciar un tratamiento si.
        FinanceEnv.Ok(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = leaves[0].Id, Treatment = VatTreatment.Vat10 }] }, default));
        Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Taxes).ToList());
        var cleared = FinanceEnv.Ok(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = leaves[0].Id, Treatment = null }] }, default));
        Assert.Null(cleared.Items.Single(i => i.CategoryId == leaves[0].Id).Treatment);
    }

    [Fact]
    public async Task ElTratamientoDeIvaSoloVaEnCuentasFinalesDeEgresosDelEdificio()
    {
        _env.Setup();
        _env.SeedTemplate();
        var group = _env.AllCats().First(c => _env.AllCats().Any(x => x.ParentId == c.Id) && c.Type == LedgerCategoryType.Expense);
        var income = _env.AllCats().First(c => c.Type == LedgerCategoryType.Income && !_env.AllCats().Any(x => x.ParentId == c.Id));

        AssertStatus(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = group.Id, Treatment = VatTreatment.Vat10 }] }, default), 400);
        AssertStatus(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = income.Id, Treatment = VatTreatment.Vat10 }] }, default), 400);
        AssertStatus(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = Guid.NewGuid(), Treatment = VatTreatment.Vat10 }] }, default), 400);
        var leaf = ExpenseLeaves()[0].Id;
        AssertStatus(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = leaf, Treatment = (VatTreatment)99 }] }, default), 400);
        AssertStatus(await Policies().UpdateVatTreatments(_env.Building.Id, new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = leaf }, new() { CategoryId = leaf }] }, default), 400);
        Assert.Empty(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Taxes).ToList());
    }

    [Fact]
    public async Task ElIvaPorCuentaSoloLoEditanSuperAdminYAdministradorYRequiereFinanzas()
    {
        _env.Setup();
        _env.SeedTemplate();
        var request = new UpdateVatTreatmentsRequest { Items = [new() { CategoryId = ExpenseLeaves()[0].Id, Treatment = VatTreatment.Vat10 }] };

        foreach (var role in new[] { "CompanyOperator", "BuildingManager", "Owner" })
        {
            _env.LoginAs(role);
            AssertStatus(await Policies().UpdateVatTreatments(_env.Building.Id, request, default), 403);
        }

        _env.LoginAs("BuildingManager");
        Assert.NotNull(FinanceEnv.Ok(await Policies().GetVatTreatments(_env.Building.Id, default)));      // el encargado lo ve

        _env.LoginAs("CompanyAdmin");
        FinanceEnv.Ok(await Policies().UpdateVatTreatments(_env.Building.Id, request, default));

        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();
        AssertStatus(await Policies().UpdateVatTreatments(_env.Building.Id, request, default), 403);
        Assert.False(FinanceEnv.Ok(await Policies().GetVatTreatments(_env.Building.Id, default)).FinanceAvailable);
    }

    [Fact]
    public async Task ElResumenDeImpuestosEsObligatorioSoloConRegimenGeneral()
    {
        _env.Setup();
        _env.SeedTemplate();
        var building = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);

        // Sin regimen definido o con regimen que no discrimina: opcional.
        var none = Section(await Overview(), ConfigSectionKeys.Taxes);
        Assert.False(none.Required);
        Assert.Equal(ConfigSectionStatus.Optional, none.Status);
        Assert.Contains("régimen", none.Reasons.Single());

        building.VatRegime = VatRegime.Resimple;
        _env.T.Db.SaveChanges();
        Assert.False(Section(await Overview(), ConfigSectionKeys.Taxes).Required);

        // Regimen general: obligatorio e incompleto hasta clasificar todas las cuentas.
        building.VatRegime = VatRegime.General;
        _env.T.Db.SaveChanges();
        var incomplete = Section(await Overview(), ConfigSectionKeys.Taxes);
        Assert.True(incomplete.Required);
        Assert.Equal(ConfigSectionStatus.Incomplete, incomplete.Status);
        Assert.Contains("Faltan", incomplete.Reasons.Single());

        foreach (var leaf in _env.T.Db.LedgerCategories.Where(x => x.BuildingId == _env.Building.Id && x.Type == LedgerCategoryType.Expense).ToList())
            leaf.VatTreatment = VatTreatment.Exempt;
        _env.T.Db.SaveChanges();
        var complete = Section(await Overview(), ConfigSectionKeys.Taxes);
        Assert.Equal(ConfigSectionStatus.Complete, complete.Status);
        Assert.Equal("self", complete.LinkKind);
    }

    [Fact]
    public async Task ElResumenDeImpuestosNoEstaDisponibleSinFinanzasYNoCuentaComoObligatorio()
    {
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).VatRegime = VatRegime.General;
        _env.T.Db.SaveChanges();

        var taxes = Section(await Overview(), ConfigSectionKeys.Taxes);

        Assert.Equal(ConfigSectionStatus.NotAvailable, taxes.Status);
        Assert.False(taxes.Required);
    }

    [Fact]
    public async Task ElResumenDeProveedoresCuentaLosDeLaEmpresa()
    {
        var empty = Section(await Overview(), ConfigSectionKeys.Suppliers);
        Assert.Equal(ConfigSectionStatus.Optional, empty.Status);
        Assert.False(empty.Required);

        AddSupplier("Uno");
        AddSupplier("Dos", active: false);
        AddSupplier("De otra empresa", company: _env.T.AddCompany("Otra"));

        var section = Section(await Overview(), ConfigSectionKeys.Suppliers);
        Assert.Equal(ConfigSectionStatus.Complete, section.Status);
        Assert.Contains(section.Summary, i => i.Label == "Proveedores activos" && i.Value == "1");
        Assert.Contains(section.Summary, i => i.Label == "Proveedores desactivados" && i.Value == "1");
    }

    private async Task<BuildingConfigOverviewDto> Overview() => FinanceEnv.Ok(await Config().GetOverview(_env.Building.Id, default));

    private static ConfigSectionDto Section(BuildingConfigOverviewDto o, string key) => o.Sections.Single(s => s.Key == key);

    [Fact]
    public async Task LaCuentaDelPlanMuestraSuTratamientoDeIvaSinPerderloAlEditarla()
    {
        _env.Setup();
        _env.SeedTemplate();
        var leaf = ExpenseLeaves()[0];
        _env.T.Db.LedgerCategories.Single(x => x.Id == leaf.Id).VatTreatment = VatTreatment.Vat5;
        _env.T.Db.SaveChanges();
        var api = _env.Controller();

        var listed = FinanceEnv.Ok(await api.GetAll(_env.Building.Id, default)).Single(c => c.Id == leaf.Id);
        Assert.Equal(VatTreatment.Vat5, listed.VatTreatment);

        FinanceEnv.Ok(await api.Update(leaf.Id, new LedgerCategoryUpsertRequest
        {
            BuildingId = _env.Building.Id, ParentId = leaf.ParentId, Code = leaf.Code, Name = "Renombrada", Type = leaf.Type, IsActive = true
        }, default));

        Assert.Equal(VatTreatment.Vat5, _env.T.NewContext().LedgerCategories.Single(x => x.Id == leaf.Id).VatTreatment);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Libro de compras
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<VatPurchasesBookDto> Book(DateOnly from, DateOnly to) =>
        FinanceEnv.Ok(await Vat().Purchases(_env.Building.Id, from, to, default));

    private void SeedPurchases(out DateOnly first, out DateOnly last)
    {
        var (period, date) = PastMonth(1);
        first = new DateOnly(date.Year, date.Month, 1);
        last = first.AddMonths(1).AddDays(-1);
        var supplier = AddSupplier("Ascensores del Sur", Ruc());
        var a = AddExpenseRow(period, first.AddDays(2), 110_000m, vatRate: 10m, supplierId: supplier.Id, supplierName: supplier.Name);
        a.InvoiceNumber = "001-001-0000100"; a.InvoiceTimbrado = "12345678";
        AddExpenseRow(period, first.AddDays(4), 105_000m, vatRate: 5m);
        AddExpenseRow(period, first.AddDays(6), 80_000m, vatRate: 0m);
        AddExpenseRow(period, first.AddDays(8), 60_000m);                                    // sin tasa y sin cuenta: sin clasificar
        var withdrawn = AddExpenseRow(period, first.AddDays(9), 70_000m, vatRate: 10m);
        withdrawn.IsDeleted = true;                                                          // un gasto eliminado no entra
        _env.T.Db.SaveChanges();
    }

    [Fact]
    public async Task ElLibroDiscriminaElIvaPorTasa()
    {
        _env.Setup();
        SeedPurchases(out var first, out var last);

        var book = await Book(first, last);

        Assert.Equal(3, book.Rows.Count);
        Assert.Equal(["10", "5", "0"], book.TotalsByRate.Select(t => t.Rate.ToString("0")).ToArray());
        var ten = book.TotalsByRate[0];
        Assert.Equal((1, 110_000m, 100_000m, 10_000m), (ten.Count, ten.Total, ten.Base, ten.Vat));
        var five = book.TotalsByRate[1];
        Assert.Equal((1, 105_000m, 100_000m, 5_000m), (five.Count, five.Total, five.Base, five.Vat));
        var exempt = book.TotalsByRate[2];
        Assert.Equal((1, 80_000m, 80_000m, 0m), (exempt.Count, exempt.Total, exempt.Base, exempt.Vat));
        Assert.Equal(295_000m, book.GrandTotal);
        Assert.Equal(280_000m, book.GrandBase);
        Assert.Equal(15_000m, book.GrandVat);
        Assert.Equal(1, book.UnclassifiedCount);
        Assert.Equal(60_000m, book.UnclassifiedTotal);
    }

    [Fact]
    public async Task CadaRenglonLlevaProveedorRucTimbradoYComprobante()
    {
        _env.Setup();
        SeedPurchases(out var first, out var last);

        var row = (await Book(first, last)).Rows.Single(r => r.VatRate == 10m);

        Assert.Equal("Ascensores del Sur", row.SupplierName);
        Assert.Equal(Ruc(), row.SupplierRuc);
        Assert.Equal("12345678", row.Timbrado);
        Assert.Equal("001-001-0000100", row.DocumentNumber);
        Assert.False(row.IsCreditNote);
        Assert.Equal(first.AddDays(2), row.Date);
    }

    [Fact]
    public async Task UnaNotaDeCreditoVaEnNegativoConLaTasaDelGastoYElLibroCuadraConElMontoNeto()
    {
        _env.Setup();
        var (period, date) = PastMonth(1);
        var first = new DateOnly(date.Year, date.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        // La factura era de 220.000 y una nota de credito la bajo a 110.000 (el gasto queda neto).
        var expense = AddExpenseRow(period, first.AddDays(2), 110_000m, vatRate: 10m);
        expense.OriginalAmount = 220_000m;
        _env.T.Db.BuildingExpenseCreditNotes.Add(new BuildingExpenseCreditNote
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, BuildingExpenseId = expense.Id, ExpensePeriodId = period.Id,
            SupplierName = "Proveedor", Numero = "001-001-0000900", Timbrado = "999", IssueDate = first.AddDays(5), Amount = 110_000m,
            Reason = "Devolución", Status = BuildingExpenseCreditNoteStatus.Applied, Mode = BuildingExpenseCreditNoteMode.Netted,
            SupplierKey = "P", NumeroKey = "9", TimbradoKey = "9", CreatedByUserId = _env.Admin.Id
        });
        _env.T.Db.SaveChanges();

        var book = await Book(first, last);

        Assert.Equal(2, book.Rows.Count);
        var note = book.Rows.Single(r => r.IsCreditNote);
        Assert.Equal(-110_000m, note.Total);
        Assert.Equal(-10_000m, note.Vat);
        Assert.Equal(10m, note.VatRate);
        Assert.Equal("001-001-0000900", note.DocumentNumber);
        Assert.Equal(220_000m, book.Rows.Single(r => !r.IsCreditNote).Total);                  // la factura por su monto original
        Assert.Equal(110_000m, book.GrandTotal);                                               // 220.000 - 110.000 = el monto neto
        Assert.Equal(10_000m, book.GrandVat);
    }

    [Fact]
    public async Task UnaNotaAnuladaOFueraDelRangoNoEntra()
    {
        _env.Setup();
        var (period, date) = PastMonth(1);
        var first = new DateOnly(date.Year, date.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var expense = AddExpenseRow(period, first.AddDays(2), 110_000m, vatRate: 10m);
        BuildingExpenseCreditNote Note(DateOnly issued, BuildingExpenseCreditNoteStatus status, string n) => new()
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, BuildingExpenseId = expense.Id, ExpensePeriodId = period.Id, SupplierName = "P",
            Numero = n, IssueDate = issued, Amount = 10_000m, Reason = "x", Status = status, SupplierKey = "P", NumeroKey = n, TimbradoKey = n, CreatedByUserId = _env.Admin.Id
        };
        _env.T.Db.BuildingExpenseCreditNotes.AddRange(
            Note(first.AddDays(3), BuildingExpenseCreditNoteStatus.Voided, "1"),
            Note(last.AddDays(5), BuildingExpenseCreditNoteStatus.Applied, "2"));
        _env.T.Db.SaveChanges();

        Assert.Single((await Book(first, last)).Rows);
    }

    [Fact]
    public async Task ElLibroValidaElRangoYPorDefectoMuestraElMesEnCurso()
    {
        AssertStatus(await Vat().Purchases(_env.Building.Id, Today, Today.AddDays(-1), default), 400);
        AssertStatus(await Vat().Purchases(_env.Building.Id, Today.AddDays(-500), Today, default), 400);

        var current = FinanceEnv.Ok(await Vat().Purchases(_env.Building.Id, null, null, default));
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1), current.From);
        Assert.Equal(FinancePeriods.EndOfMonth(Today.Year, Today.Month), current.To);
        Assert.Equal(3, current.TotalsByRate.Count);                                           // siempre las tres tasas
    }

    [Fact]
    public async Task ElLibroDeComprasEsDeConsultaParaLosCuatroRolesYExigeFinanzas()
    {
        foreach (var role in new[] { "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            _env.LoginAs(role);
            Assert.NotNull(FinanceEnv.Ok(await Vat().Purchases(_env.Building.Id, null, null, default)));
        }

        _env.LoginAs("Owner");
        AssertStatus(await Vat().Purchases(_env.Building.Id, null, null, default), 403);

        _env.LoginAs("SuperAdmin");
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();
        AssertStatus(await Vat().Purchases(_env.Building.Id, null, null, default), 403);
    }

    [Fact]
    public async Task ElExcelDelLibroTraeLosMismosNumerosQueLaPantalla()
    {
        _env.Setup();
        SeedPurchases(out var first, out var last);

        var result = await Vat().ExportPurchases(_env.Building.Id, first, last, default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Contains("libro-de-compras", file.FileDownloadName);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        var ws = workbook.Worksheet("Libro de compras");
        Assert.Equal("Fecha", ws.Cell(4, 1).GetString());
        Assert.Equal("IVA", ws.Cell(4, 13).GetString());
        Assert.Equal("Ascensores del Sur", ws.Cell(5, 2).GetString());          // el primer renglon es la factura del 10 %
        Assert.Equal(Ruc(), ws.Cell(5, 3).GetString());
        Assert.Equal(110_000m, ws.Cell(5, 10).GetValue<decimal>());
        Assert.Equal(10_000m, ws.Cell(5, 13).GetValue<decimal>());
        Assert.Equal("Total", ws.Cell(8, 1).GetString());                        // tres renglones y la fila de totales
        Assert.Equal(295_000m, ws.Cell(8, 10).GetValue<decimal>());
        Assert.Equal(15_000m, ws.Cell(8, 13).GetValue<decimal>());
        Assert.Contains(ws.CellsUsed(), c => c.GetString().Contains("no tienen tasa de IVA"));
    }

    [Fact]
    public async Task ElExcelDelLibroRechazaUnRangoInvalido()
    {
        var result = await Vat().ExportPurchases(_env.Building.Id, Today, Today.AddDays(-3), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
