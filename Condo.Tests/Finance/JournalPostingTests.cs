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
/// Libro Diario, fase 1: asientos asentados con numero correlativo por edificio y ejercicio, sin huecos, que no se vuelven a asentar y que
/// se guardan todo-o-nada. La guarda y la numeracion se prueban con asientos armados a mano (sin leer el libro); el asentado de punta a punta,
/// que lee el libro de Finanzas, usa SQL Server real y se omite solo sin la variable CONDO_TEST_SQLSERVER.
/// </summary>
public class JournalPostingTests
{
    private static readonly Guid BankAccountId = Guid.NewGuid();
    private static readonly Guid RubroCat = Guid.NewGuid();
    private static readonly DateOnly D1 = new(2026, 3, 5);
    private static readonly DateOnly D2 = new(2026, 3, 10);
    private static readonly DateOnly D3 = new(2026, 3, 20);

    // ── Armado ───────────────────────────────────────────────────────────────

    private static AccountingEntryDto Entry(DateOnly date, decimal amount = 100_000m, Guid? source = null, bool complete = true, string description = "Expensas")
    {
        var lines = new[]
        {
            new AccountingLineDto { CategoryId = Guid.NewGuid(), Code = complete ? "1.1.03" : "", Name = "Banco", Debit = amount, Kind = "Financial", Missing = !complete },
            new AccountingLineDto { CategoryId = RubroCat, Code = "4.1.01", ExternalCode = "401", Name = "Expensas ordinarias", Credit = amount, Kind = "Rubro" }
        };
        return new AccountingEntryDto
        {
            Date = date, Description = description, ThirdParty = "Unidad A", Reference = "R-1",
            SourceType = LedgerSourceType.OwnerPayment, SourceId = source ?? Guid.NewGuid(), SourceAccountId = BankAccountId,
            Lines = lines, Debit = amount, Credit = amount, IsComplete = complete
        };
    }

    private static FinanceEnv NewEnv()
    {
        var env = new FinanceEnv();
        env.Setup();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        return env;
    }

    private static JournalPostingService Journal(FinanceEnv env, CondoDbContextHolder? db = null) =>
        new(db?.Db ?? env.T.Db, new AccountingEntriesService(db?.Db ?? env.T.Db, new FinanceLedgerService(db?.Db ?? env.T.Db), env.Tenant), env.Tenant);

    // Un contexto propio: despues de un guardado fallido el contexto queda con cambios sin guardar, como pasaria en una peticion perdida.
    private sealed class CondoDbContextHolder(Condo.Infrastructure.Persistence.CondoDbContext db)
    {
        public Condo.Infrastructure.Persistence.CondoDbContext Db { get; } = db;
    }

    private static async Task<LedgerContext> Context(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static async Task<(PostJournalResultDto? Result, string? Error, bool Conflict)> Post(
        FinanceEnv env, IReadOnlyList<AccountingEntryDto> entries, DateOnly through)
    {
        var holder = new CondoDbContextHolder(env.T.NewContext());
        var settings = holder.Db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id);
        return await Journal(env, holder).PostBuiltAsync(await Context(env), settings, entries, through, env.Company.Id, default);
    }

    private static List<JournalEntry> Saved(FinanceEnv env) =>
        env.T.NewContext().JournalEntries.Include(x => x.Lines).Where(x => x.BuildingId == env.Building.Id)
            .OrderBy(x => x.FiscalYear).ThenBy(x => x.Number).ToList();

    private static DateOnly? PostedThrough(FinanceEnv env) =>
        env.T.NewContext().FinanceSettings.Single(x => x.BuildingId == env.Building.Id).JournalPostedThrough;

    // ═════════════════════════════════════════════════════════════════════════
    // Numeracion y reglas puras
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ElNumeroSeMuestraConEjercicioYSeisCifras()
    {
        Assert.Equal("2026-000001", JournalPostingService.LabelOf(2026, 1));
        Assert.Equal("2027-000342", JournalPostingService.LabelOf(2027, 342));
    }

    [Fact]
    public void LosNumerosSonCorrelativosPorEjercicioYReinicianAlCambiarDeEjercicio()
    {
        var entries = new[] { Entry(new DateOnly(2026, 12, 29)), Entry(new DateOnly(2026, 12, 30)), Entry(new DateOnly(2027, 1, 2)), Entry(new DateOnly(2027, 1, 3)) };

        var plan = JournalPostingService.Allocate(entries, 1, new Dictionary<int, int>());

        Assert.Equal(["2026-000001", "2026-000002", "2027-000001", "2027-000002"], plan.Select(p => JournalPostingService.LabelOf(p.FiscalYear, p.Number)).ToArray());
    }

    [Fact]
    public void LaNumeracionContinuaDesdeElUltimoUsadoDelEjercicio()
    {
        var plan = JournalPostingService.Allocate([Entry(D1), Entry(D2)], 1, new Dictionary<int, int> { [2026] = 41, [2025] = 900 });

        Assert.Equal([42, 43], plan.Select(p => p.Number).ToArray());
    }

    [Fact]
    public void ConEjercicioQueEmpiezaEnJulioMarzoYSeptiembreCaenEnEjerciciosDistintos()
    {
        var plan = JournalPostingService.Allocate([Entry(new DateOnly(2026, 3, 1)), Entry(new DateOnly(2026, 9, 1))], 7, new Dictionary<int, int>());

        Assert.Equal([2025, 2026], plan.Select(p => p.FiscalYear).ToArray());
        Assert.Equal([1, 1], plan.Select(p => p.Number).ToArray());
    }

    [Fact]
    public void ElDiaHastaElQueSeAsientaSeValida()
    {
        var today = new DateOnly(2026, 10, 19);
        var start = new DateOnly(2026, 1, 1);

        Assert.Null(JournalPostingService.ValidateThrough(null, new DateOnly(2026, 3, 31), start, today));
        Assert.Null(JournalPostingService.ValidateThrough(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), start, today));
        Assert.Contains("futura", JournalPostingService.ValidateThrough(null, today.AddDays(1), start, today));
        Assert.Contains("arranque", JournalPostingService.ValidateThrough(null, new DateOnly(2025, 12, 31), start, today));
        Assert.Null(JournalPostingService.ValidateThrough(new DateOnly(2026, 3, 31), new DateOnly(2026, 3, 31), start, today));   // el mismo dia sirve para asentar solo las correcciones
        Assert.Contains("Ya está asentado", JournalPostingService.ValidateThrough(new DateOnly(2026, 3, 31), new DateOnly(2026, 3, 1), start, today));
    }

    [Fact]
    public void LaHuellaDependeDelContenidoPeroNoDelOrdenDeLosRenglones()
    {
        var source = Guid.NewGuid();
        var a = Entry(D1, 100_000m, source);
        var b = Entry(D1, 100_000m, source);
        var reordered = Entry(D1, 100_000m, source);
        reordered.Lines = reordered.Lines.Reverse().ToList();

        Assert.Equal(64, JournalPostingService.HashOf(a).Length);
        Assert.Equal(JournalPostingService.HashOf(a), JournalPostingService.HashOf(b));
        Assert.Equal(JournalPostingService.HashOf(a), JournalPostingService.HashOf(reordered));
        Assert.NotEqual(JournalPostingService.HashOf(a), JournalPostingService.HashOf(Entry(D1, 100_001m, source)));
        Assert.NotEqual(JournalPostingService.HashOf(a), JournalPostingService.HashOf(Entry(D2, 100_000m, source)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Asentar (guarda)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsentarGuardaLosAsientosNumeradosConSusRenglonesYAvanzaElAsentadoHasta()
    {
        using var env = NewEnv();

        var (result, error, conflict) = await Post(env, [Entry(D1, 100_000m, description: "Cobro A"), Entry(D2, 250_000m, description: "Cobro B")], D3);

        Assert.Null(error);
        Assert.False(conflict);
        Assert.Equal(2, result!.PostedCount);
        Assert.Equal("2026-000001", result.FirstLabel);
        Assert.Equal("2026-000002", result.LastLabel);
        Assert.Equal(D3, result.PostedThrough);
        Assert.Equal(D3, PostedThrough(env));

        var saved = Saved(env);
        Assert.Equal([1, 2], saved.Select(e => e.Number).ToArray());
        Assert.All(saved, e =>
        {
            Assert.Equal(2026, e.FiscalYear);
            Assert.Equal(JournalEntryKind.Auto, e.Kind);
            Assert.Equal(e.TotalDebit, e.TotalCredit);
            Assert.Equal(e.Date, e.OperationDate);
            Assert.Equal(env.Company.Id, e.CompanyId);
            Assert.Equal(env.Admin.Id, e.PostedByUserId);
            Assert.Equal(64, e.ContentHash.Length);
            Assert.Equal((int)LedgerSourceType.OwnerPayment, e.SourceType);
            Assert.Equal(BankAccountId, e.SourceAccountId);
            Assert.Equal(0, e.Revision);
        });
        Assert.Equal("Cobro A", saved[0].Description);
        Assert.Equal(250_000m, saved[1].TotalDebit);

        var lines = saved[0].Lines.OrderBy(l => l.LineNo).ToList();
        Assert.Equal([1, 2], lines.Select(l => l.LineNo).ToArray());
        Assert.Equal(("1.1.03", 100_000m, 0m), (lines[0].Code, lines[0].Debit, lines[0].Credit));
        Assert.Equal(("4.1.01", 0m, 100_000m), (lines[1].Code, lines[1].Debit, lines[1].Credit));
        Assert.Equal("401", lines[1].ExternalCode);

        Assert.Equal(2, env.T.NewContext().JournalSequences.Single(x => x.BuildingId == env.Building.Id).LastNumber);
        var log = Assert.Single(env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Accounting && x.Action == "Posted").ToList());
        Assert.Contains("2026-000001", log.Summary);
        Assert.Contains("2026-000002", log.Summary);
        Assert.Equal(env.Admin.Id, log.UserId);
    }

    [Fact]
    public async Task LaSegundaTandaContinuaLaNumeracionSinHuecosNiRepetidos()
    {
        using var env = NewEnv();

        await Post(env, [Entry(D1), Entry(D1)], D2);
        var (second, error, _) = await Post(env, [Entry(D3), Entry(D3), Entry(D3)], D3.AddDays(5));

        Assert.Null(error);
        Assert.Equal("2026-000003", second!.FirstLabel);
        Assert.Equal("2026-000005", second.LastLabel);
        Assert.Equal([1, 2, 3, 4, 5], Saved(env).Select(e => e.Number).ToArray());
        Assert.Equal(5, env.T.NewContext().JournalSequences.Single().LastNumber);
        Assert.Equal(D3.AddDays(5), PostedThrough(env));
    }

    [Fact]
    public async Task AsentarUnPeriodoSinMovimientosAvanzaElAsentadoHastaSinCrearAsientos()
    {
        using var env = NewEnv();

        var (result, error, _) = await Post(env, [], D2);

        Assert.Null(error);
        Assert.Equal(0, result!.PostedCount);
        Assert.Null(result.FirstLabel);
        Assert.Equal(D2, PostedThrough(env));
        Assert.Empty(Saved(env));
        Assert.Empty(env.T.NewContext().JournalSequences.ToList());
        Assert.Contains("sin movimientos", Assert.Single(env.T.NewContext().FinanceAuditLogs.Where(x => x.Action == "Posted").ToList()).Summary);
    }

    [Fact]
    public async Task UnAsientoIncompletoImpideAsentarTodoYNoSeConsumeNingunNumero()
    {
        using var env = NewEnv();

        var (result, error, conflict) = await Post(env, [Entry(D1), Entry(D2, complete: false), Entry(D3)], D3);

        Assert.Null(result);
        Assert.True(conflict);
        Assert.Contains("1 asientos incompletos", error);
        Assert.Contains("Banco", error);
        Assert.Empty(Saved(env));
        Assert.Empty(env.T.NewContext().JournalSequences.ToList());
        Assert.Null(PostedThrough(env));
        Assert.Empty(env.T.NewContext().FinanceAuditLogs.Where(x => x.Action == "Posted").ToList());
    }

    [Fact]
    public async Task UnAsientoQueNoCuadraNoSeAsienta()
    {
        using var env = NewEnv();
        var broken = Entry(D1);
        broken.Credit = broken.Debit + 1m;

        var (result, error, conflict) = await Post(env, [Entry(D1), broken], D2);

        Assert.Null(result);
        Assert.True(conflict);
        Assert.Contains("no cuadra", error);
        Assert.Empty(Saved(env));
        Assert.Null(PostedThrough(env));
    }

    [Fact]
    public async Task ElMismoMovimientoNoSeAsientaDosVeces_YElFalloNoConsumeNumeros()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        var first = await Post(env, [Entry(D1, source: source)], D2);
        Assert.Null(first.Error);

        // Una segunda persona que todavia veia «nada asentado» intenta asentar el mismo cobro mas otro movimiento nuevo.
        var stale = new CondoDbContextHolder(env.T.NewContext());
        var settings = stale.Db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id);
        settings.JournalPostedThrough = null;
        var (result, error, conflict) = await Journal(env, stale).PostBuiltAsync(
            await Context(env), settings, [Entry(D1, source: source), Entry(D2)], D3, env.Company.Id, default);

        Assert.Null(result);
        Assert.True(conflict);
        Assert.Contains("al mismo tiempo", error);
        Assert.Equal([1], Saved(env).Select(e => e.Number).ToArray());            // el nuevo tampoco quedo: todo o nada
        Assert.Equal(1, env.T.NewContext().JournalSequences.Single().LastNumber);  // el numero 2 no se consumio
        Assert.Equal(D2, PostedThrough(env));
    }

    [Fact]
    public async Task SiLaSecuenciaQuedoAtrasadaElIndiceDelNumeroFrenaElAsentadoYNoSePisaNada()
    {
        using var env = NewEnv();
        await Post(env, [Entry(D1)], D2);

        // Simula a otra persona que asento entre la lectura y el guardado: la secuencia que se lee dice 0 pero el numero 1 ya existe.
        var rollback = env.T.NewContext();
        rollback.JournalSequences.Single().LastNumber = 0;
        rollback.SaveChanges();

        var (result, error, conflict) = await Post(env, [Entry(D3)], D3);

        Assert.Null(result);
        Assert.True(conflict);
        Assert.Contains("al mismo tiempo", error);
        Assert.Single(Saved(env));                       // sigue el unico asiento original
        Assert.Equal(D2, PostedThrough(env));            // y el asentado hasta no avanzo
    }

    [Fact]
    public void LaBaseNoDejaRepetirUnNumeroDentroDeUnEdificioYEjercicio()
    {
        using var env = NewEnv();
        JournalEntry Row(int year, int number, Guid building) => new()
        {
            CompanyId = env.Company.Id, BuildingId = building, FiscalYear = year, Number = number, Date = D1, OperationDate = D1,
            TotalDebit = 1m, TotalCredit = 1m, ContentHash = new string('a', 64)
        };
        var other = env.T.AddBuilding(env.Company, "Otro");
        env.T.Db.JournalEntries.AddRange(Row(2026, 1, env.Building.Id), Row(2027, 1, env.Building.Id), Row(2026, 1, other.Id));
        env.T.Db.SaveChanges();                          // mismo numero en otro ejercicio u otro edificio: valido

        var db = env.T.NewContext();
        db.JournalEntries.Add(Row(2026, 1, env.Building.Id));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task LosRenglonesGuardanCopiaDeLaCuentaYNoDependenDelPlan()
    {
        using var env = NewEnv();
        var entry = Entry(D1);
        entry.Lines[1].CategoryId = Guid.NewGuid();     // una cuenta que ya no existe en el plan: no hay llave foranea

        var (_, error, _) = await Post(env, [entry], D1);

        Assert.Null(error);
        var line = Saved(env).Single().Lines.Single(l => l.Kind == "Rubro");
        Assert.Equal("4.1.01", line.Code);
        Assert.Equal("Expensas ordinarias", line.Name);
        Assert.False(env.T.NewContext().LedgerCategories.Any(x => x.Id == line.LedgerCategoryId));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Lectura del Libro Diario
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ElLibroSeLeePorEjercicioConFiltroDeNumeroFechasYPaginas()
    {
        using var env = NewEnv();
        await Post(env, [Entry(new DateOnly(2025, 11, 5)), Entry(new DateOnly(2026, 2, 5), 200_000m), Entry(new DateOnly(2026, 3, 5), 300_000m), Entry(new DateOnly(2026, 4, 5), 400_000m)], new DateOnly(2026, 4, 30));
        var ctx = await Context(env);
        var service = Journal(env, new CondoDbContextHolder(env.T.NewContext()));

        var latest = await service.GetAsync(ctx, null, null, null, null, 1, 100, canPost: true, default);
        Assert.Equal(2026, latest.FiscalYear);
        Assert.Equal([2026, 2025], latest.FiscalYears.ToArray());
        Assert.Equal(["2026-000001", "2026-000002", "2026-000003"], latest.Entries.Select(e => e.Label).ToArray());
        Assert.Equal(900_000m, latest.TotalDebit);
        Assert.Equal(latest.TotalDebit, latest.TotalCredit);
        Assert.True(latest.CanPost);
        Assert.Equal(new DateOnly(2026, 4, 30), latest.PostedThrough);
        Assert.All(latest.Entries, e => Assert.Equal(2, e.Lines.Count));

        var old = await service.GetAsync(ctx, 2025, null, null, null, 1, 100, canPost: false, default);
        Assert.Equal(["2025-000001"], old.Entries.Select(e => e.Label).ToArray());
        Assert.False(old.CanPost);

        var byNumber = await service.GetAsync(ctx, 2026, null, null, 2, 1, 100, canPost: false, default);
        Assert.Equal(300_000m, byNumber.Entries.Single().Debit);

        var byDate = await service.GetAsync(ctx, 2026, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), null, 1, 100, canPost: false, default);
        Assert.Equal(["2026-000002"], byDate.Entries.Select(e => e.Label).ToArray());

        var paged = await service.GetAsync(ctx, 2026, null, null, null, 2, 2, canPost: false, default);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(["2026-000003"], paged.Entries.Select(e => e.Label).ToArray());
        Assert.Equal(900_000m, paged.TotalDebit);          // las sumas son de todo el filtro, no de la pagina
    }

    [Fact]
    public async Task UnEdificioSinAsientosDevuelveElLibroVacio()
    {
        using var env = NewEnv();

        var dto = await Journal(env).GetAsync(await Context(env), null, null, null, null, 1, 100, canPost: true, default);

        Assert.Empty(dto.Entries);
        Assert.Empty(dto.FiscalYears);
        Assert.Null(dto.PostedThrough);
        Assert.Equal(0, dto.TotalCount);
    }

    [Fact]
    public async Task ElLibroNoMezclaAsientosDeOtroEdificio()
    {
        using var env = NewEnv();
        await Post(env, [Entry(D1)], D1);
        var other = env.T.AddBuilding(env.Company, "Otro edificio");
        env.T.Db.JournalEntries.Add(new JournalEntry
        {
            CompanyId = env.Company.Id, BuildingId = other.Id, FiscalYear = 2026, Number = 1, Date = D1, OperationDate = D1,
            TotalDebit = 5m, TotalCredit = 5m, ContentHash = new string('a', 64)
        });
        env.T.Db.SaveChanges();

        var dto = await Journal(env).GetAsync(await Context(env), null, null, null, null, 1, 100, canPost: false, default);

        Assert.Equal(100_000m, dto.TotalDebit);
        Assert.Single(dto.Entries);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Permisos del controlador
    // ═════════════════════════════════════════════════════════════════════════

    private static FinanceJournalController Api(FinanceEnv env) => new(
        env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), Journal(env));

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result.Result).StatusCode);

    [Fact]
    public async Task LoVenLosCuatroRolesPeroAsientanSoloSuperAdminYAdministrador()
    {
        using var env = NewEnv();
        var api = Api(env);

        foreach (var role in new[] { "SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            var dto = FinanceEnv.Ok(await api.Get(env.Building.Id, null, null, null, null, 1, 100, default));
            Assert.Equal(role is "SuperAdmin" or "CompanyAdmin", dto.CanPost);
        }

        foreach (var role in new[] { "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            AssertStatus(await api.Post(env.Building.Id, new PostJournalRequest { Through = D1 }, default), 403);
        }

        Assert.Null(PostedThrough(env));
        Assert.Empty(Saved(env));
    }

    [Fact]
    public async Task UnEdificioAjenoOSinFinanzasNoSeLeeNiSeAsienta()
    {
        using var env = NewEnv();
        var api = Api(env);
        env.LoginAs("CompanyAdmin");

        env.Access.Buildings.Clear();
        Assert.IsNotType<OkObjectResult>((await api.Get(env.Building.Id, null, null, null, null, 1, 100, default)).Result);
        Assert.IsNotType<OkObjectResult>((await api.Post(env.Building.Id, new PostJournalRequest { Through = D1 }, default)).Result);
        Assert.IsNotType<OkObjectResult>((await api.Preview(env.Building.Id, D1, default)).Result);

        env.Access.Buildings.Add(env.Building.Id);
        env.T.Db.Buildings.Single(x => x.Id == env.Building.Id).FinanceModuleEnabled = false;
        env.T.Db.SaveChanges();
        AssertStatus(await api.Post(env.Building.Id, new PostJournalRequest { Through = D1 }, default), 403);
        Assert.Null(PostedThrough(env));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // De punta a punta con el libro real (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    // Un banco con tres movimientos: un ingreso de 1.000.000, un gasto de 110.000 con IVA 10 % y otro de 50.000 exento.
    private static async Task<FinancialAccount> Seed(FinanceEnv env, bool assign = true)
    {
        var bank = env.Setup();
        env.SeedTemplate();
        env.AddIncome(null, 1_000_000m, FinanceEnv.Yesterday.AddDays(-10));
        var expense = env.AddExpense(null, 110_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-6));
        var exempt = env.AddExpense(null, 50_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-3));
        env.T.Db.BuildingExpenses.Single(x => x.Id == expense.Id).VatRate = 10m;
        env.T.Db.BuildingExpenses.Single(x => x.Id == exempt.Id).VatRate = 0m;
        env.T.Db.SaveChanges();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        if (assign)
        {
            var accounting = new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);
            var (_, error) = await accounting.UpdateRolesAsync(await Context(env), new UpdateAccountingRolesRequest
            {
                Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
                SetVatCredit = true,
                VatCreditCategoryId = env.Cat("1.4.06").Id
            }, env.Company.Id, default);
            Assert.Null(error);
        }

        return bank;
    }

    [SqlServerFact]
    public async Task AsentarElLibroRealNumeraEnOrdenCronologicoYNoVuelveAAsentarLoYaAsentado()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await Seed(env);
        var api = Api(env);
        var through = FinanceEnv.Yesterday;

        var preview = FinanceEnv.Ok(await api.Preview(env.Building.Id, through, default));
        Assert.Equal(3, preview.EntryCount);
        Assert.Equal(0, preview.IncompleteCount);
        Assert.True(preview.CanPost);
        Assert.Equal(preview.TotalDebit, preview.TotalCredit);
        Assert.Null(preview.PostedThrough);
        Assert.Empty(Saved(env));                                  // la vista previa no asienta nada

        var posted = FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = through }, default));
        Assert.Equal(3, posted.PostedCount);
        Assert.Equal(preview.FirstLabel, posted.FirstLabel);
        Assert.Equal(preview.LastLabel, posted.LastLabel);
        Assert.Equal(through, PostedThrough(env));

        var saved = Saved(env);
        Assert.Equal(saved.OrderBy(e => e.Date).Select(e => e.Number), saved.Select(e => e.Number));   // el numero sigue a la fecha
        Assert.All(saved, e => Assert.Equal(e.TotalDebit, e.TotalCredit));
        Assert.Equal(1_000_000m - 110_000m - 50_000m,
            saved.SelectMany(e => e.Lines).Where(l => l.Code == "1.1.03").Sum(l => l.Debit - l.Credit));    // el banco coincide con el libro

        // Lo asentado no se asienta de nuevo, ni pidiendo el mismo dia ni uno anterior.
        AssertStatus(await api.Post(env.Building.Id, new PostJournalRequest { Through = through }, default), 400);
        AssertStatus(await api.Post(env.Building.Id, new PostJournalRequest { Through = through.AddDays(-5) }, default), 400);
        Assert.Equal(3, Saved(env).Count);

        // Un movimiento nuevo (de hoy) se asienta con el numero siguiente.
        var today = FinancePeriods.Today();
        env.AddExpense(null, 30_000m, BuildingExpenseCategory.Other, today);
        env.T.Db.BuildingExpenses.OrderByDescending(x => x.CreatedAtUtc).First().VatRate = 0m;
        env.T.Db.SaveChanges();
        var second = FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = today }, default));
        Assert.Equal(1, second.PostedCount);
        Assert.Equal(4, Saved(env).Count);
        Assert.Equal([1, 2, 3, 4], Saved(env).Select(e => e.Number).ToArray());

        var book = FinanceEnv.Ok(await api.Get(env.Building.Id, null, null, null, null, 1, 100, default));
        Assert.Equal(4, book.TotalCount);
        Assert.Equal(book.TotalDebit, book.TotalCredit);
        Assert.Equal(today, book.PostedThrough);
    }

    [SqlServerFact]
    public async Task SinLasCuentasDelPlanNoSeAsientaNadaYElAvisoDiceCualesFaltan()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await Seed(env, assign: false);
        var api = Api(env);

        var preview = FinanceEnv.Ok(await api.Preview(env.Building.Id, FinanceEnv.Yesterday, default));
        Assert.False(preview.CanPost);
        Assert.True(preview.IncompleteCount > 0);
        Assert.NotEmpty(preview.Pending);

        var denied = await api.Post(env.Building.Id, new PostJournalRequest { Through = FinanceEnv.Yesterday }, default);

        AssertStatus(denied, 409);
        Assert.Empty(Saved(env));
        Assert.Null(PostedThrough(env));
    }

    [SqlServerFact]
    public async Task LosSugeridosAvisanHastaQueDiaYaEstanAsentados()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await Seed(env);
        var api = Api(env);
        var accounting = new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);
        var ctx = await Context(env);

        Assert.Null((await accounting.EntriesAsync(ctx, FinanceEnv.Yesterday.AddDays(-12), FinanceEnv.Yesterday, default)).Entries!.PostedThrough);

        FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = FinanceEnv.Yesterday }, default));

        var suggested = (await accounting.EntriesAsync(ctx, FinanceEnv.Yesterday.AddDays(-12), FinanceEnv.Yesterday, default)).Entries!;
        Assert.Equal(FinanceEnv.Yesterday, suggested.PostedThrough);
        Assert.All(suggested.Entries, e => Assert.NotNull(e.SourceAccountId));
    }
}
