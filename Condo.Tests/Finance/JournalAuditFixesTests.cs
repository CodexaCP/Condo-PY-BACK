using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Correcciones de la auditoria del Libro Diario: aviso de gastos que el libro no toma, aviso de correcciones en mes cerrado, version de fila
/// de «asentado hasta» (dos asentados a la vez no se pisan) y conflictos de otros errores de base que no se disfrazan.
/// Usan SQL Server real (se omiten sin CONDO_TEST_SQLSERVER).
/// </summary>
public class JournalAuditFixesTests
{
    private static async Task<(FinanceEnv Env, FinancialAccount Bank)> Seeded()
    {
        var env = new FinanceEnv(sqlServer: true);
        var bank = env.Setup();
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = ExpensePeriodStatus.Published;
        env.T.Db.SaveChanges();
        env.SeedTemplate();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var accounting = new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);
        var (_, error) = await accounting.UpdateRolesAsync(await Ctx(env), new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = env.Cat("1.4.06").Id
        }, env.Company.Id, default);
        Assert.Null(error);
        return (env, bank);
    }

    private static async Task<LedgerContext> Ctx(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static JournalPostingService Journal(FinanceEnv env, CondoDbContext db) =>
        new(db, new AccountingEntriesService(db, new FinanceLedgerService(db), env.Tenant), env.Tenant);

    // ── Gastos que el libro no toma ───────────────────────────────────────────

    [SqlServerFact]
    public async Task LaVistaPreviaAvisaLosGastosQueElLibroNoTomaConSuMotivo()
    {
        var (env, _) = await Seeded();
        using var _e = env;
        env.AddExpense(null, 50_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-5));
        var reserve = env.AddExpense(null, 70_000m, BuildingExpenseCategory.ReserveFund, FinanceEnv.Yesterday.AddDays(-5));
        var unpaid = env.AddExpense(null, 30_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-5));
        env.T.Db.BuildingExpenses.Single(x => x.Id == unpaid.Id).DueDate = FinanceEnv.Yesterday.AddDays(10);
        var old = env.AddExpense(null, 20_000m, BuildingExpenseCategory.Other, FinancePeriods.Today().AddMonths(-5));
        env.T.Db.SaveChanges();

        var (preview, error) = await Journal(env, env.T.NewContext()).PreviewAsync(await Ctx(env), FinanceEnv.Yesterday, default);

        Assert.Null(error);
        Assert.Equal(1, preview!.EntryCount);   // solo el gasto normal llega a un asiento
        Assert.Equal(3, preview.ExcludedCount);
        Assert.Equal(JournalExcludedReason.ReserveNotPaidByFund, preview.Excluded.Single(x => x.SourceId == reserve.Id).Reason);
        Assert.Equal(JournalExcludedReason.UnpaidWithDueDate, preview.Excluded.Single(x => x.SourceId == unpaid.Id).Reason);
        Assert.Equal(JournalExcludedReason.BeforeStartDate, preview.Excluded.Single(x => x.SourceId == old.Id).Reason);
        Assert.Equal(JournalExcludedReason.ReserveNotPaidByFund, preview.Excluded[0].Reason);   // lo que hay que revisar va primero
        Assert.True(preview.CanPost);                                                            // es un aviso: no bloquea
    }

    [SqlServerFact]
    public async Task UnGastoDeReservaPagadoPorElFondoNoSeAvisaComoExcluido()
    {
        var (env, _) = await Seeded();
        using var _e = env;
        var expense = env.AddExpense(null, 70_000m, BuildingExpenseCategory.ReserveFund, FinanceEnv.Yesterday.AddDays(-5));
        env.T.Db.BuildingExpenses.Single(x => x.Id == expense.Id).PaidByReserveFund = true;
        env.T.Db.SaveChanges();

        var (preview, _) = await Journal(env, env.T.NewContext()).PreviewAsync(await Ctx(env), FinanceEnv.Yesterday, default);

        Assert.Equal(0, preview!.ExcludedCount);
    }

    // ── Correccion con fecha en un mes cerrado ────────────────────────────────

    [SqlServerFact]
    public async Task LaVistaPreviaAvisaSiLasCorreccionesCaenEnUnMesCerrado()
    {
        var (env, _) = await Seeded();
        using var _e = env;
        var expense = env.AddExpense(null, 40_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-9));
        var first = await Journal(env, env.T.NewContext()).PostAsync(await Ctx(env), FinanceEnv.Yesterday.AddDays(-1), env.Company.Id, default);
        Assert.Null(first.Error);

        var setup = env.T.NewContext();   // contexto nuevo: el de env tiene la version vieja de la configuracion
        setup.FinanceSettings.Single(x => x.BuildingId == env.Building.Id).PeriodClosingEnabled = true;
        setup.FinancePeriodClosures.Add(new FinancePeriodClosure
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, Year = FinanceEnv.Yesterday.Year, Month = FinanceEnv.Yesterday.Month,
            ClosedByUserId = env.Admin.Id
        });
        setup.BuildingExpenses.Single(x => x.Id == expense.Id).Amount = 45_000m;   // cambio por un camino sin guarda
        setup.SaveChanges();

        var (preview, _) = await Journal(env, env.T.NewContext()).PreviewAsync(await Ctx(env), FinanceEnv.Yesterday, default);

        Assert.Single(preview!.Differences);
        Assert.True(preview.ThroughInClosedMonth);
    }

    [SqlServerFact]
    public async Task SinCorreccionesNoHayAvisoDeMesCerrado()
    {
        var (env, _) = await Seeded();
        using var _e = env;
        env.AddExpense(null, 40_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-9));

        var (preview, _) = await Journal(env, env.T.NewContext()).PreviewAsync(await Ctx(env), FinanceEnv.Yesterday, default);

        Assert.False(preview!.ThroughInClosedMonth);
    }

    // ── Version de fila: dos asentados a la vez no se pisan ───────────────────

    [SqlServerFact]
    public async Task UnAsentadoConLaConfiguracionVencidaRecibeConflictoYNoGuardaNada()
    {
        var (env, _) = await Seeded();
        using var _e = env;
        env.AddExpense(null, 10_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-5));
        var ctx = await Ctx(env);

        // Dos personas leen la configuracion con la misma version; la primera asienta hasta ayer.
        var dbSlow = env.T.NewContext();
        var staleSettings = dbSlow.FinanceSettings.Single(x => x.BuildingId == env.Building.Id);
        var winner = await Journal(env, env.T.NewContext()).PostAsync(ctx, FinanceEnv.Yesterday, env.Company.Id, default);
        Assert.Null(winner.Error);

        // La segunda, con la version vieja, quiere asentar «hasta antes» sin movimientos (antes pisaba y hacia retroceder el dia).
        var (result, error, conflict) = await Journal(env, dbSlow)
            .PostBuiltAsync(ctx, staleSettings, [], FinanceEnv.Yesterday.AddDays(-6), env.Company.Id, default);

        Assert.Null(result);
        Assert.True(conflict);
        Assert.NotNull(error);
        Assert.Equal(FinanceEnv.Yesterday, env.T.NewContext().FinanceSettings.Single(x => x.BuildingId == env.Building.Id).JournalPostedThrough);
    }

    // ── Que error es un conflicto ─────────────────────────────────────────────

    [Fact]
    public void ElConflictoDeVersionYLosErroresSinDetalleSonConflictoDeAsentado()
    {
        Assert.True(JournalPostingService.IsConcurrencyConflict(new DbUpdateConcurrencyException("version")));
        Assert.True(JournalPostingService.IsConcurrencyConflict(new DbUpdateException("indice unico", new InvalidOperationException("sqlite"))));
    }

    [SqlServerFact]
    public async Task UnErrorDeBaseQueNoEsDeUnicidadNoSeDisfrazaDeConflicto()
    {
        var (env, _) = await Seeded();
        using var _e = env;
        env.AddExpense(null, 10_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-5));
        // Un trigger que rechaza todo INSERT en el Libro Diario simula un error de SQL Server que no es de unicidad.
        env.T.NewContext().Database.ExecuteSqlRaw(
            "CREATE TRIGGER [TR_Test_NoInsert] ON [JournalEntries] INSTEAD OF INSERT AS BEGIN THROW 50001, N'bloqueado', 1; END");

        var db = env.T.NewContext();
        var service = Journal(env, db);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => service.PostAsync(Ctx(env).Result, FinanceEnv.Yesterday, env.Company.Id, default));

        Assert.False(JournalPostingService.IsConcurrencyConflict(ex));
        Assert.Empty(env.T.NewContext().JournalEntries.Where(x => x.BuildingId == env.Building.Id));
    }

}
