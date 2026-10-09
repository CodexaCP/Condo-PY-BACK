using ClosedXML.Excel;
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
/// Finanzas, asientos sugeridos: el armado de la partida doble (logica pura), la asignacion de las cuentas del plan que hacen de contrapartida,
/// los asientos del libro real y su Excel, y el paquete del contador con el libro de compras. El libro suma importes en la base: las pruebas
/// que lo leen usan SQL Server real y se omiten solas sin la variable CONDO_TEST_SQLSERVER.
/// </summary>
public class ConfigCenterPhase6Tests
{
    // ═════════════════════════════════════════════════════════════════════════
    // Armado de asientos (sin base de datos)
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly DateOnly D1 = new(2026, 3, 5);
    private static readonly DateOnly D2 = new(2026, 3, 10);

    private static readonly Guid BankId = Guid.NewGuid();
    private static readonly Guid CashId = Guid.NewGuid();
    private static readonly AccountingPlanAccount BankPlan = new(Guid.NewGuid(), "1.1.03", "101003", "Banco cuenta corriente");
    private static readonly AccountingPlanAccount CashPlan = new(Guid.NewGuid(), "1.1.01", null, "Caja administración");
    private static readonly AccountingPlanAccount VatPlan = new(Guid.NewGuid(), "1.4.06", "140106", "IVA crédito fiscal");
    private static readonly AccountingPlanAccount IncomePlan = new(Guid.NewGuid(), "4.1.01", null, "Expensas ordinarias");
    private static readonly AccountingPlanAccount LateFeePlan = new(Guid.NewGuid(), "4.1.04", null, "Intereses por mora");
    private static readonly AccountingPlanAccount ExpensePlan = new(Guid.NewGuid(), "5.01.01", null, "Mantenimiento");

    private static AccountingBuildInput Input(
        bool bank = true, bool vat = true, bool rubros = true, params (LedgerSourceType Type, Guid Id, decimal Rate)[] rates) => new()
    {
        FinancialPlan = bank
            ? new Dictionary<Guid, AccountingPlanAccount> { [BankId] = BankPlan, [CashId] = CashPlan }
            : new Dictionary<Guid, AccountingPlanAccount>(),
        FinancialNames = new Dictionary<Guid, string> { [BankId] = "Banco Itaú", [CashId] = "Caja" },
        VatCredit = vat ? VatPlan : null,
        RubroPlan = key => !rubros ? null : key switch
        {
            "Income.Ordinary" => IncomePlan,
            "Income.LateFee" => LateFeePlan,
            "Expense.Maintenance" => ExpensePlan,
            _ => null
        },
        RubroLabel = key => key,
        VatRates = rates.ToDictionary(r => (r.Type, r.Id), r => r.Rate)
    };

    private static LedgerRow Row(
        DateOnly date, Guid? account, string rubro, LedgerDirection direction, decimal amount, LedgerSourceType type, Guid id, string description = "Mov") =>
        new(date, account, rubro, direction, amount, type, id, description, "Tercero", "Ref");

    private static AccountingLineDto Line(AccountingEntryDto entry, string code) => entry.Lines.Single(l => l.Code == code);

    [Fact]
    public void UnCobroDebitaElBancoYAcreditaElIngreso_YElAsientoCuadra()
    {
        var id = Guid.NewGuid();

        var result = AccountingEntryBuilder.Build(
            [Row(D1, BankId, "Income.Ordinary", LedgerDirection.In, 1_000_000m, LedgerSourceType.OwnerPayment, id, "Expensas marzo")], Input());

        var entry = Assert.Single(result.Entries);
        Assert.Equal(1, entry.Number);
        Assert.Equal(D1, entry.Date);
        Assert.Equal("Expensas marzo", entry.Description);
        Assert.Equal(1_000_000m, Line(entry, "1.1.03").Debit);
        Assert.Equal(0m, Line(entry, "1.1.03").Credit);
        Assert.Equal(1_000_000m, Line(entry, "4.1.01").Credit);
        Assert.Equal("101003", Line(entry, "1.1.03").ExternalCode);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.True(entry.IsComplete);
        Assert.Empty(entry.Issues);
        Assert.Empty(result.Pending);
    }

    [Fact]
    public void UnGastoConIvaSeParteEnBaseEIvaCreditoYSaleDelBanco()
    {
        var id = Guid.NewGuid();

        var entry = Assert.Single(AccountingEntryBuilder.Build(
            [Row(D1, BankId, "Expense.Maintenance", LedgerDirection.Out, 110_000m, LedgerSourceType.BuildingExpense, id)],
            Input(rates: (LedgerSourceType.BuildingExpense, id, 10m))).Entries);

        Assert.Equal(100_000m, Line(entry, "5.01.01").Debit);
        Assert.Equal(10_000m, Line(entry, "1.4.06").Debit);
        Assert.Equal(110_000m, Line(entry, "1.1.03").Credit);
        Assert.Equal(110_000m, entry.Debit);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.Equal(["1.4.06", "5.01.01", "1.1.03"], entry.Lines.OrderByDescending(l => l.Debit > 0).ThenBy(l => l.Code).Select(l => l.Code).ToArray());
    }

    [Fact]
    public void UnGastoExentoOSinTasaNoTieneRenglonDeIva()
    {
        var exempt = Guid.NewGuid();
        var unrated = Guid.NewGuid();

        var result = AccountingEntryBuilder.Build(
        [
            Row(D1, BankId, "Expense.Maintenance", LedgerDirection.Out, 50_000m, LedgerSourceType.BuildingExpense, exempt),
            Row(D1, BankId, "Expense.Maintenance", LedgerDirection.Out, 70_000m, LedgerSourceType.BuildingExpense, unrated)
        ], Input(rates: (LedgerSourceType.BuildingExpense, exempt, 0m)));

        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.DoesNotContain(e.Lines, l => l.Code == "1.4.06"));
        Assert.All(result.Entries, e => Assert.Equal(e.Debit, e.Credit));
    }

    [Fact]
    public void UnaNotaDeCreditoDelProveedorEsElAsientoAlReves_ConElIvaDelGastoQueCorrige()
    {
        var id = Guid.NewGuid();

        // El libro la trae como salida negativa: el banco recibe 22.000 de vuelta.
        var entry = Assert.Single(AccountingEntryBuilder.Build(
            [Row(D2, BankId, "Expense.Maintenance", LedgerDirection.Out, -22_000m, LedgerSourceType.SupplierCreditNote, id, "NC proveedor")],
            Input(rates: (LedgerSourceType.SupplierCreditNote, id, 10m))).Entries);

        Assert.Equal(22_000m, Line(entry, "1.1.03").Debit);
        Assert.Equal(20_000m, Line(entry, "5.01.01").Credit);
        Assert.Equal(2_000m, Line(entry, "1.4.06").Credit);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.True(entry.IsComplete);
    }

    [Fact]
    public void UnCobroPartidoEnVariosRubrosEsUnSoloAsientoConLaCuentaDelBancoSumada()
    {
        var id = Guid.NewGuid();

        var entry = Assert.Single(AccountingEntryBuilder.Build(
        [
            Row(D1, BankId, "Income.Ordinary", LedgerDirection.In, 800_000m, LedgerSourceType.OwnerPayment, id, "Expensas"),
            Row(D1, BankId, "Income.LateFee", LedgerDirection.In, 200_000m, LedgerSourceType.OwnerPayment, id, "Mora")
        ], Input()).Entries);

        Assert.Equal(3, entry.Lines.Count);
        Assert.Equal(1_000_000m, Line(entry, "1.1.03").Debit);
        Assert.Equal(800_000m, Line(entry, "4.1.01").Credit);
        Assert.Equal(200_000m, Line(entry, "4.1.04").Credit);
        Assert.Equal("Expensas · Mora", entry.Description);
    }

    [Fact]
    public void ElMismoOrigenEnDosCuentasFinancierasSonDosAsientos()
    {
        var id = Guid.NewGuid();

        var result = AccountingEntryBuilder.Build(
        [
            Row(D1, BankId, "Income.Ordinary", LedgerDirection.In, 600_000m, LedgerSourceType.OwnerPayment, id),
            Row(D1, CashId, "Income.Ordinary", LedgerDirection.In, 400_000m, LedgerSourceType.OwnerPayment, id)
        ], Input());

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal([1, 2], result.Entries.Select(e => e.Number).ToArray());
        Assert.Equal(600_000m + 400_000m, result.Totals.Single(t => t.Code == "4.1.01").Credit);
    }

    [Fact]
    public void SinCuentaDelPlanElAsientoQuedaIncompletoYNoSeInventaNada()
    {
        var expense = Guid.NewGuid();
        var income = Guid.NewGuid();

        var noBank = AccountingEntryBuilder.Build(
            [Row(D1, BankId, "Income.Ordinary", LedgerDirection.In, 100_000m, LedgerSourceType.BuildingIncome, income)], Input(bank: false));
        var entry = Assert.Single(noBank.Entries);
        Assert.False(entry.IsComplete);
        Assert.Contains(entry.Lines, l => l.Missing && l.Name == "Cuenta financiera: Banco Itaú" && l.Debit == 100_000m && l.CategoryId is null);
        Assert.Contains("Falta la cuenta del plan: Cuenta financiera: Banco Itaú.", entry.Issues);
        Assert.Equal(entry.Debit, entry.Credit);                              // numericamente cuadra, pero no se da por completo
        Assert.Equal(["Cuenta financiera: Banco Itaú"], noBank.Pending.ToArray());
        Assert.True(noBank.Totals.Single(t => t.Missing).Debit == 100_000m);

        var noVat = AccountingEntryBuilder.Build(
            [Row(D1, BankId, "Expense.Maintenance", LedgerDirection.Out, 110_000m, LedgerSourceType.BuildingExpense, expense)],
            Input(vat: false, rates: (LedgerSourceType.BuildingExpense, expense, 10m)));
        Assert.False(Assert.Single(noVat.Entries).IsComplete);
        Assert.Contains("IVA crédito fiscal", Assert.Single(noVat.Pending));

        var noRubro = AccountingEntryBuilder.Build(
            [Row(D1, BankId, "Expense.Unknown", LedgerDirection.Out, 10_000m, LedgerSourceType.BuildingExpense, Guid.NewGuid())], Input());
        Assert.False(Assert.Single(noRubro.Entries).IsComplete);
        Assert.Contains("Expense.Unknown", Assert.Single(noRubro.Pending));

        var noAccount = AccountingEntryBuilder.Build(
            [Row(D1, null, "Income.Ordinary", LedgerDirection.In, 10_000m, LedgerSourceType.BuildingIncome, Guid.NewGuid())], Input());
        Assert.Contains("Movimiento sin cuenta financiera", Assert.Single(noAccount.Pending));
    }

    [Fact]
    public void ElIvaSeRedondeaADosDecimalesYElAsientoSigueCuadrando()
    {
        var id = Guid.NewGuid();

        var entry = Assert.Single(AccountingEntryBuilder.Build(
            [Row(D1, BankId, "Expense.Maintenance", LedgerDirection.Out, 10_001m, LedgerSourceType.BuildingExpense, id)],
            Input(rates: (LedgerSourceType.BuildingExpense, id, 10m))).Entries);

        Assert.Equal(909.18m, Line(entry, "1.4.06").Debit);
        Assert.Equal(9_091.82m, Line(entry, "5.01.01").Debit);
        Assert.Equal(10_001m, Line(entry, "1.1.03").Credit);
        Assert.Equal(entry.Debit, entry.Credit);
    }

    [Fact]
    public void LasSumasPorCuentaCuadranYLaDelBancoEsElMovimientoNetoDelLibro()
    {
        var rows = new List<LedgerRow>
        {
            Row(D1, BankId, "Income.Ordinary", LedgerDirection.In, 1_000_000m, LedgerSourceType.OwnerPayment, Guid.NewGuid()),
            Row(D1, BankId, "Income.LateFee", LedgerDirection.In, 50_000m, LedgerSourceType.OwnerPayment, Guid.NewGuid()),
            Row(D2, BankId, "Expense.Maintenance", LedgerDirection.Out, 330_000m, LedgerSourceType.BuildingExpense, Guid.NewGuid()),
            Row(D2, CashId, "Expense.Maintenance", LedgerDirection.Out, 44_000m, LedgerSourceType.BuildingExpense, Guid.NewGuid())
        };
        var expenseIds = rows.Where(r => r.Direction == LedgerDirection.Out).Select(r => r.SourceId).ToList();

        var result = AccountingEntryBuilder.Build(rows, Input(rates: expenseIds.Select(i => (LedgerSourceType.BuildingExpense, i, 10m)).ToArray()));

        Assert.Equal(result.Entries.Sum(e => e.Debit), result.Totals.Sum(t => t.Debit));
        Assert.Equal(result.Totals.Sum(t => t.Debit), result.Totals.Sum(t => t.Credit));
        var bank = result.Totals.Single(t => t.Code == "1.1.03");
        Assert.Equal(rows.Where(r => r.AccountId == BankId).Sum(r => r.Signed), bank.Debit - bank.Credit);
        var cash = result.Totals.Single(t => t.Code == "1.1.01");
        Assert.Equal(-44_000m, cash.Debit - cash.Credit);
        Assert.Equal(30_000m + 4_000m, result.Totals.Single(t => t.Code == "1.4.06").Debit);
        Assert.Equal([1, 2, 3, 4], result.Entries.Select(e => e.Number).ToArray());
        Assert.Equal(result.Entries.OrderBy(e => e.Date).Select(e => e.Number), result.Entries.Select(e => e.Number));
    }

    [Fact]
    public void SinRenglonesNoHayAsientos()
    {
        var result = AccountingEntryBuilder.Build([], Input());

        Assert.Empty(result.Entries);
        Assert.Empty(result.Totals);
        Assert.Empty(result.Pending);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Contrapartidas (SQLite alcanza)
    // ═════════════════════════════════════════════════════════════════════════

    private static AccountingEntriesService Service(FinanceEnv env)
    {
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        return new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);
    }

    private static FinanceAccountingController Api(FinanceEnv env)
    {
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var ledger = new FinanceLedgerService(env.T.Db);
        return new FinanceAccountingController(env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), ledger, Service(env));
    }

    private static async Task<LedgerContext> Context(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static UpdateAccountingRolesRequest Assign(Guid accountId, Guid? categoryId) =>
        new() { Accounts = [new AccountingRoleItemRequest { FinancialAccountId = accountId, LedgerCategoryId = categoryId }] };

    [Fact]
    public async Task LasContrapartidasSugierenPorNombreYTipoPeroNoAplicanNada()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        var cash = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Caja chica", Type = FinancialAccountType.Cash };
        var fund = new FinancialAccount { CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Fondo", Type = FinancialAccountType.ReserveFund };
        env.T.Db.FinancialAccounts.AddRange(cash, fund);
        env.T.Db.SaveChanges();
        env.SeedTemplate();

        var roles = await Service(env).RolesAsync(await Context(env), canEdit: true, default);

        Assert.True(roles.CanEdit);
        Assert.False(roles.IsComplete);
        Assert.All(roles.Accounts, a => Assert.Null(a.LedgerCategoryId));
        Assert.Equal(env.Cat("1.1.03").Id, roles.Accounts.Single(a => a.FinancialAccountId == bank.Id).SuggestedCategoryId);
        Assert.Equal(env.Cat("1.1.01").Id, roles.Accounts.Single(a => a.FinancialAccountId == cash.Id).SuggestedCategoryId);
        Assert.Equal(env.Cat("3.1.01").Id, roles.Accounts.Single(a => a.FinancialAccountId == fund.Id).SuggestedCategoryId);
        Assert.Equal(env.Cat("1.4.06").Id, roles.SuggestedVatCreditCategoryId);
        Assert.Null(roles.VatCreditCategoryId);
        Assert.Empty(env.T.NewContext().LedgerAccountRoles.ToList());
        // Solo se ofrecen cuentas finales de activo o de fondos y activas.
        Assert.All(roles.Options, o => Assert.True(o.Type is LedgerCategoryType.Asset or LedgerCategoryType.Fund));
        Assert.DoesNotContain(roles.Options, o => o.Code is "1" or "1.1" or "4.1.01" or "5.01.01");
        Assert.Contains(roles.Options, o => o.Code == "1.1.03");
    }

    [Fact]
    public async Task SeAsignanLasCuentasDelPlanYQuedaEnElHistorial()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        env.SeedTemplate();
        var ctx = await Context(env);

        var (roles, error) = await Service(env).UpdateRolesAsync(ctx, new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = env.Cat("1.4.06").Id
        }, env.Company.Id, default);

        Assert.Null(error);
        Assert.True(roles!.IsComplete);
        Assert.Equal("1.1.03", roles.Accounts.Single().Code);
        Assert.Equal("1.4.06", roles.VatCreditCode);
        Assert.Null(roles.Accounts.Single().SuggestedCategoryId);
        var saved = env.T.NewContext().LedgerAccountRoles.Where(x => !x.IsDeleted).ToList();
        Assert.Equal(2, saved.Count);
        var log = Assert.Single(env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Accounting).ToList());
        Assert.Equal("Updated", log.Action);
        Assert.Equal(env.Admin.Id, log.UserId);
        Assert.Contains("2 contrapartidas", log.Summary);
        Assert.Contains("1.1.03", log.ChangesJson);
    }

    [Fact]
    public async Task CambiarQuitarOGuardarLoMismoNoDuplicaNiEnsuciaElHistorial()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        env.SeedTemplate();
        var service = Service(env);
        var ctx = await Context(env);
        await service.UpdateRolesAsync(ctx, Assign(bank.Id, env.Cat("1.1.03").Id), env.Company.Id, default);

        // Lo mismo otra vez: no cambia nada ni deja otra fila de historial.
        await service.UpdateRolesAsync(ctx, Assign(bank.Id, env.Cat("1.1.03").Id), env.Company.Id, default);
        Assert.Single(env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Accounting).ToList());

        // Cambiar: reutiliza la fila.
        var (changed, _) = await service.UpdateRolesAsync(ctx, Assign(bank.Id, env.Cat("1.1.04").Id), env.Company.Id, default);
        Assert.Equal("1.1.04", changed!.Accounts.Single().Code);
        Assert.Single(env.T.NewContext().LedgerAccountRoles.ToList());

        // Quitar: queda deshecha (no cuenta) y la cuenta puede volver a asignarse.
        var (cleared, _) = await service.UpdateRolesAsync(ctx, Assign(bank.Id, null), env.Company.Id, default);
        Assert.Null(cleared!.Accounts.Single().LedgerCategoryId);
        Assert.Empty(env.T.NewContext().LedgerAccountRoles.Where(x => !x.IsDeleted).ToList());
        var (again, _) = await service.UpdateRolesAsync(ctx, Assign(bank.Id, env.Cat("1.1.03").Id), env.Company.Id, default);
        Assert.Equal("1.1.03", again!.Accounts.Single().Code);
        Assert.Equal(4, env.T.NewContext().FinanceAuditLogs.Count(x => x.Section == ConfigSectionKeys.Accounting && x.Action == "Updated"));
    }

    [Fact]
    public async Task LaAsignacionSeValidaAntesDeGuardar()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        env.SeedTemplate();
        var service = Service(env);
        var ctx = await Context(env);

        // Una cuenta de ingresos, un grupo, una de otro edificio y una inexistente no sirven de contrapartida.
        Assert.Contains("activo o de fondos", (await service.UpdateRolesAsync(ctx, Assign(bank.Id, env.Cat("4.1.01").Id), env.Company.Id, default)).Error);
        Assert.Contains("activo o de fondos", (await service.UpdateRolesAsync(ctx, Assign(bank.Id, env.Cat("1.1").Id), env.Company.Id, default)).Error);
        Assert.Contains("activo o de fondos", (await service.UpdateRolesAsync(ctx, Assign(bank.Id, Guid.NewGuid()), env.Company.Id, default)).Error);
        Assert.Contains("no existe", (await service.UpdateRolesAsync(ctx, Assign(Guid.NewGuid(), env.Cat("1.1.03").Id), env.Company.Id, default)).Error);

        var repeated = new UpdateAccountingRolesRequest
        {
            Accounts =
            [
                new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id },
                new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.04").Id }
            ]
        };
        Assert.Contains("repetida", (await service.UpdateRolesAsync(ctx, repeated, env.Company.Id, default)).Error);

        // El IVA credito tiene que ser de activo (no de fondos).
        var fundVat = new UpdateAccountingRolesRequest { SetVatCredit = true, VatCreditCategoryId = env.Cat("3.1.01").Id };
        Assert.Contains("activo", (await service.UpdateRolesAsync(ctx, fundVat, env.Company.Id, default)).Error);

        Assert.Empty(env.T.NewContext().LedgerAccountRoles.ToList());
        Assert.Empty(env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Accounting).ToList());
    }

    [Fact]
    public async Task UnaCuentaInactivaDelPlanSeAceptaComoContrapartidaYSeOfreceDespuesDeLasActivas()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        env.SeedTemplate();
        var vat = env.Cat("1.4.06");
        Assert.False(vat.IsActive);                                           // el IVA credito fiscal viene sin activar en la plantilla

        var (roles, error) = await Service(env).UpdateRolesAsync(await Context(env), new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = vat.Id
        }, env.Company.Id, default);

        Assert.Null(error);
        Assert.True(roles!.IsComplete);
        Assert.Equal("1.4.06", roles.VatCreditCode);
        Assert.False(roles.Options.Single(o => o.Code == "1.4.06").IsActive);
        var codes = roles.Options.Select(o => (o.IsActive, o.Code)).ToList();
        Assert.Equal(codes.OrderByDescending(c => c.IsActive).ThenBy(c => c.Code, StringComparer.Ordinal), codes);
    }

    [Fact]
    public async Task SoloAdministradorYSuperAdminAsignanYLosCuatroRolesVen()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        env.SeedTemplate();
        var api = Api(env);
        var request = new UpdateAccountingRolesRequest { Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }] };

        foreach (var role in new[] { "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            Assert.False(FinanceEnv.Ok(await api.GetRoles(env.Building.Id, default)).CanEdit);
            var denied = await api.UpdateRoles(env.Building.Id, request, default);
            Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>(denied.Result).StatusCode);
        }

        env.LoginAs("CompanyAdmin");
        Assert.True(FinanceEnv.Ok(await api.GetRoles(env.Building.Id, default)).CanEdit);
        Assert.Equal("1.1.03", FinanceEnv.Ok(await api.UpdateRoles(env.Building.Id, request, default)).Accounts.Single().Code);

        env.LoginAs("SuperAdmin");
        Assert.True(FinanceEnv.Ok(await api.GetRoles(env.Building.Id, default)).CanEdit);

        var bad = await api.UpdateRoles(env.Building.Id, Assign(Guid.NewGuid(), null), default);
        Assert.IsType<BadRequestObjectResult>(bad.Result);
    }

    [Fact]
    public async Task UnEdificioAjenoNoVeNiAsignaNiPideAsientos()
    {
        using var env = new FinanceEnv();
        env.Setup();
        var api = Api(env);
        env.LoginAs("CompanyAdmin");
        env.Access.Buildings.Clear();

        Assert.IsNotType<OkObjectResult>((await api.GetRoles(env.Building.Id, default)).Result);
        Assert.IsNotType<OkObjectResult>((await api.UpdateRoles(env.Building.Id, new UpdateAccountingRolesRequest(), default)).Result);
        Assert.IsNotType<OkObjectResult>((await api.GetEntries(env.Building.Id, null, null, default)).Result);
        Assert.IsNotType<FileContentResult>(await api.ExportEntries(env.Building.Id, null, null, default));
    }

    [Fact]
    public async Task ElResumenDelCentroMuestraLosAsientosComoOpcionalesYCompletosAlAsignarTodo()
    {
        using var env = new FinanceEnv();
        var bank = env.Setup();
        env.SeedTemplate();
        env.Tenant.CompanyId = env.Company.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var config = new BuildingConfigController(env.T.Db, env.Access, env.Tenant, new BuildingConfigOverviewService(env.T.Db, new FinanceModuleGate(env.T.Db)));

        var open = FinanceEnv.Ok(await config.GetOverview(env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Accounting);
        Assert.Equal(ConfigSectionStatus.Optional, open.Status);
        Assert.False(open.Required);
        Assert.Equal("finance", open.LinkKind);
        Assert.Equal("accounting", open.LinkTab);
        Assert.Equal(2, open.Reasons.Count);
        Assert.Contains(open.Summary, i => i.Label == "Cuentas financieras con cuenta del plan" && i.Value == "0 de 1");

        await Service(env).UpdateRolesAsync(await Context(env), new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = env.Cat("1.4.06").Id
        }, env.Company.Id, default);

        var done = FinanceEnv.Ok(await config.GetOverview(env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Accounting);
        Assert.Equal(ConfigSectionStatus.Complete, done.Status);
        Assert.Empty(done.Reasons);
        Assert.Contains(done.Summary, i => i.Label == "Cuentas financieras con cuenta del plan" && i.Value == "1 de 1");
        Assert.Contains(done.Summary, i => i.Label == "IVA crédito fiscal" && i.Value == "Asignado");
    }

    [Fact]
    public async Task ElRangoDeAsientosSeValida()
    {
        using var env = new FinanceEnv();
        env.Setup();
        env.SeedTemplate();
        var ctx = await Context(env);
        var service = Service(env);
        var today = FinancePeriods.Today();

        Assert.Contains("posterior", (await service.EntriesAsync(ctx, today, today.AddDays(-1), default)).Error);
        Assert.Contains("400", (await service.EntriesAsync(ctx, today.AddDays(-401), today, default)).Error);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Asientos del libro real y paquete del contador (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    private sealed record Fixture(FinancialAccount Bank, BuildingIncome Income, BuildingExpense Expense, BuildingExpense Exempt);

    // Un banco con tres movimientos: un ingreso de 1.000.000, un gasto de 110.000 con IVA 10 % y otro de 50.000 exento.
    private static Fixture Seed(FinanceEnv env, bool assign = true)
    {
        var bank = env.Setup();
        env.SeedTemplate();
        var income = env.AddIncome(null, 1_000_000m, FinanceEnv.Yesterday.AddDays(-10));
        var expense = env.AddExpense(null, 110_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-6));
        var exempt = env.AddExpense(null, 50_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-3));
        env.T.Db.BuildingExpenses.Single(x => x.Id == expense.Id).VatRate = 10m;
        env.T.Db.BuildingExpenses.Single(x => x.Id == exempt.Id).VatRate = 0m;
        env.T.Db.SaveChanges();
        if (assign)
        {
            var (_, error) = Service(env).UpdateRolesAsync(Context(env).GetAwaiter().GetResult(), new UpdateAccountingRolesRequest
            {
                Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
                SetVatCredit = true,
                VatCreditCategoryId = env.Cat("1.4.06").Id
            }, env.Company.Id, default).GetAwaiter().GetResult();
            Assert.Null(error);
        }

        return new Fixture(bank, income, expense, exempt);
    }

    [SqlServerFact]
    public async Task LosAsientosDelLibroCuadranYLaCuentaDelBancoEsElMovimientoNetoDelLibro()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);

        var (dto, error) = await Service(env).EntriesAsync(await Context(env), FinanceEnv.Yesterday.AddDays(-12), FinanceEnv.Yesterday, default);

        Assert.Null(error);
        Assert.Equal(3, dto!.EntryCount);
        Assert.Equal(0, dto.IncompleteCount);
        Assert.Equal(dto.TotalDebit, dto.TotalCredit);
        Assert.Empty(dto.Pending);
        Assert.Equal([1, 2, 3], dto.Entries.Select(e => e.Number).ToArray());
        Assert.Equal(dto.Entries.OrderBy(e => e.Date).Select(e => e.Date), dto.Entries.Select(e => e.Date));
        Assert.All(dto.Entries, e => Assert.Equal(e.Debit, e.Credit));

        var bank = dto.Totals.Single(t => t.Code == "1.1.03");
        Assert.Equal(1_000_000m - 110_000m - 50_000m, bank.Debit - bank.Credit);       // = lo que el libro movio en el banco
        Assert.Equal(10_000m, dto.Totals.Single(t => t.Code == "1.4.06").Debit);       // IVA de la factura con 10 %; el exento no suma

        var withVat = dto.Entries.Single(e => e.SourceId == f.Expense.Id);
        Assert.Equal(LedgerSourceType.BuildingExpense, SourceOf(withVat));
        Assert.Equal(110_000m, withVat.Lines.Single(l => l.Kind == "Financial").Credit);
        Assert.Equal(10_000m, withVat.Lines.Single(l => l.Kind == "VatCredit").Debit);
        Assert.Equal(100_000m, withVat.Lines.Single(l => l.Kind == "Rubro").Debit);
        Assert.DoesNotContain(dto.Entries.Single(e => e.SourceId == f.Exempt.Id).Lines, l => l.Kind == "VatCredit");
        Assert.Equal(1_000_000m, dto.Entries.Single(e => e.SourceId == f.Income.Id).Lines.Single(l => l.Kind == "Financial").Debit);
    }

    private static LedgerSourceType SourceOf(AccountingEntryDto entry) => entry.SourceType;

    [SqlServerFact]
    public async Task SinContrapartidasLosAsientosSalenIncompletosYElResumenDiceQueFalta()
    {
        using var env = new FinanceEnv(sqlServer: true);
        Seed(env, assign: false);

        var (dto, _) = await Service(env).EntriesAsync(await Context(env), FinanceEnv.Yesterday.AddDays(-12), FinanceEnv.Yesterday, default);

        Assert.Equal(3, dto!.EntryCount);
        Assert.Equal(3, dto.IncompleteCount);
        Assert.Contains("Cuenta financiera: Banco", dto.Pending);
        Assert.Contains("IVA crédito fiscal", dto.Pending);
        Assert.Equal(dto.TotalDebit, dto.TotalCredit);
        Assert.Contains(dto.Totals, t => t.Missing);
    }

    [SqlServerFact]
    public async Task ElRangoSeAcotaAlArranqueDelModuloYLoAnteriorNoGeneraAsientos()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var ctx = await Context(env);

        // Solo el gasto con IVA entra en el rango de un dia.
        var (one, _) = await Service(env).EntriesAsync(ctx, f.Expense.ExpenseDate, f.Expense.ExpenseDate, default);
        Assert.Equal(1, one!.EntryCount);

        var (before, _) = await Service(env).EntriesAsync(ctx, ctx.StartDate.AddDays(-20), ctx.StartDate.AddDays(-10), default);
        Assert.Equal(0, before!.EntryCount);
    }

    [SqlServerFact]
    public async Task ElExcelDeAsientosTraeLosRenglonesLasSumasYAvisaSiHayIncompletos()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var f = Seed(env);
        var api = Api(env);

        var file = Assert.IsType<FileContentResult>(await api.ExportEntries(env.Building.Id, FinanceEnv.Yesterday.AddDays(-12), FinanceEnv.Yesterday, default));

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Contains("asientos", file.FileDownloadName);
        using (var wb = new XLWorkbook(new MemoryStream(file.FileContents)))
        {
            var ws = wb.Worksheet("Asientos");
            Assert.Equal("Debe", ws.Cell(4, 6).GetString());
            Assert.Equal("Completo", ws.Cell(5, 11).GetString());
            Assert.Equal(1_000_000m + 110_000m + 50_000m, ws.Cell(5 + 7, 6).GetValue<decimal>());   // 7 renglones y luego el total
            Assert.Equal(1_160_000m, ws.Cell(5 + 7, 7).GetValue<decimal>());
            var sums = wb.Worksheet("Sumas por cuenta");
            Assert.Contains(sums.CellsUsed().Select(c => c.GetString()), t => t == "1.1.03");
        }

        // Sin contrapartidas el Excel lo avisa.
        env.T.Db.LedgerAccountRoles.RemoveRange(env.T.Db.LedgerAccountRoles.ToList());
        env.T.Db.SaveChanges();
        var incomplete = Assert.IsType<FileContentResult>(await api.ExportEntries(env.Building.Id, FinanceEnv.Yesterday.AddDays(-12), FinanceEnv.Yesterday, default));
        using var wb2 = new XLWorkbook(new MemoryStream(incomplete.FileContents));
        Assert.Equal("Incompleto", wb2.Worksheet("Asientos").Cell(5, 11).GetString());
        Assert.Contains(wb2.Worksheet("Asientos").CellsUsed().Select(c => c.GetString()), t => t.StartsWith("Atención: 3 asientos están incompletos"));
        _ = f;
    }

    [SqlServerFact]
    public async Task ElPaqueteDelContadorTraeLosAsientosLasSumasYElLibroDeCompras()
    {
        using var env = new FinanceEnv(sqlServer: true);
        Seed(env);
        env.Tenant.CompanyId = env.Company.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var db = env.T.Db;
        var ledger = new FinanceLedgerService(db);
        var budgets = new FinanceBudgetService(db, ledger);
        var controller = new FinanceExportController(
            db, env.Access, env.Tenant, new FinanceModuleGate(db), ledger, new FinanceReportService(db, ledger, budgets),
            new FinanceVatService(db), Service(env));

        var file = Assert.IsType<FileContentResult>(await controller.AccountantPack(env.Building.Id, null, default));

        using var wb = new XLWorkbook(new MemoryStream(file.FileContents));
        var names = wb.Worksheets.Select(w => w.Name).ToList();
        Assert.Contains("Asientos", names);
        Assert.Contains("Sumas por cuenta", names);
        Assert.Contains("Libro de compras", names);
        Assert.True(names.IndexOf("Movimientos") < names.IndexOf("Asientos"));
        var summary = string.Join("|", wb.Worksheet(1).CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Asientos", summary);
        Assert.Contains("Libro de compras", summary);
        // El libro de compras del paquete trae la factura con IVA 10 %.
        var purchases = wb.Worksheet("Libro de compras");
        Assert.Contains(purchases.CellsUsed().Select(c => c.GetString()), t => t == "10%");
    }
}
