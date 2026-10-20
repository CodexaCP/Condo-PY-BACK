using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Finance;

/// <summary>
/// Cuenta de cobros online por liquidar (FinancialAccountType.Clearing), metodo y canal Online: donde cae un cobro online en el libro, que la
/// cuenta no se confunda con una caja o un banco, que no se maneje a mano y que el registro manual no acepte el metodo Online.
/// </summary>
public class ClearingAccountTests
{
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Fund = Guid.NewGuid();
    private static readonly Guid Bank = Guid.NewGuid();
    private static readonly Guid Clearing = Guid.NewGuid();

    private static FinancialAccount Account(FinancialAccountType type, bool active = true, Guid? id = null) =>
        new() { Id = id ?? Guid.NewGuid(), Name = type.ToString(), Type = type, IsActive = active };

    // ── Donde cae un cobro online (regla pura) ───────────────────────────────

    [Theory]
    [InlineData(CollectionClass.Ordinary)]
    [InlineData(CollectionClass.Extraordinary)]
    [InlineData(CollectionClass.IndividualAdjustment)]
    [InlineData(CollectionClass.LateFee)]
    [InlineData(CollectionClass.ReserveFund)]
    public void UnCobroOnlineVaALaCuentaPorLiquidarSeaCualSeaElCargoQueCubrio(CollectionClass cls)
    {
        var result = FinanceLedgerRules.ForPayment(PaymentMethod.Online, cls, new LedgerAccounts(Cash, Fund, Bank, Clearing));

        Assert.Equal(Clearing, result.AccountId);
        Assert.Equal(LedgerDirection.In, result.Direction);
        Assert.Equal(FinanceLedgerRules.CollectionKey(cls), result.RubroKey);   // el rubro sigue saliendo del cargo cubierto
    }

    [Fact]
    public void SinCuentaPorLiquidarElCobroOnlineQuedaEnLaCuentaPorDefectoYNoSePierde()
    {
        var result = FinanceLedgerRules.ForPayment(PaymentMethod.Online, CollectionClass.Ordinary, new LedgerAccounts(Cash, Fund, Bank));

        Assert.Equal(Bank, result.AccountId);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Card)]
    public void LosDemasMetodosNoCambianAunqueHayaCuentaPorLiquidar(PaymentMethod method)
    {
        var result = FinanceLedgerRules.ForPayment(method, CollectionClass.Ordinary, new LedgerAccounts(Cash, Fund, Bank, Clearing));

        Assert.Equal(method == PaymentMethod.Cash ? Cash : Bank, result.AccountId);
    }

    // ── La cuenta por liquidar no es caja ni banco ───────────────────────────

    [Fact]
    public void LaCuentaPorLiquidarSeResuelveSolaYNoEsLaCuentaPorDefecto()
    {
        var clearing = Account(FinancialAccountType.Clearing);

        var resolved = LedgerAccounts.Resolve([clearing], null);

        Assert.Equal(clearing.Id, resolved.ClearingId);
        Assert.Null(resolved.DefaultId);   // aunque sea la unica cuenta, no es "el resto" de caja o banco
    }

    [Fact]
    public void LaUnicaCajaOBancoSigueSiendoLaPorDefectoAunqueExistaLaCuentaPorLiquidar()
    {
        var cash = Account(FinancialAccountType.Cash);

        var resolved = LedgerAccounts.Resolve([cash, Account(FinancialAccountType.Clearing), Account(FinancialAccountType.ReserveFund)], null);

        Assert.Equal(cash.Id, resolved.DefaultId);
    }

    [Fact]
    public void UnaCuentaPorDefectoConfiguradaQueEsLaPorLiquidarSeIgnora()
    {
        var clearing = Account(FinancialAccountType.Clearing);
        var bank = Account(FinancialAccountType.Bank);

        var resolved = LedgerAccounts.Resolve([clearing, bank], clearing.Id);

        Assert.Equal(bank.Id, resolved.DefaultId);
    }

    [Fact]
    public void UnaCuentaPorLiquidarInactivaNoSeResuelve()
    {
        Assert.Null(LedgerAccounts.Resolve([Account(FinancialAccountType.Clearing, active: false)], null).ClearingId);
    }

    [Fact]
    public void LaCuentaPorLiquidarNoSumaACajaBancoNiFondoPeroSiAlTotal()
    {
        var bank = Account(FinancialAccountType.Bank);
        var clearing = Account(FinancialAccountType.Clearing);
        var ctx = new LedgerContext
        {
            BuildingId = Guid.NewGuid(), BuildingName = "Edificio", StartDate = new DateOnly(2026, 1, 1), FiscalYearStartMonth = 1,
            IncomeTreatment = default, Accounts = [bank, clearing], Resolved = LedgerAccounts.Resolve([bank, clearing], null), Categories = []
        };
        bank.OpeningBalance = 1_000_000m;
        LedgerBucket In(Guid account, decimal amount) =>
            new(2026, 2, account, FinanceChartTemplate.CollectionOrdinary, LedgerDirection.In, amount);

        var balances = FinanceReportBuilder.BuildBalances(ctx, [In(bank.Id, 200_000m), In(clearing.Id, 300_000m)], new DateOnly(2026, 3, 1), null);

        Assert.Equal(1_200_000m, balances.BankBalance);
        Assert.Equal(300_000m, balances.ClearingBalance);
        Assert.Equal(0m, balances.CashBalance);
        Assert.Equal(0m, balances.ReserveFundBalance);
        Assert.Equal(1_500_000m, balances.TotalBalance);
        Assert.Equal(FinancialAccountType.Clearing, balances.Accounts.Last().Type);   // despues de caja, bancos y fondo
    }

    // ── Liquidacion del proveedor = transferencia al banco ───────────────────

    [Fact]
    public void LaTransferenciaDesdeLaCuentaPorLiquidarSeLlamaLiquidacionDeCobrosOnline()
    {
        Assert.Equal("Liquidación de cobros online", FinanceTransferRules.KindLabel(FinancialAccountType.Clearing, FinancialAccountType.Bank));
        Assert.Equal("Aporte al fondo de reserva", FinanceTransferRules.KindLabel(FinancialAccountType.Clearing, FinancialAccountType.ReserveFund));
        Assert.Equal("Transferencia entre cuentas", FinanceTransferRules.KindLabel(FinancialAccountType.Bank, FinancialAccountType.Bank));
    }

    [Fact]
    public void LaCuentaPorLiquidarSoloPuedeSerOrigenDeUnaTransferencia()
    {
        var building = Guid.NewGuid();
        FinancialAccount Acc(FinancialAccountType type) => new() { Id = Guid.NewGuid(), BuildingId = building, Name = type.ToString(), Type = type };
        var clearing = Acc(FinancialAccountType.Clearing);
        var bank = Acc(FinancialAccountType.Bank);
        var day = new DateOnly(2026, 2, 1);
        var today = new DateOnly(2026, 3, 1);

        Assert.Null(FinanceTransferRules.Validate(clearing, bank, 100m, day, day, today, "", "", ""));
        Assert.Contains("solo puede ser el origen", FinanceTransferRules.Validate(bank, clearing, 100m, day, day, today, "", "", ""));
    }

    // ── Alta y manejo de la cuenta ───────────────────────────────────────────

    private static FinanceEnv NewEnv()
    {
        var env = new FinanceEnv();
        env.Setup();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        return env;
    }

    private static FinanceAccountsController Accounts(FinanceEnv env) =>
        new(env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db));

    private static FinancialAccountUpsertRequest Upsert(FinanceEnv env, string name, FinancialAccountType type) =>
        new() { BuildingId = env.Building.Id, Name = name, Type = type, IsActive = true };

    private static async Task<FinancialAccount> EnsureAndSave(FinanceEnv env)
    {
        var account = await ClearingAccounts.EnsureAsync(env.T.Db, env.Company.Id, env.Building.Id, default);
        await env.T.Db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task ElSistemaCreaLaCuentaPorLiquidarUnaSolaVezPorEdificio()
    {
        using var env = NewEnv();

        var first = await EnsureAndSave(env);
        var second = await EnsureAndSave(env);

        Assert.Equal(first.Id, second.Id);
        var saved = Assert.Single(env.T.NewContext().FinancialAccounts.Where(x => x.Type == FinancialAccountType.Clearing).ToList());
        Assert.Equal(ClearingAccounts.DefaultName, saved.Name);
        Assert.Equal(0m, saved.OpeningBalance);
        Assert.True(saved.IsActive);
        Assert.False(saved.Reconciles);
    }

    [Fact]
    public async Task UnaCuentaPorLiquidarDesactivadaSeReactivaYUnNombreOcupadoSeDesambigua()
    {
        using var env = NewEnv();
        var account = await EnsureAndSave(env);
        account.IsActive = false;
        env.T.Db.SaveChanges();

        var again = await EnsureAndSave(env);

        Assert.Equal(account.Id, again.Id);
        Assert.True(env.T.NewContext().FinancialAccounts.Single(x => x.Id == account.Id).IsActive);

        // Un edificio donde una cuenta manual ya usa ese nombre.
        using var other = NewEnv();
        other.T.Db.FinancialAccounts.Add(new FinancialAccount
        {
            CompanyId = other.Company.Id, BuildingId = other.Building.Id, Name = ClearingAccounts.DefaultName, Type = FinancialAccountType.Bank
        });
        other.T.Db.SaveChanges();

        var created = await EnsureAndSave(other);

        Assert.Equal($"{ClearingAccounts.DefaultName} (sistema)", created.Name);
        Assert.Equal(FinancialAccountType.Clearing, created.Type);
    }

    [Fact]
    public async Task LaCuentaPorLiquidarNoSeCreaNiSeEditaNiSeEliminaAMano()
    {
        using var env = NewEnv();
        var api = Accounts(env);
        var clearing = await EnsureAndSave(env);

        var create = await api.Create(Upsert(env, "Mi cuenta online", FinancialAccountType.Clearing), default);
        var rename = await api.Update(clearing.Id, Upsert(env, "Otro nombre", FinancialAccountType.Clearing), default);
        var retype = await api.Update(clearing.Id, Upsert(env, "Cobros online por liquidar", FinancialAccountType.Bank), default);
        var delete = await api.Delete(clearing.Id, default);

        Assert.Contains("la administra el sistema", FinanceEnv.BadRequestText(create));
        Assert.Contains("la administra el sistema", FinanceEnv.BadRequestText(rename));
        Assert.Contains("la administra el sistema", FinanceEnv.BadRequestText(retype));
        Assert.Contains("la administra el sistema", Assert.IsType<BadRequestObjectResult>(delete).Value!.ToString());
        var saved = env.T.NewContext().FinancialAccounts.Single(x => x.Id == clearing.Id);
        Assert.Equal(ClearingAccounts.DefaultName, saved.Name);
        Assert.Equal(FinancialAccountType.Clearing, saved.Type);
        Assert.False(saved.IsDeleted);
    }

    [Fact]
    public async Task UnaCuentaManualNoPuedePasarASerLaCuentaPorLiquidar()
    {
        using var env = NewEnv();
        var api = Accounts(env);
        var bank = FinanceEnv.Ok(await api.Create(Upsert(env, "Banco B", FinancialAccountType.Bank), default));

        var result = await api.Update(bank.Id, Upsert(env, "Banco B", FinancialAccountType.Clearing), default);

        Assert.Contains("la administra el sistema", FinanceEnv.BadRequestText(result));
        Assert.Equal(FinancialAccountType.Bank, env.T.NewContext().FinancialAccounts.Single(x => x.Id == bank.Id).Type);
    }

    [Fact]
    public async Task LaListaDeCuentasMuestraLaCuentaPorLiquidarAlFinal()
    {
        using var env = NewEnv();
        var api = Accounts(env);
        FinanceEnv.Ok(await api.Create(Upsert(env, "Caja", FinancialAccountType.Cash), default));
        await EnsureAndSave(env);

        var list = (Assert.IsType<OkObjectResult>((await api.GetAll(env.Building.Id, default)).Result).Value as IReadOnlyList<FinancialAccountDto>)!;

        Assert.Equal(FinancialAccountType.Clearing, list[^1].Type);
        Assert.False(list[^1].Reconciles);   // no se concilia contra un extracto
    }

    // ── No es una caja ni un banco ───────────────────────────────────────────

    [Fact]
    public async Task LaCuentaPorLiquidarNoPuedeSerLaCuentaPorDefectoNiCuentaDePagoDeGastos()
    {
        using var env = NewEnv();
        var clearing = await EnsureAndSave(env);

        var finance = new FinanceController(env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db));
        var defaultResult = await finance.SetDefaultAccount(env.Building.Id, new FinanceDefaultAccountRequest { AccountId = clearing.Id }, default);
        Assert.Contains("cobros online por liquidar", FinanceEnv.BadRequestText(defaultResult));

        var rubros = new MovementRubroResolver(env.T.Db, new FinanceModuleGate(env.T.Db));
        var expenses = new BuildingExpensesController(
            env.T.Db, env.Access, env.Tenant, new StubWebHostEnvironment(Path.GetTempPath()), new ConfigurationBuilder().Build(), rubros);
        var expense = env.AddExpense(null);
        expense.DueDate = FinanceEnv.Yesterday.AddDays(10);
        env.T.Db.SaveChanges();
        var payResult = await expenses.RegisterPayment(
            expense.Id, new RegisterExpensePaymentRequest { PaidAt = FinanceEnv.Yesterday, AccountId = clearing.Id }, default);
        Assert.Contains("no es una caja ni un banco", FinanceEnv.BadRequestText(payResult));
        Assert.Null(env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).PaidAt);
    }

    [Fact]
    public async Task LaCuentaPorLiquidarNoCuentaComoCajaOBancoEnLaConfiguracionDelEdificio()
    {
        using var env = new FinanceEnv();
        env.T.Db.FinanceSettings.Add(new FinanceSettings
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, FinanceStartDate = FinancePeriods.Today().AddMonths(-2), SetupCompleted = true
        });
        env.T.Db.SaveChanges();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        await EnsureAndSave(env);   // solo existe la cuenta por liquidar: no hay una caja ni un banco

        var finance = new FinanceController(env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db));
        var settings = FinanceEnv.Ok(await finance.GetSettings(env.Building.Id, default));

        Assert.Contains(settings.MissingForSetup, m => m.Contains("caja o banco"));
    }

    // ── El metodo y el canal Online ──────────────────────────────────────────

    private OwnerPaymentsController OwnerPayments(FinanceEnv env)
    {
        var credits = new OwnerCreditService(env.T.Db);
        var comprobantes = new ComprobanteService(env.T.Db, credits);
        return new OwnerPaymentsController(
            env.T.Db, env.Tenant, env.Access, credits, comprobantes, new InvoiceDraftService(env.T.Db, env.Access),
            new PushDispatcher(env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance),
            new OwnerPaymentSettlementService(env.T.Db, credits, comprobantes));
    }

    private static (Unit Unit, ApplicationUser Owner, ExpensePeriod Period) AddDebt(FinanceEnv env, decimal amount = 100_000m)
    {
        var unit = env.T.AddUnit(env.Building);
        var owner = env.T.AddUser(env.Company, UserRole.Owner);
        env.T.AddOwner(unit, owner);
        var period = new ExpensePeriod
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, Year = 2025, Month = 1, Name = "01/2025",
            StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 1, 31), DueDate = new DateOnly(2025, 1, 21),
            Status = ExpensePeriodStatus.Published
        };
        env.T.Db.ExpensePeriods.Add(period);
        env.T.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = env.Company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, ChargeType = ExpenseChargeType.Ordinary,
            Concept = "Expensas", Amount = amount
        });
        env.T.Db.SaveChanges();
        return (unit, owner, period);
    }

    [Fact]
    public async Task ElRegistroManualDelPersonalNoAceptaElMetodoOnline()
    {
        using var env = NewEnv();
        env.Tenant.Role = "CompanyAdmin";
        var (_, owner, _) = AddDebt(env);

        var result = await OwnerPayments(env).Register(
            new OwnerPaymentRegisterRequest
            {
                OwnerId = owner.Id, Amount = 100_000m, Method = PaymentMethod.Online, PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow)
            }, default);

        Assert.Contains("lo asigna el sistema", FinanceEnv.BadRequestText(result));
        Assert.Empty(env.T.NewContext().Payments.ToList());
    }

    [Fact]
    public async Task UnCobroOnlineLiquidadoQuedaConMetodoOnlineYSuNotaPropia()
    {
        using var env = NewEnv();
        var (unit, owner, _) = AddDebt(env);
        var payment = new OwnerPayment
        {
            CompanyId = env.Company.Id, OwnerId = owner.Id, PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow), DeclaredAmount = 100_000m,
            ReviewedAmount = 100_000m, Status = OwnerPaymentStatus.UnderReview, Reference = "PAY-2026-000001",
            Channel = OwnerPaymentChannel.Online, Method = PaymentMethod.Online, ExternalReference = "TK-998"
        };
        payment.Units.Add(new OwnerPaymentUnit { CompanyId = env.Company.Id, UnitId = unit.Id });
        env.T.Db.OwnerPayments.Add(payment);
        env.T.Db.SaveChanges();
        var credits = new OwnerCreditService(env.T.Db);

        await new OwnerPaymentSettlementService(env.T.Db, credits, new ComprobanteService(env.T.Db, credits))
            .SettleAsync(payment, new SettlementContext(env.Company.Id, null), default);
        await env.T.Db.SaveChangesAsync();

        var saved = Assert.Single(env.T.NewContext().Payments.ToList());
        Assert.Equal(PaymentMethod.Online, saved.Method);
        Assert.Contains("Pago online confirmado por el proveedor", saved.Notes);
        Assert.Contains("Transacción: TK-998", saved.Notes);
    }

    [Fact]
    public async Task UnCobroOnlineNoSeRevierteDesdeElCaminoManual()
    {
        using var env = NewEnv();
        env.Tenant.Role = "CompanyAdmin";
        var (unit, owner, _) = AddDebt(env);
        var payment = new OwnerPayment
        {
            CompanyId = env.Company.Id, OwnerId = owner.Id, PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow), DeclaredAmount = 100_000m,
            ReviewedAmount = 100_000m, Status = OwnerPaymentStatus.Approved, Reference = "PAY-2026-000001",
            Channel = OwnerPaymentChannel.Online, Method = PaymentMethod.Online
        };
        payment.Units.Add(new OwnerPaymentUnit { CompanyId = env.Company.Id, UnitId = unit.Id, AllocatedAmount = 100_000m });
        env.T.Db.OwnerPayments.Add(payment);
        env.T.Db.SaveChanges();

        var result = await OwnerPayments(env).Reverse(payment.Id, new OwnerPaymentReverseRequest { Reason = "Error" }, default);

        Assert.Contains("la devolución se gestiona con el proveedor", FinanceEnv.BadRequestText(result));
        Assert.Equal(OwnerPaymentStatus.Approved, env.T.NewContext().OwnerPayments.Single(x => x.Id == payment.Id).Status);
    }

    [Fact]
    public async Task ElMetodoYElCanalOnlineSeGuardanComoTextoYSeLeenDeNuevo()
    {
        using var env = NewEnv();
        var (unit, owner, _) = AddDebt(env);
        var payment = new OwnerPayment
        {
            CompanyId = env.Company.Id, OwnerId = owner.Id, PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow), DeclaredAmount = 1m,
            Status = OwnerPaymentStatus.Pending, Reference = "PAY-2026-000009", Channel = OwnerPaymentChannel.Online, Method = PaymentMethod.Online
        };
        payment.Units.Add(new OwnerPaymentUnit { CompanyId = env.Company.Id, UnitId = unit.Id });
        env.T.Db.OwnerPayments.Add(payment);
        env.T.Db.SaveChanges();

        var read = await env.T.NewContext().OwnerPayments.SingleAsync(x => x.Id == payment.Id);

        Assert.Equal(OwnerPaymentChannel.Online, read.Channel);
        Assert.Equal(PaymentMethod.Online, read.Method);
    }
}
