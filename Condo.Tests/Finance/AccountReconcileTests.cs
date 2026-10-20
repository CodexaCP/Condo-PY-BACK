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
/// Conciliacion por cuenta: cada cuenta (banco, caja o fondo de reserva) se concilia o no segun lo que elija la empresa. Sin eleccion rige lo de
/// siempre: se concilian los bancos y no la caja ni el fondo. Sirve, por ejemplo, para conciliar un fondo de reserva que esta en una cuenta bancaria.
/// </summary>
public class AccountReconcileTests
{
    private static FinancialAccount Account(FinancialAccountType type, bool? over = null) =>
        new() { Name = "Cuenta", Type = type, ReconcileOverride = over };

    // ── Regla (pura) ─────────────────────────────────────────────────────────

    [Fact]
    public void SinEleccionSeConcilianLosBancosYNoLaCajaNiElFondo()
    {
        Assert.True(Account(FinancialAccountType.Bank).Reconciles);
        Assert.False(Account(FinancialAccountType.Cash).Reconciles);
        Assert.False(Account(FinancialAccountType.ReserveFund).Reconciles);
    }

    [Fact]
    public void LaEleccionDeLaEmpresaPuedeMasQueElTipo()
    {
        Assert.True(Account(FinancialAccountType.ReserveFund, true).Reconciles);
        Assert.True(Account(FinancialAccountType.Cash, true).Reconciles);
        Assert.False(Account(FinancialAccountType.Bank, false).Reconciles);
    }

    // ── Alta y edicion de la cuenta ──────────────────────────────────────────

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

    private static FinancialAccountUpsertRequest Upsert(FinanceEnv env, string name, FinancialAccountType type, bool? reconciles = null, bool active = true) =>
        new() { BuildingId = env.Building.Id, Name = name, Type = type, IsActive = active, Reconciles = reconciles };

    [Fact]
    public async Task UnaCuentaNuevaSeConciliaSegunSuTipoSalvoQueSeElijaOtraCosa()
    {
        using var env = NewEnv();
        var api = Accounts(env);

        var bank = FinanceEnv.Ok(await api.Create(Upsert(env, "Banco B", FinancialAccountType.Bank), default));
        var cash = FinanceEnv.Ok(await api.Create(Upsert(env, "Caja", FinancialAccountType.Cash), default));
        var fund = FinanceEnv.Ok(await api.Create(Upsert(env, "Fondo", FinancialAccountType.ReserveFund, reconciles: true), default));
        var offBank = FinanceEnv.Ok(await api.Create(Upsert(env, "Banco sin extracto", FinancialAccountType.Bank, reconciles: false), default));

        Assert.True(bank.Reconciles);
        Assert.False(cash.Reconciles);
        Assert.True(fund.Reconciles);
        Assert.False(offBank.Reconciles);
        var saved = env.T.NewContext().FinancialAccounts.ToDictionary(x => x.Name);
        Assert.Null(saved["Banco B"].ReconcileOverride);                     // sin eleccion: queda lo del tipo
        Assert.Equal(true, saved["Fondo"].ReconcileOverride);
        Assert.Equal(false, saved["Banco sin extracto"].ReconcileOverride);
    }

    [Fact]
    public async Task LaListaDeCuentasDiceCualesSeConcilian()
    {
        using var env = NewEnv();
        var api = Accounts(env);
        FinanceEnv.Ok(await api.Create(Upsert(env, "Fondo", FinancialAccountType.ReserveFund, reconciles: true), default));

        var list = (Assert.IsType<OkObjectResult>((await api.GetAll(env.Building.Id, default)).Result).Value as IReadOnlyList<FinancialAccountDto>)!;

        Assert.Equal(["Banco", "Fondo"], list.Where(a => a.Reconciles).Select(a => a.Name).ToArray());
    }

    [Fact]
    public async Task EditarCambiaLaEleccionLaDejaIgualSiNoSeEnviaYQuedaEnElHistorial()
    {
        using var env = NewEnv();
        var api = Accounts(env);
        var fund = FinanceEnv.Ok(await api.Create(Upsert(env, "Fondo", FinancialAccountType.ReserveFund), default));
        Assert.False(fund.Reconciles);

        var on = FinanceEnv.Ok(await api.Update(fund.Id, Upsert(env, "Fondo", FinancialAccountType.ReserveFund, reconciles: true), default));
        Assert.True(on.Reconciles);
        Assert.Equal(true, env.T.NewContext().FinancialAccounts.Single(x => x.Id == fund.Id).ReconcileOverride);   // quedo guardado en la base, no solo en la respuesta
        Assert.True(env.T.NewContext().FinancialAccounts.Single(x => x.Id == fund.Id).Reconciles);
        var kept = FinanceEnv.Ok(await api.Update(fund.Id, Upsert(env, "Fondo renombrado", FinancialAccountType.ReserveFund), default));   // no manda la casilla
        Assert.True(kept.Reconciles);

        var audit = env.T.NewContext().FinanceAuditLogs.Where(x => x.EntityType == "FinancialAccount" && x.Action == "Updated").ToList();
        Assert.Contains(audit, a => a.ChangesJson.Contains("reconciles"));
        Assert.Contains(audit, a => a.Summary.Contains("Se concilia con extracto"));
    }

    [Fact]
    public async Task NoSeDejaDeConciliarUnaCuentaConUnaConciliacionAbierta()
    {
        using var env = NewEnv();
        var api = Accounts(env);
        var bank = env.T.Db.FinancialAccounts.Single(x => x.BuildingId == env.Building.Id);
        env.T.Db.BankReconciliations.Add(new BankReconciliation
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, AccountId = bank.Id, StatementDate = FinanceEnv.Yesterday,
            StatementBalance = 0m, CreatedByUserId = env.Admin.Id
        });
        env.T.Db.SaveChanges();

        var blocked = await api.Update(bank.Id, Upsert(env, bank.Name, bank.Type, reconciles: false), default);

        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeActionResult>(blocked.Result).StatusCode);
        Assert.True(env.T.NewContext().FinancialAccounts.Single(x => x.Id == bank.Id).Reconciles);

        env.T.Db.BankReconciliations.Single().Status = BankReconciliationStatus.Completed;     // cerrada: ya se puede dejar de conciliar
        env.T.Db.SaveChanges();
        Assert.False(FinanceEnv.Ok(await api.Update(bank.Id, Upsert(env, bank.Name, bank.Type, reconciles: false), default)).Reconciles);
    }

    // ── Conciliacion (con la base real) ──────────────────────────────────────

    private static BankReconciliationService Recon(FinanceEnv env) =>
        new(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);

    private static async Task<LedgerContext> Ctx(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static StartReconciliationRequest Start(FinanceEnv env, Guid accountId) =>
        new() { BuildingId = env.Building.Id, AccountId = accountId, StatementDate = FinancePeriods.Today(), StatementBalance = 0m };

    [SqlServerFact]
    public async Task ElFondoDeReservaMarcadoSeConciliaYSusMovimientosSePuedenTildar()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        var fund = new FinancialAccount
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Fondo en el banco", Type = FinancialAccountType.ReserveFund,
            OpeningBalance = 1_000_000m, ReconcileOverride = true
        };
        env.T.Db.FinancialAccounts.Add(fund);
        env.T.Db.SaveChanges();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);

        // Un aporte de 400.000 del banco al fondo: es un movimiento del fondo que tiene que figurar en su extracto.
        var ledger = new FinanceLedgerService(env.T.Db);
        var transfers = new FinanceTransferService(env.T.Db, ledger, env.Tenant);
        var created = await transfers.CreateAsync(await Ctx(env), new CreateFinanceTransferRequest
        {
            FromAccountId = bank.Id, ToAccountId = fund.Id, Amount = 400_000m, SupportReference = "Acta 1", ConfirmNegativeBalance = true
        }, env.Company.Id, default);
        Assert.True(created.Ok, created.Message);

        var overview = await Recon(env).OverviewAsync(await Ctx(env), true, default);
        var row = overview.Accounts.Single(a => a.AccountId == fund.Id);
        Assert.Equal(FinancialAccountType.ReserveFund, row.AccountType);
        Assert.Equal(1_400_000m, row.BookBalance);

        var started = await Recon(env).StartAsync(await Ctx(env), Start(env, fund.Id), env.Company.Id, default);
        Assert.True(started.Ok, started.Message);
        var workspace = started.Value!;
        Assert.Equal(1_000_000m, workspace.OpeningBalance);
        Assert.Equal(400_000m, workspace.PendingIn);                                  // el aporte, todavia sin tildar
        Assert.Equal(1, workspace.PendingCount);
    }

    [SqlServerFact]
    public async Task UnBancoMarcadoParaNoConciliarSeSaleDeLaListaYNoSePuedeEmpezar()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        env.T.Db.FinancialAccounts.Single(x => x.Id == bank.Id).ReconcileOverride = false;
        env.T.Db.SaveChanges();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);

        var overview = await Recon(env).OverviewAsync(await Ctx(env), true, default);
        var started = await Recon(env).StartAsync(await Ctx(env), Start(env, bank.Id), env.Company.Id, default);

        Assert.Empty(overview.Accounts);
        Assert.Equal("account_not_reconcilable", started.Code);
        Assert.Empty(env.T.NewContext().BankReconciliations.ToList());
    }

    [SqlServerFact]
    public async Task LaCajaMarcadaParaConciliarAparece()
    {
        using var env = new FinanceEnv(sqlServer: true);
        env.Setup();
        env.T.Db.FinancialAccounts.Add(new FinancialAccount
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, Name = "Caja", Type = FinancialAccountType.Cash, ReconcileOverride = true
        });
        env.T.Db.SaveChanges();

        var overview = await Recon(env).OverviewAsync(await Ctx(env), true, default);

        Assert.Equal(["Banco", "Caja"], overview.Accounts.Select(a => a.AccountName).OrderBy(n => n).ToArray());
    }
}
