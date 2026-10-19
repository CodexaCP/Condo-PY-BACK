using System.Reflection;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Marketplace;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Libro Diario, fase 2: lo asentado no se corrige nunca. Si el movimiento de origen cambia, se revierte o aparece con fecha ya asentada, al
/// asentar se genera un contraasiento (y un asiento nuevo si el movimiento sigue existiendo); la base ademas impide modificar o borrar lo
/// asentado. La comparacion y la guarda se prueban con asientos armados a mano; la deteccion contra el libro real y el bloqueo de la base
/// usan SQL Server y se omiten solos sin la variable CONDO_TEST_SQLSERVER.
/// </summary>
public class JournalCorrectionsTests
{
    private static readonly Guid BankAccountId = Guid.NewGuid();
    private static readonly Guid RubroCat = Guid.NewGuid();
    private static readonly DateOnly D1 = new(2026, 3, 5);
    private static readonly DateOnly D2 = new(2026, 3, 10);
    private static readonly DateOnly D3 = new(2026, 3, 20);
    private static readonly DateOnly D4 = new(2026, 3, 25);

    // ── Armado ───────────────────────────────────────────────────────────────

    private static AccountingEntryDto Entry(
        DateOnly date, decimal amount = 100_000m, Guid? source = null, Guid? rubro = null, bool complete = true, string description = "Expensas")
    {
        var lines = new[]
        {
            new AccountingLineDto { CategoryId = Guid.NewGuid(), Code = "1.1.03", Name = "Banco", Debit = amount, Kind = "Financial" },
            new AccountingLineDto { CategoryId = rubro ?? RubroCat, Code = "4.1.01", ExternalCode = "401", Name = "Expensas ordinarias", Credit = amount, Kind = "Rubro", Missing = !complete }
        };
        return new AccountingEntryDto
        {
            Date = date, Description = description, ThirdParty = "Unidad A", Reference = "R-1",
            SourceType = LedgerSourceType.OwnerPayment, SourceId = source ?? Guid.NewGuid(), SourceAccountId = BankAccountId,
            Lines = lines, Debit = amount, Credit = amount, IsComplete = complete
        };
    }

    // Un asiento ya asentado, como lo dejaria el servicio (para probar la comparacion sin base de datos).
    private static JournalEntry Posted(AccountingEntryDto entry, int number = 1, int revision = 0) => new()
    {
        Id = Guid.NewGuid(), FiscalYear = 2026, Number = number, Date = entry.Date, OperationDate = entry.Date, Kind = JournalEntryKind.Auto,
        SourceType = (int)entry.SourceType, SourceId = entry.SourceId, SourceAccountId = entry.SourceAccountId, Revision = revision,
        Description = entry.Description, ThirdParty = entry.ThirdParty, TotalDebit = entry.Debit, TotalCredit = entry.Credit,
        ContentHash = JournalPostingService.HashOf(entry)
    };

    private static List<JournalDifference> Compare(IReadOnlyList<AccountingEntryDto> current, IReadOnlyList<JournalEntry> posted, params JournalEntry[] reversed) =>
        JournalPostingService.Compare(current, posted, reversed.Select(r => r.Id).ToHashSet());

    // ═════════════════════════════════════════════════════════════════════════
    // Comparacion contra lo asentado (pura)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SinCambiosNoHayDiferencias()
    {
        var a = Entry(D1);
        var b = Entry(D2);

        Assert.Empty(Compare([a, b], [Posted(a, 1), Posted(b, 2)]));
    }

    [Fact]
    public void UnImporteQueCambioEsUnaDiferenciaConSuAsientoOriginalYLaVersionSiguiente()
    {
        var original = Entry(D1, 100_000m);
        var posted = Posted(original);
        var edited = Entry(D1, 120_000m, original.SourceId);

        var diff = Assert.Single(Compare([edited], [posted]));

        Assert.Equal(JournalDifferenceKind.Changed, diff.Kind);
        Assert.Same(posted, diff.Original);
        Assert.Same(edited, diff.Current);
        Assert.Equal(1, diff.NextRevision);
        Assert.Equal(2, diff.EntryCount);
    }

    [Fact]
    public void RenombrarRecodificarOReasignarLaCuentaDeUnBancoNoEsUnCambioDelMovimiento()
    {
        var original = Entry(D1);
        var posted = Posted(original);
        var renamed = Entry(D1, source: original.SourceId);
        var bank = renamed.Lines[0];
        bank.CategoryId = Guid.NewGuid();
        bank.Code = "1.1.99";
        bank.Name = "Otro banco renombrado";
        renamed.Lines[1].Code = "4.9.99";
        renamed.Lines[1].Name = "Rubro recodificado";

        Assert.Empty(Compare([renamed], [posted]));
    }

    [Fact]
    public void ReclasificarElRubroDeUnMovimientoSiEsUnCambio()
    {
        var original = Entry(D1);
        var posted = Posted(original);
        var reclassified = Entry(D1, source: original.SourceId, rubro: Guid.NewGuid());

        Assert.Equal(JournalDifferenceKind.Changed, Assert.Single(Compare([reclassified], [posted])).Kind);
    }

    [Fact]
    public void UnMovimientoDeUnDiaYaAsentadoQueNoTieneAsientoEsFaltante()
    {
        var known = Entry(D1);
        var late = Entry(D2);

        var diff = Assert.Single(Compare([known, late], [Posted(known)]));

        Assert.Equal(JournalDifferenceKind.Missing, diff.Kind);
        Assert.Null(diff.Original);
        Assert.Same(late, diff.Current);
        Assert.Equal(0, diff.NextRevision);
    }

    [Fact]
    public void UnMovimientoQueYaNoExisteEsUnaBaja()
    {
        var gone = Entry(D1);
        var posted = Posted(gone);

        var diff = Assert.Single(Compare([], [posted]));

        Assert.Equal(JournalDifferenceKind.Removed, diff.Kind);
        Assert.Same(posted, diff.Original);
        Assert.Null(diff.Current);
        Assert.Equal(1, diff.EntryCount);
    }

    [Fact]
    public void UnAsientoYaAnuladoNoVuelveAAnularse_YSiElMovimientoVolvioSeAsientaDeNuevoConLaVersionSiguiente()
    {
        var entry = Entry(D1);
        var original = Posted(entry, 1, 0);

        Assert.Empty(Compare([], [original], original));                           // anulado y el movimiento ya no esta: todo en orden

        var back = Assert.Single(Compare([entry], [original], original));          // anulado pero el movimiento volvio
        Assert.Equal(JournalDifferenceKind.Missing, back.Kind);
        Assert.Equal(1, back.NextRevision);
    }

    [Fact]
    public void ConVariasVersionesSoloCuentaLaVigente()
    {
        var entry = Entry(D1, 100_000m);
        var v0 = Posted(entry, 1, 0);
        var changed = Entry(D1, 150_000m, entry.SourceId);
        var v1 = Posted(changed, 5, 1);

        Assert.Empty(Compare([changed], [v0, v1], v0));                            // la version 0 esta anulada y la 1 coincide

        var again = Entry(D1, 170_000m, entry.SourceId);
        var diff = Assert.Single(Compare([again], [v0, v1], v0));
        Assert.Same(v1, diff.Original);                                            // se corrige la vigente, no la vieja
        Assert.Equal(2, diff.NextRevision);
    }

    [Fact]
    public void LasHuellasDeLaFase1SeSiguenReconociendo()
    {
        var entry = Entry(D1);
        var posted = Posted(entry);
        posted.ContentHash = (string)typeof(JournalPostingService)
            .GetMethod("LegacyHashOf", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [entry])!;

        Assert.Empty(Compare([entry], [posted]));
        Assert.NotEqual(posted.ContentHash, JournalPostingService.HashOf(entry));
    }

    [Fact]
    public void LasDiferenciasSalenPorFechaDeOperacion()
    {
        var late = Entry(D3);
        var changed = Entry(D1, 2m);
        var postedChanged = Posted(Entry(D1, 1m, changed.SourceId));
        var removed = Posted(Entry(D2));

        var diffs = Compare([late, changed], [postedChanged, removed]);

        Assert.Equal([JournalDifferenceKind.Changed, JournalDifferenceKind.Removed, JournalDifferenceKind.Missing], diffs.Select(d => d.Kind).ToArray());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Asentar correcciones (guarda)
    // ═════════════════════════════════════════════════════════════════════════

    private static FinanceEnv NewEnv()
    {
        var env = new FinanceEnv();
        env.Setup();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        return env;
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

    // Lo que hoy diria el libro, comparado con lo ya guardado en la base.
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

    [Fact]
    public async Task UnImporteCorregidoGeneraElContraasientoYElAsientoNuevoSinTocarElOriginal()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        await Post(env, [Entry(D1, 100_000m, source, description: "Cobro A")], D2);
        var original = Saved(env).Single();
        var edited = Entry(D1, 120_000m, source, description: "Cobro A");

        var (result, error, _) = await Post(env, [], D3, Detect(env, [edited]));

        Assert.Null(error);
        Assert.Equal(2, result!.PostedCount);
        Assert.Equal(2, result.CorrectionEntryCount);
        Assert.Equal("2026-000002", result.FirstLabel);
        Assert.Equal("2026-000003", result.LastLabel);

        var saved = Saved(env);
        Assert.Equal([1, 2, 3], saved.Select(e => e.Number).ToArray());
        var unchanged = saved[0];
        Assert.Equal(original.Id, unchanged.Id);
        Assert.Equal(100_000m, unchanged.TotalDebit);                       // el original queda tal cual
        Assert.Equal(original.ContentHash, unchanged.ContentHash);

        var reversal = saved[1];
        Assert.Equal(JournalEntryKind.Reversal, reversal.Kind);
        Assert.Equal(original.Id, reversal.ReversesEntryId);
        Assert.Equal(D3, reversal.Date);                                    // fecha contable = el dia hasta el que se asienta
        Assert.Equal(D1, reversal.OperationDate);                           // la fecha real de la operacion
        Assert.Null(reversal.SourceType);
        Assert.Contains("Contraasiento del asiento 2026-000001", reversal.Description);
        Assert.Equal(original.TotalCredit, reversal.TotalDebit);
        var inverted = reversal.Lines.OrderBy(l => l.LineNo).ToList();
        var first = unchanged.Lines.OrderBy(l => l.LineNo).ToList();
        Assert.Equal(first.Select(l => (l.Code, l.Debit, l.Credit)).Select(x => (x.Code, x.Credit, x.Debit)).ToArray(),
            inverted.Select(l => (l.Code, l.Debit, l.Credit)).ToArray());

        var replacement = saved[2];
        Assert.Equal(JournalEntryKind.Auto, replacement.Kind);
        Assert.Equal(1, replacement.Revision);
        Assert.Equal(D3, replacement.Date);
        Assert.Equal(D1, replacement.OperationDate);
        Assert.Equal(120_000m, replacement.TotalDebit);
        Assert.Contains("corrige el asiento 2026-000001", replacement.Description);

        Assert.Equal(D3, env.T.NewContext().FinanceSettings.Single(x => x.BuildingId == env.Building.Id).JournalPostedThrough);
        Assert.Equal(3, env.T.NewContext().JournalSequences.Single().LastNumber);
        Assert.Contains("correcciones", env.T.NewContext().FinanceAuditLogs.Where(x => x.Action == "Posted").OrderBy(x => x.CreatedAtUtc).Last().Summary);

        // Con todo corregido, no quedan diferencias (converge).
        Assert.Empty(Detect(env, [edited]));
    }

    [Fact]
    public async Task UnMovimientoRevertidoSoloGeneraElContraasiento()
    {
        using var env = NewEnv();
        var entry = Entry(D1);
        await Post(env, [entry], D2);

        var (result, error, _) = await Post(env, [], D2, Detect(env, []));        // el mismo dia: solo las correcciones

        Assert.Null(error);
        Assert.Equal(1, result!.PostedCount);
        var saved = Saved(env);
        Assert.Equal([JournalEntryKind.Auto, JournalEntryKind.Reversal], saved.Select(e => e.Kind).ToArray());
        Assert.Equal(D2, saved[1].Date);
        Assert.Empty(Detect(env, []));
    }

    [Fact]
    public async Task UnMovimientoConFechaYaAsentadaSeAsientaDespuesConSuFechaRealEnLaOperacion()
    {
        using var env = NewEnv();
        var known = Entry(D1);
        await Post(env, [known], D2);
        var late = Entry(D1, 50_000m, description: "Cobro atrasado");

        var (result, error, _) = await Post(env, [], D3, Detect(env, [known, late]));

        Assert.Null(error);
        Assert.Equal(1, result!.PostedCount);
        var row = Saved(env).Last();
        Assert.Equal(JournalEntryKind.Auto, row.Kind);
        Assert.Equal(0, row.Revision);
        Assert.Equal(D3, row.Date);
        Assert.Equal(D1, row.OperationDate);
        Assert.Contains("asentado después", row.Description);
        Assert.Empty(Detect(env, [known, late]));
    }

    [Fact]
    public async Task LasCorreccionesVanDespuesDeLosAsientosNuevosYTodasConFechaDelDiaDeAsentado()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        await Post(env, [Entry(D1, 100_000m, source)], D2);
        var edited = Entry(D1, 90_000m, source);
        var fresh = Entry(D3, 10_000m, description: "Cobro nuevo");

        var (result, error, _) = await Post(env, [fresh], D4, Detect(env, [edited]));

        Assert.Null(error);
        Assert.Equal(3, result!.PostedCount);
        Assert.Equal(2, result.CorrectionEntryCount);
        var saved = Saved(env);
        Assert.Equal([1, 2, 3, 4], saved.Select(e => e.Number).ToArray());
        Assert.Equal("Cobro nuevo", saved[1].Description);                          // primero lo nuevo, por fecha
        Assert.Equal([D1, D3, D4, D4], saved.Select(e => e.Date).ToArray());        // y las correcciones al final: el numero sigue el orden de las fechas
        Assert.Equal(saved.OrderBy(e => e.Date).Select(e => e.Number), saved.Select(e => e.Number));
    }

    [Fact]
    public async Task CorregirDosVecesElMismoMovimientoEncadenaLasVersiones()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        await Post(env, [Entry(D1, 100_000m, source)], D1);
        var second = Entry(D1, 110_000m, source);
        await Post(env, [], D2, Detect(env, [second]));
        var third = Entry(D1, 130_000m, source);

        var diffs = Detect(env, [third]);
        var diff = Assert.Single(diffs);
        Assert.Equal(1, diff.Original!.Revision);                                   // se corrige la vigente, no la primera
        Assert.Equal(2, diff.NextRevision);

        var (_, error, _) = await Post(env, [], D3, diffs);

        Assert.Null(error);
        var saved = Saved(env);
        Assert.Equal(5, saved.Count);                                               // original, contraasiento, v1, contraasiento, v2
        Assert.Equal([JournalEntryKind.Auto, JournalEntryKind.Reversal, JournalEntryKind.Auto, JournalEntryKind.Reversal, JournalEntryKind.Auto],
            saved.Select(e => e.Kind).ToArray());
        Assert.Equal(130_000m, saved[4].TotalDebit);
        Assert.Empty(Detect(env, [third]));
    }

    [Fact]
    public async Task UnAsientoNuevoIncompletoEnUnaCorreccionFrenaTodoSinGuardarNada()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        await Post(env, [Entry(D1, 100_000m, source)], D2);
        var broken = Entry(D1, 120_000m, source, complete: false);

        var (result, error, conflict) = await Post(env, [], D3, Detect(env, [broken]));

        Assert.Null(result);
        Assert.True(conflict);
        Assert.Contains("incompletos", error);
        Assert.Single(Saved(env));
        Assert.Equal(D2, env.T.NewContext().FinanceSettings.Single(x => x.BuildingId == env.Building.Id).JournalPostedThrough);
    }

    [Fact]
    public async Task UnAsientoSoloSePuedeAnularUnaVez_YElFalloNoDejaNadaAMedias()
    {
        using var env = NewEnv();
        await Post(env, [Entry(D1)], D2);
        var stale = Detect(env, []);                                                // dos personas ven la misma baja
        Assert.True((await Post(env, [], D3, stale)).Error is null);

        var (result, error, conflict) = await Post(env, [], D4, stale);

        Assert.Null(result);
        Assert.True(conflict);
        Assert.Contains("al mismo tiempo", error);
        Assert.Equal(2, Saved(env).Count);                                          // el original y un solo contraasiento
        Assert.Equal(2, env.T.NewContext().JournalSequences.Single().LastNumber);
        Assert.Equal(D3, env.T.NewContext().FinanceSettings.Single(x => x.BuildingId == env.Building.Id).JournalPostedThrough);
    }

    [Fact]
    public async Task ElLibroMuestraQuienAnuloACadaAsientoYQueAsientoInvierteCadaContraasiento()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        await Post(env, [Entry(D1, 100_000m, source)], D2);
        await Post(env, [], D3, Detect(env, [Entry(D1, 120_000m, source)]));

        var db = env.T.NewContext();
        var dto = await Journal(env, db).GetAsync(await Context(env), null, null, null, null, 1, 100, canPost: true, default);

        Assert.Equal("2026-000002", dto.Entries[0].ReversedByLabel);
        Assert.Null(dto.Entries[0].ReversesLabel);
        Assert.Equal("2026-000001", dto.Entries[1].ReversesLabel);
        Assert.Equal(JournalEntryKind.Reversal, dto.Entries[1].Kind);
        Assert.Null(dto.Entries[2].ReversedByLabel);
        Assert.Equal(dto.TotalDebit, dto.TotalCredit);
    }

    [Fact]
    public async Task ElCaminoPuraDeLasSumasUnContraasientoAnulaExactamenteAlOriginal()
    {
        using var env = NewEnv();
        var source = Guid.NewGuid();
        await Post(env, [Entry(D1, 100_000m, source)], D2);
        await Post(env, [], D3, Detect(env, []));

        var saved = Saved(env);
        var sumByAccount = saved.SelectMany(e => e.Lines).GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.Sum(l => l.Debit - l.Credit));

        Assert.All(sumByAccount.Values, v => Assert.Equal(0m, v));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Asentar de punta a punta con el libro real y bloqueo de la base (SQL Server)
    // ═════════════════════════════════════════════════════════════════════════

    private static FinanceJournalController Api(FinanceEnv env) => new(
        env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), Journal(env, env.T.Db));

    private static async Task Seed(FinanceEnv env)
    {
        var bank = env.Setup();
        env.SeedTemplate();
        env.AddIncome(null, 1_000_000m, FinanceEnv.Yesterday.AddDays(-10));
        var expense = env.AddExpense(null, 110_000m, BuildingExpenseCategory.Other, FinanceEnv.Yesterday.AddDays(-6));
        env.T.Db.BuildingExpenses.Single(x => x.Id == expense.Id).VatRate = 10m;
        env.T.Db.SaveChanges();
        env.Tenant.CompanyId = env.Company.Id;
        env.Tenant.UserId = env.Admin.Id;
        env.Access.Buildings.Add(env.Building.Id);
        var accounting = new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant);
        var (_, error) = await accounting.UpdateRolesAsync(await Context(env), new UpdateAccountingRolesRequest
        {
            Accounts = [new AccountingRoleItemRequest { FinancialAccountId = bank.Id, LedgerCategoryId = env.Cat("1.1.03").Id }],
            SetVatCredit = true,
            VatCreditCategoryId = env.Cat("1.4.06").Id
        }, env.Company.Id, default);
        Assert.Null(error);
    }

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result.Result).StatusCode);

    [SqlServerFact]
    public async Task EditarRevertirYAgregarMovimientosDeUnDiaAsentadoSeCorrigenConContraasientosYElLibroConverge()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await Seed(env);
        var api = Api(env);
        var through = FinanceEnv.Yesterday;
        FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = through }, default));
        Assert.Empty(FinanceEnv.Ok(await api.Differences(env.Building.Id, default)).Items);
        Assert.Equal(2, Saved(env).Count);

        // Un gasto cambia de importe, el ingreso se borra y aparece un gasto nuevo con fecha de un dia ya asentado.
        var expense = env.T.Db.BuildingExpenses.Single(x => x.Amount == 110_000m);
        expense.Amount = 220_000m;
        env.T.Db.BuildingIncomes.Single().IsDeleted = true;
        env.AddExpense(null, 30_000m, BuildingExpenseCategory.Other, through.AddDays(-2));
        env.T.Db.BuildingExpenses.OrderByDescending(x => x.CreatedAtUtc).First().VatRate = 0m;
        env.T.Db.SaveChanges();

        var differences = FinanceEnv.Ok(await api.Differences(env.Building.Id, default));
        Assert.Equal(1, differences.ChangedCount);
        Assert.Equal(1, differences.RemovedCount);
        Assert.Equal(1, differences.MissingCount);
        Assert.Equal(0, differences.IncompleteCount);

        var preview = FinanceEnv.Ok(await api.Preview(env.Building.Id, through, default));
        Assert.Equal(0, preview.EntryCount);
        Assert.Equal(4, preview.CorrectionEntryCount);                          // cambio (2) + baja (1) + faltante (1)
        Assert.Equal(3, preview.Differences.Count);
        Assert.True(preview.CanPost);

        var posted = FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = through }, default));
        Assert.Equal(4, posted.PostedCount);
        Assert.Equal(4, posted.CorrectionEntryCount);

        Assert.Empty(FinanceEnv.Ok(await api.Differences(env.Building.Id, default)).Items);   // converge
        var saved = Saved(env);
        Assert.Equal(6, saved.Count);
        Assert.Equal(2, saved.Count(e => e.Kind == JournalEntryKind.Reversal));
        Assert.Equal(-220_000m - 30_000m,                                        // el ingreso se borro: solo quedan los dos gastos
            saved.SelectMany(e => e.Lines).Where(l => l.Code == "1.1.03").Sum(l => l.Debit - l.Credit));   // el banco coincide con el libro de hoy

        // Sin diferencias ni dia nuevo, no hay nada para asentar.
        AssertStatus(await api.Post(env.Building.Id, new PostJournalRequest { Through = through }, default), 400);
    }

    [SqlServerFact]
    public async Task ConAsientosCambiadosNoSePuedeCerrarElMesHastaCorregirlos()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await Seed(env);
        var api = Api(env);
        var through = FinanceEnv.Yesterday;
        FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = through }, default));
        env.T.Db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id).PeriodClosingEnabled = true;
        env.T.Db.BuildingExpenses.Single(x => x.Amount == 110_000m).Amount = 150_000m;
        env.T.Db.SaveChanges();

        var config = new BuildingConfigController(env.T.Db, env.Access, env.Tenant, new BuildingConfigOverviewService(env.T.Db, new FinanceModuleGate(env.T.Db)));
        var start = env.T.Db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id).FinanceStartDate!.Value;

        var blocked = await config.CloseMonth(env.Building.Id, start.Year, start.Month, default);
        var conflict = Assert.IsType<ConflictObjectResult>(blocked.Result);
        Assert.Contains("journal_differences", conflict.Value!.ToString());
        Assert.Empty(env.T.NewContext().FinancePeriodClosures.ToList());

        FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = through }, default));

        FinanceEnv.Ok(await config.CloseMonth(env.Building.Id, start.Year, start.Month, default));
        Assert.Single(env.T.NewContext().FinancePeriodClosures.ToList());
    }

    [SqlServerFact]
    public async Task LaBaseNoDejaModificarNiBorrarLoAsentadoPeroSiAgregar()
    {
        using var env = new FinanceEnv(sqlServer: true);
        await Seed(env);
        var db = env.T.Db;
        db.Database.ExecuteSqlRaw(JournalImmutability.CreateSql("JournalEntries", JournalImmutability.EntriesTrigger));
        db.Database.ExecuteSqlRaw(JournalImmutability.CreateSql("JournalEntryLines", JournalImmutability.LinesTrigger));
        var api = Api(env);

        FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = FinanceEnv.Yesterday.AddDays(-8) }, default));   // agregar sigue andando
        Assert.Single(Saved(env));

        var update = Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("UPDATE JournalEntries SET Description = N'x'"));
        Assert.Contains("no se puede modificar", update.ToString());
        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("DELETE FROM JournalEntries"));
        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("UPDATE JournalEntryLines SET Debit = 1"));
        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw("DELETE FROM JournalEntryLines"));

        var after = Saved(env).Single();
        Assert.NotEqual("x", after.Description);
        Assert.Equal(2, after.Lines.Count);

        FinanceEnv.Ok(await api.Post(env.Building.Id, new PostJournalRequest { Through = FinanceEnv.Yesterday }, default));             // y se puede seguir asentando
        Assert.Equal(2, Saved(env).Count);
    }
}
