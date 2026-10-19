using Condo.Api.Controllers;
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
/// Libro Diario, ajustes de la fase 2: no se asientan gastos ni ingresos de periodos en borrador (todavia se pueden editar), el aviso previo
/// a revertir un pago o deshacer la publicacion de un periodo ya asentado, y la causa y el motivo de cada correccion.
/// </summary>
public class JournalGuardsTests
{
    private static readonly Guid BankAccountId = Guid.NewGuid();
    private static readonly Guid RubroCat = Guid.NewGuid();
    private static readonly DateOnly D1 = new(2026, 3, 5);
    private static readonly DateOnly D2 = new(2026, 3, 10);
    private static readonly DateOnly D3 = new(2026, 3, 20);

    // ── Armado ───────────────────────────────────────────────────────────────

    private static AccountingEntryDto Entry(DateOnly date, LedgerSourceType type, Guid source, decimal amount = 100_000m, string description = "Movimiento")
    {
        var lines = new[]
        {
            new AccountingLineDto { CategoryId = Guid.NewGuid(), Code = "1.1.03", Name = "Banco", Debit = amount, Kind = "Financial" },
            new AccountingLineDto { CategoryId = RubroCat, Code = "4.1.01", Name = "Expensas ordinarias", Credit = amount, Kind = "Rubro" }
        };
        return new AccountingEntryDto
        {
            Date = date, Description = description, ThirdParty = "Unidad A", Reference = "R-1",
            SourceType = type, SourceId = source, SourceAccountId = BankAccountId,
            Lines = lines, Debit = amount, Credit = amount, IsComplete = true
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

    private static void SetPeriod(FinanceEnv env, ExpensePeriodStatus status)
    {
        env.T.Db.ExpensePeriods.Single(x => x.Id == env.Period.Id).Status = status;
        env.T.Db.SaveChanges();
    }

    private static async Task<LedgerContext> Context(FinanceEnv env) =>
        (await new FinanceLedgerService(env.T.NewContext()).LoadContextAsync(env.Building.Id, default))!;

    private static JournalPostingService Journal(FinanceEnv env, CondoDbContext db) =>
        new(db, new AccountingEntriesService(db, new FinanceLedgerService(db), env.Tenant), env.Tenant);

    private static async Task<(PostJournalResultDto? Result, string? Error, bool Conflict)> Post(
        FinanceEnv env, IReadOnlyList<AccountingEntryDto> entries, DateOnly through, IReadOnlyList<JournalDifference>? differences = null)
    {
        var db = env.T.NewContext();
        var settings = db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id);
        return await Journal(env, db).PostBuiltAsync(await Context(env), settings, entries, through, env.Company.Id, default, differences);
    }

    private static List<JournalDifference> Detect(FinanceEnv env, IReadOnlyList<AccountingEntryDto> current)
    {
        var db = env.T.NewContext();
        var posted = db.JournalEntries.Where(x => x.BuildingId == env.Building.Id && x.Kind == JournalEntryKind.Auto && x.SourceType != null).ToList();
        var reversed = db.JournalEntries.Where(x => x.ReversesEntryId != null).Select(x => x.ReversesEntryId!.Value).ToHashSet();
        return JournalPostingService.Compare(current, posted, reversed);
    }

    private static List<JournalEntry> Saved(FinanceEnv env) =>
        env.T.NewContext().JournalEntries.Include(x => x.Lines).Where(x => x.BuildingId == env.Building.Id)
            .OrderBy(x => x.FiscalYear).ThenBy(x => x.Number).ToList();

    // Un pago de propietario ya revertido, tal como lo deja el sistema: el Payment queda marcado y el pago del propietario guarda el motivo.
    private static Payment AddReversedPayment(FinanceEnv env, string reason, string reference = "PAY-1")
    {
        var unit = env.T.AddUnit(env.Building);
        var payment = new Payment
        {
            CompanyId = env.Company.Id, ExpensePeriodId = env.Period.Id, UnitId = unit.Id, PaymentDate = D1, Amount = 100_000m,
            Reference = reference, IsReversed = true, ReversedAt = DateTime.UtcNow
        };
        var owner = new OwnerPayment
        {
            CompanyId = env.Company.Id, OwnerId = env.Admin.Id, PaymentDate = D1, DeclaredAmount = 100_000m, Reference = reference,
            Status = OwnerPaymentStatus.Rejected, RejectionReason = $"PAGO REVERTIDO: {reason}", Channel = OwnerPaymentChannel.Web
        };
        env.T.Db.Payments.Add(payment);
        env.T.Db.OwnerPayments.Add(owner);
        env.T.Db.SaveChanges();
        return payment;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Periodos en borrador
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnGastoDeUnPeriodoEnBorradorFrenaTodoElAsentadoYDiceCualPeriodo()
    {
        using var env = NewEnv();
        var expense = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, D1);          // el periodo de FinanceEnv nace en borrador
        var fine = Entry(D1, LedgerSourceType.OwnerPayment, Guid.NewGuid());

        var (result, error, conflict) = await Post(env, [fine, Entry(D2, LedgerSourceType.BuildingExpense, expense.Id)], D3);

        Assert.Null(result);
        Assert.True(conflict);
        Assert.Contains("períodos en borrador", error);
        Assert.Contains("Octubre 2026", error);
        Assert.Empty(Saved(env));                                                                 // ni siquiera lo que estaba bien
        Assert.Null(env.T.NewContext().FinanceSettings.Single(x => x.BuildingId == env.Building.Id).JournalPostedThrough);
    }

    [Fact]
    public async Task UnIngresoDeUnPeriodoEnBorradorTambienFrena_YAlPublicarseSeAsienta()
    {
        using var env = NewEnv();
        var income = env.AddIncome(null, 50_000m, D1);

        var blocked = await Post(env, [Entry(D1, LedgerSourceType.BuildingIncome, income.Id)], D2);
        Assert.True(blocked.Conflict);
        Assert.Empty(Saved(env));

        SetPeriod(env, ExpensePeriodStatus.Published);
        var ok = await Post(env, [Entry(D1, LedgerSourceType.BuildingIncome, income.Id)], D2);

        Assert.Null(ok.Error);
        Assert.Single(Saved(env));
    }

    [Theory]
    [InlineData(ExpensePeriodStatus.Published)]
    [InlineData(ExpensePeriodStatus.Closed)]
    public async Task UnPeriodoPublicadoOCerradoNoFrena(ExpensePeriodStatus status)
    {
        using var env = NewEnv();
        SetPeriod(env, status);
        var expense = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, D1);

        var (_, error, _) = await Post(env, [Entry(D1, LedgerSourceType.BuildingExpense, expense.Id)], D2);

        Assert.Null(error);
    }

    [Fact]
    public async Task LosCobrosNoDependenDelEstadoDeUnPeriodo()
    {
        using var env = NewEnv();                                                                 // el periodo sigue en borrador

        var (_, error, _) = await Post(env, [Entry(D1, LedgerSourceType.OwnerPayment, Guid.NewGuid())], D2);

        Assert.Null(error);
    }

    [Fact]
    public async Task UnaBajaDeUnGastoDeUnPeriodoEnBorradorSiSePuedeAnular()
    {
        using var env = NewEnv();
        SetPeriod(env, ExpensePeriodStatus.Published);
        var expense = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, D1);
        var entry = Entry(D1, LedgerSourceType.BuildingExpense, expense.Id);
        await Post(env, [entry], D2);
        SetPeriod(env, ExpensePeriodStatus.Draft);                                                // se deshizo la publicacion

        var (result, error, _) = await Post(env, [], D2, Detect(env, []));                        // y el gasto ya no esta: solo hay contraasiento

        Assert.Null(error);
        Assert.Equal(1, result!.PostedCount);
    }

    [Fact]
    public async Task UnaCorreccionDeUnGastoQueSigueEnBorradorEsperaALaPublicacion()
    {
        using var env = NewEnv();
        SetPeriod(env, ExpensePeriodStatus.Published);
        var expense = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, D1);
        await Post(env, [Entry(D1, LedgerSourceType.BuildingExpense, expense.Id, 80_000m)], D2);
        SetPeriod(env, ExpensePeriodStatus.Draft);                                                // se deshizo la publicacion y se edito el gasto
        var edited = Entry(D1, LedgerSourceType.BuildingExpense, expense.Id, 95_000m);

        var blocked = await Post(env, [], D3, Detect(env, [edited]));
        Assert.True(blocked.Conflict);
        Assert.Contains("Octubre 2026", blocked.Error);
        Assert.Single(Saved(env));

        SetPeriod(env, ExpensePeriodStatus.Published);                                            // al republicar se corrige
        var ok = await Post(env, [], D3, Detect(env, [edited]));
        Assert.Null(ok.Error);
        Assert.Equal(3, Saved(env).Count);
    }

    [Fact]
    public async Task LosPeriodosEnBorradorSeListanSinRepetirYEnOrden()
    {
        using var env = NewEnv();
        var a = env.AddExpense(null, 1m, BuildingExpenseCategory.Other, D1);
        var b = env.AddExpense(null, 2m, BuildingExpenseCategory.Other, D2);
        var c = env.AddIncome(null, 3m, D2);
        var db = env.T.NewContext();

        var names = await Journal(env, db).DraftPeriodNamesAsync(
            [Entry(D1, LedgerSourceType.BuildingExpense, a.Id), Entry(D2, LedgerSourceType.BuildingExpense, b.Id), Entry(D2, LedgerSourceType.BuildingIncome, c.Id),
             Entry(D2, LedgerSourceType.OwnerPayment, Guid.NewGuid())], default);

        Assert.Equal(["Octubre 2026"], names);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Causa y motivo
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(JournalDifferenceKind.Removed, LedgerSourceType.OwnerPayment, "Pago revertido")]
    [InlineData(JournalDifferenceKind.Changed, LedgerSourceType.OwnerPayment, "Pago modificado")]
    [InlineData(JournalDifferenceKind.Changed, LedgerSourceType.BuildingExpense, "Gasto modificado")]
    [InlineData(JournalDifferenceKind.Removed, LedgerSourceType.BuildingIncome, "Ingreso eliminado")]
    [InlineData(JournalDifferenceKind.Removed, LedgerSourceType.SupplierCreditNote, "Nota de crédito anulada")]
    [InlineData(JournalDifferenceKind.Missing, LedgerSourceType.BuildingExpense, "Gasto cargado después de asentar ese día")]
    [InlineData(JournalDifferenceKind.Missing, LedgerSourceType.SupplierCreditNote, "Movimiento cargado después de asentar ese día")]
    public void CadaDiferenciaTieneSuCausaEnPalabras(JournalDifferenceKind kind, LedgerSourceType type, string expected)
    {
        Assert.Equal(expected, JournalPostingService.CauseOf(kind, type));
    }

    [Fact]
    public async Task ElMotivoDeLaReversaDeUnPagoSeLeeDelPagoYQuedaEnLaGlosaDelContraasiento()
    {
        using var env = NewEnv();
        var payment = AddReversedPayment(env, "Importe mal cargado");
        await Post(env, [Entry(D1, LedgerSourceType.OwnerPayment, payment.Id, description: "Cobro unidad A")], D2);

        var differences = Detect(env, []);                                                        // el pago revertido ya no esta en el libro
        var diff = Assert.Single(differences);
        Assert.Equal(JournalDifferenceKind.Removed, diff.Kind);

        var reasons = await Journal(env, env.T.NewContext()).ReasonsAsync(differences, default);
        Assert.Equal("Importe mal cargado", reasons[payment.Id]);

        await Post(env, [], D3, differences);
        var reversal = Saved(env).Single(e => e.Kind == JournalEntryKind.Reversal);
        Assert.Contains("pago revertido", reversal.Description);
        Assert.Contains("Motivo: Importe mal cargado", reversal.Description);
        Assert.Contains("Cobro unidad A", reversal.Description);
    }

    [Fact]
    public async Task ElMotivoDeUnaNotaDeCreditoAnuladaSeLeeDeLaNota()
    {
        using var env = NewEnv();
        SetPeriod(env, ExpensePeriodStatus.Published);
        var expense = env.AddExpense(null, 100_000m, BuildingExpenseCategory.Other, D1);
        var note = new BuildingExpenseCreditNote
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, BuildingExpenseId = expense.Id, ExpensePeriodId = env.Period.Id,
            IssueDate = D1, Amount = 10_000m, Status = BuildingExpenseCreditNoteStatus.Voided, VoidReason = "Se cargó por error",
            Mode = BuildingExpenseCreditNoteMode.Credited
        };
        env.T.Db.BuildingExpenseCreditNotes.Add(note);
        env.T.Db.SaveChanges();
        await Post(env, [Entry(D1, LedgerSourceType.SupplierCreditNote, note.Id)], D2);

        var differences = Detect(env, []);
        var reasons = await Journal(env, env.T.NewContext()).ReasonsAsync(differences, default);

        Assert.Equal("Se cargó por error", reasons[note.Id]);
    }

    [Fact]
    public async Task SinMotivoGuardadoLaGlosaQuedaConLaCausaSola()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        await Post(env, [Entry(D1, LedgerSourceType.OwnerPayment, source)], D2);

        await Post(env, [], D3, Detect(env, []));

        var reversal = Saved(env).Single(e => e.Kind == JournalEntryKind.Reversal);
        Assert.DoesNotContain("Motivo", reversal.Description);
        Assert.Contains("(pago revertido)", reversal.Description);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Aviso previo: impacto sobre lo asentado
    // ═════════════════════════════════════════════════════════════════════════

    private static FinanceJournalImpactController Api(FinanceEnv env) => new(
        env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), Journal(env, env.T.Db));

    [Fact]
    public async Task ElImpactoDeUnPagoAsentadoDiceQueAsientoEsYHastaQueDiaEstaAsentado()
    {
        using var env = NewEnv();
        var unit = env.T.AddUnit(env.Building);
        var payment = new Payment { CompanyId = env.Company.Id, ExpensePeriodId = env.Period.Id, UnitId = unit.Id, PaymentDate = D1, Amount = 1m, Reference = "PAY-7" };
        var owner = new OwnerPayment
        {
            CompanyId = env.Company.Id, OwnerId = env.Admin.Id, PaymentDate = D1, DeclaredAmount = 1m, Reference = "PAY-7",
            Status = OwnerPaymentStatus.Approved, Channel = OwnerPaymentChannel.Web
        };
        env.T.Db.Payments.Add(payment);
        env.T.Db.OwnerPayments.Add(owner);
        env.T.Db.SaveChanges();

        var before = FinanceEnv.Ok(await Api(env).Get(owner.Id, null, null, null, default));
        Assert.False(before.Posted);

        await Post(env, [Entry(D1, LedgerSourceType.OwnerPayment, payment.Id)], D2);

        var after = FinanceEnv.Ok(await Api(env).Get(owner.Id, null, null, null, default));
        Assert.True(after.Posted);
        Assert.Equal(1, after.PostedCount);
        Assert.Equal(["2026-000001"], after.Labels.ToArray());
        Assert.Equal(D2, after.PostedThrough);
    }

    [Fact]
    public async Task ElImpactoDeUnPeriodoSumaLosAsientosDeSusGastosEIngresosYNoCuentaLosYaAnulados()
    {
        using var env = NewEnv();
        SetPeriod(env, ExpensePeriodStatus.Published);
        var expense = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, D1);
        var income = env.AddIncome(null, 20_000m, D1);
        var entries = new[]
        {
            Entry(D1, LedgerSourceType.BuildingExpense, expense.Id, 80_000m), Entry(D1, LedgerSourceType.BuildingIncome, income.Id, 20_000m)
        };
        await Post(env, entries, D2);

        var impact = FinanceEnv.Ok(await Api(env).Get(null, env.Period.Id, null, null, default));
        Assert.True(impact.Posted);
        Assert.Equal(2, impact.PostedCount);
        Assert.Equal(2, impact.Labels.Count);

        // El ingreso se anula con un contraasiento: ya no cuenta como asiento vigente.
        await Post(env, [], D3, Detect(env, [entries[0]]));
        var after = FinanceEnv.Ok(await Api(env).Get(null, env.Period.Id, null, null, default));
        Assert.Equal(1, after.PostedCount);
    }

    [Fact]
    public async Task ElImpactoDeUnGastoQueSeDeshaceDePagarTambienSeAvisa()
    {
        using var env = NewEnv();
        SetPeriod(env, ExpensePeriodStatus.Published);
        var expense = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, D1);
        await Post(env, [Entry(D1, LedgerSourceType.BuildingExpense, expense.Id)], D2);

        var impact = FinanceEnv.Ok(await Api(env).Get(null, null, expense.Id, null, default));

        Assert.True(impact.Posted);
        Assert.Equal(1, impact.PostedCount);
    }

    [Fact]
    public async Task ElImpactoNuncaFallaSinoQueDiceNadaAsentado()
    {
        using var env = NewEnv();
        SetPeriod(env, ExpensePeriodStatus.Published);
        var expense = env.AddExpense(null, 80_000m, BuildingExpenseCategory.Other, D1);
        await Post(env, [Entry(D1, LedgerSourceType.BuildingExpense, expense.Id)], D2);
        var api = Api(env);

        Assert.False(FinanceEnv.Ok(await api.Get(null, null, null, null, default)).Posted);                  // sin parametros
        Assert.False(FinanceEnv.Ok(await api.Get(null, env.Period.Id, expense.Id, null, default)).Posted);   // dos a la vez
        Assert.False(FinanceEnv.Ok(await api.Get(Guid.NewGuid(), null, null, null, default)).Posted);        // pago inexistente

        env.LoginAs("Owner");                                                                          // rol sin acceso a Finanzas
        Assert.False(FinanceEnv.Ok(await api.Get(null, env.Period.Id, null, null, default)).Posted);
        env.LoginAs("CompanyAdmin");

        env.Access.Buildings.Clear();                                                                  // edificio ajeno
        Assert.False(FinanceEnv.Ok(await api.Get(null, env.Period.Id, null, null, default)).Posted);
        env.Access.Buildings.Add(env.Building.Id);

        env.T.Db.Buildings.Single(x => x.Id == env.Building.Id).FinanceModuleEnabled = false;          // sin Finanzas
        env.T.Db.SaveChanges();
        Assert.False(FinanceEnv.Ok(await api.Get(null, env.Period.Id, null, null, default)).Posted);
    }
}
