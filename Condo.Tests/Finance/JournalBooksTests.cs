using ClosedXML.Excel;
using Condo.Api.Controllers;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Condo.Tests.Finance;

/// <summary>
/// Libros oficiales: Libro Diario con folios (hojas, sumas por hoja y acumuladas, PDF y Excel), Libro Mayor y balance de sumas y saldos. Los
/// calculos y la paginacion son puros; la lectura de lo asentado, los permisos y los archivos se prueban con asientos asentados a mano.
/// </summary>
public class JournalBooksTests
{
    private static readonly Guid BankAccountId = Guid.NewGuid();
    private static readonly DateOnly D1 = new(2026, 3, 5);
    private static readonly DateOnly D2 = new(2026, 3, 10);
    private static readonly DateOnly D3 = new(2026, 3, 20);

    static JournalBooksTests() => QuestPDF.Settings.License = LicenseType.Community;

    // ── Armado ───────────────────────────────────────────────────────────────

    private static JournalEntry Entry(int number, DateOnly date, decimal amount, int lines = 2, int year = 2026, string description = "Expensas")
    {
        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(), FiscalYear = year, Number = number, Date = date, OperationDate = date, Description = description,
            ThirdParty = "Unidad A", Kind = JournalEntryKind.Auto, TotalDebit = amount, TotalCredit = amount
        };
        entry.Lines.Add(new JournalEntryLine { LineNo = 1, Code = "1.1.03", Name = "Banco", Debit = amount, Kind = "Financial" });
        for (var i = 2; i <= lines; i++)
        {
            entry.Lines.Add(new JournalEntryLine
            {
                LineNo = i, Code = i == 2 ? "4.1.01" : $"4.1.0{i}", Name = i == 2 ? "Expensas ordinarias" : $"Otro {i}",
                Credit = i == lines ? amount - (lines - 2) * 1m : 1m, Kind = "Rubro"
            });
        }

        return entry;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Paginacion del Libro Diario (pura)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LosAsientosSeReparteEnHojasConFolioCorrelativoYNingunaSeParte()
    {
        var entries = Enumerable.Range(1, 20).Select(i => Entry(i, D1, 100m * i)).ToList();       // cada asiento ocupa 2 + 2 = 4 renglones

        var pages = JournalBookPaginator.Paginate(entries, 7, 40);

        Assert.Equal(2, pages.Count);                                                             // 10 asientos por hoja
        Assert.Equal([7, 8], pages.Select(p => p.Folio).ToArray());
        Assert.All(pages, p => Assert.True(p.Rows.Sum(r => r.Weight) <= 40));
        Assert.All(pages, p => Assert.Equal(BookRowKind.Entry, p.Rows[0].Kind));                  // toda hoja empieza con una cabecera de asiento
        Assert.Equal(20, pages.SelectMany(p => p.Rows).Count(r => r.Kind == BookRowKind.Entry));
        Assert.DoesNotContain(pages.SelectMany(p => p.Rows), r => r.Continued);
    }

    [Fact]
    public void LasSumasDeCadaHojaLaAnteriorYLaAcumuladaCuadran()
    {
        var entries = Enumerable.Range(1, 25).Select(i => Entry(i, D1, 1_000m + i)).ToList();

        var pages = JournalBookPaginator.Paginate(entries, 1, 20);

        Assert.True(pages.Count >= 3);
        Assert.Equal(0m, pages[0].PreviousDebit);
        for (var i = 1; i < pages.Count; i++)
        {
            Assert.Equal(pages[i - 1].CumulativeDebit, pages[i].PreviousDebit);
            Assert.Equal(pages[i - 1].CumulativeCredit, pages[i].PreviousCredit);
        }

        Assert.All(pages, p => Assert.Equal(p.Rows.Where(r => r.Kind == BookRowKind.Line).Sum(r => r.Debit), p.PageDebit));
        Assert.Equal(entries.Sum(e => e.TotalDebit), pages[^1].CumulativeDebit);
        Assert.Equal(entries.Sum(e => e.TotalCredit), pages[^1].CumulativeCredit);
        Assert.Equal(pages[^1].CumulativeDebit, pages[^1].CumulativeCredit);
    }

    [Fact]
    public void UnAsientoMasGrandeQueUnaHojaContinuaEnLaSiguienteConSuCabeceraMarcada()
    {
        var big = Entry(1, D1, 1_000m, lines: 12);                                               // 2 + 12 = 14 renglones, la hoja admite 8

        var pages = JournalBookPaginator.Paginate([big], 1, 8);

        Assert.True(pages.Count >= 2);
        Assert.False(pages[0].Rows[0].Continued);
        Assert.True(pages[1].Rows[0].Continued);
        Assert.All(pages, p => Assert.True(p.Rows.Sum(r => r.Weight) <= 8));
        Assert.Equal(12, pages.SelectMany(p => p.Rows).Count(r => r.Kind == BookRowKind.Line));
        Assert.Equal(1_000m, pages[^1].CumulativeDebit);
    }

    [Fact]
    public void LaCabeceraNuncaQuedaSolaAlPieDeUnaHoja()
    {
        var entries = Enumerable.Range(1, 30).Select(i => Entry(i, D1, 50m, lines: 2 + i % 4)).ToList();

        var pages = JournalBookPaginator.Paginate(entries, 1, 17);

        Assert.All(pages, p =>
        {
            Assert.NotEqual(BookRowKind.Entry, p.Rows[^1].Kind);
            Assert.True(p.Rows.Sum(r => r.Weight) <= 17);
        });
        Assert.Equal(entries.Sum(e => e.Lines.Count), pages.SelectMany(p => p.Rows).Count(r => r.Kind == BookRowKind.Line));
    }

    [Fact]
    public void SinAsientosNoHayHojasYLosAsientosSalenEnOrdenDeNumero()
    {
        Assert.Empty(JournalBookPaginator.Paginate([], 1));

        var pages = JournalBookPaginator.Paginate([Entry(3, D3, 1m), Entry(1, D1, 1m), Entry(2, D2, 1m)], 1);

        Assert.Equal(["2026-000001", "2026-000002", "2026-000003"], pages[0].Rows.Where(r => r.Kind == BookRowKind.Entry).Select(r => r.Label).ToArray());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Libro Mayor y balance (puros)
    // ═════════════════════════════════════════════════════════════════════════

    private static List<BookLine> Lines(params JournalEntry[] entries) => JournalBooksCalculator.Flatten(entries);

    [Fact]
    public void ElMayorTraeSaldoInicialMovimientosYSaldoAcumuladoPorCuenta()
    {
        var lines = Lines(
            Entry(1, new DateOnly(2025, 12, 20), 500m, year: 2025),                              // anterior al periodo: saldo inicial
            Entry(1, D1, 1_000m), Entry(2, D2, 300m));

        var bank = JournalBooksCalculator.Ledger(lines, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), withMovements: true, "1.1.03").Single();

        Assert.Equal(500m, bank.OpeningBalance);
        Assert.Equal(1_300m, bank.Debit);
        Assert.Equal(0m, bank.Credit);
        Assert.Equal(1_800m, bank.ClosingBalance);
        Assert.Equal([1_500m, 1_800m], bank.Movements.Select(m => m.Balance).ToArray());
        Assert.Equal(["2026-000001", "2026-000002"], bank.Movements.Select(m => m.EntryLabel).ToArray());
    }

    [Fact]
    public void ElMayorOmiteCuentasSinMovimientoNiSaldoYOrdenaPorCodigo()
    {
        var lines = Lines(Entry(1, D1, 100m), Entry(2, new DateOnly(2027, 1, 5), 900m, year: 2027));

        var accounts = JournalBooksCalculator.Ledger(lines, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), withMovements: false);

        Assert.Equal(["1.1.03", "4.1.01"], accounts.Select(a => a.Code).ToArray());               // lo de 2027 no entra
        Assert.All(accounts, a => Assert.Empty(a.Movements));

        var onlyLater = JournalBooksCalculator.Ledger(Lines(Entry(1, D1, 100m)), new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 31), withMovements: false);
        Assert.Equal(2, onlyLater.Count);                                                         // sin movimiento pero con saldo inicial
        Assert.All(onlyLater, a => Assert.Equal(0, a.MovementCount));
    }

    [Fact]
    public void UnAsientoYSuContraasientoDejanLaCuentaEnCero()
    {
        var original = Entry(1, D1, 700m);
        var reversal = Entry(2, D3, 700m);
        foreach (var line in reversal.Lines) (line.Debit, line.Credit) = (line.Credit, line.Debit);

        var accounts = JournalBooksCalculator.Ledger(Lines(original, reversal), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false);

        Assert.All(accounts, a => Assert.Equal(0m, a.ClosingBalance));
        Assert.All(accounts, a => Assert.Equal(a.Debit, a.Credit));
    }

    [Fact]
    public void ElNombreVigenteDeUnaCuentaEsElDelUltimoAsiento()
    {
        var first = Entry(1, D1, 100m);
        var second = Entry(2, D2, 100m);
        second.Lines.First(l => l.Code == "1.1.03").Name = "Banco Itaú cuenta corriente";

        var bank = JournalBooksCalculator.Ledger(Lines(first, second), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false, "1.1.03").Single();

        Assert.Equal("Banco Itaú cuenta corriente", bank.Name);
    }

    [Fact]
    public void ElBalanceSeparaDeudoresYAcreedoresYCuadra()
    {
        var lines = Lines(Entry(1, new DateOnly(2025, 12, 20), 500m, year: 2025), Entry(1, D1, 1_000m), Entry(2, D2, 300m));

        var (rows, totals, balanced, difference) = JournalBooksCalculator.TrialBalance(lines, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var bank = rows.Single(r => r.Code == "1.1.03");
        Assert.Equal((500m, 0m, 1_300m, 0m, 1_800m, 0m), (bank.OpeningDebit, bank.OpeningCredit, bank.Debit, bank.Credit, bank.ClosingDebit, bank.ClosingCredit));
        var income = rows.Single(r => r.Code == "4.1.01");
        Assert.Equal((0m, 500m, 0m, 1_300m, 0m, 1_800m), (income.OpeningDebit, income.OpeningCredit, income.Debit, income.Credit, income.ClosingDebit, income.ClosingCredit));
        Assert.Equal(1_800m, totals.ClosingDebit);
        Assert.Equal(1_800m, totals.ClosingCredit);
        Assert.True(balanced);
        Assert.Equal(0m, difference);
    }

    [Fact]
    public void UnBalanceQueNoCuadraLoDiceYMuestraLaDiferencia()
    {
        var broken = Entry(1, D1, 1_000m);
        broken.Lines.Last().Credit = 900m;                                                       // asiento descuadrado a proposito

        var (_, _, balanced, difference) = JournalBooksCalculator.TrialBalance(Lines(broken), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.False(balanced);
        Assert.Equal(100m, difference);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Lectura de lo asentado, archivos y permisos
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

    private static AccountingEntryDto Dto(DateOnly date, decimal amount, string description = "Expensas")
    {
        var lines = new[]
        {
            new AccountingLineDto { CategoryId = Guid.NewGuid(), Code = "1.1.03", Name = "Banco", Debit = amount, Kind = "Financial" },
            new AccountingLineDto { CategoryId = Guid.NewGuid(), Code = "4.1.01", Name = "Expensas ordinarias", Credit = amount, Kind = "Rubro" }
        };
        return new AccountingEntryDto
        {
            Date = date, Description = description, ThirdParty = "Unidad A", SourceType = LedgerSourceType.OwnerPayment, SourceId = Guid.NewGuid(),
            SourceAccountId = BankAccountId, Lines = lines, Debit = amount, Credit = amount, IsComplete = true
        };
    }

    private static async Task Post(FinanceEnv env, IReadOnlyList<AccountingEntryDto> entries, DateOnly through)
    {
        var db = env.T.NewContext();
        var settings = db.FinanceSettings.Single(x => x.BuildingId == env.Building.Id);
        var journal = new JournalPostingService(db, new AccountingEntriesService(db, new FinanceLedgerService(db), env.Tenant), env.Tenant);
        var (_, error, _) = await journal.PostBuiltAsync(await Context(env), settings, entries, through, env.Company.Id, default);
        Assert.Null(error);
    }

    private static JournalBooksService Books(FinanceEnv env, CondoDbContext? db = null)
    {
        var context = db ?? env.T.NewContext();
        return new JournalBooksService(context, new AccountingBooksService(context, env.Tenant));
    }

    private static FinanceJournalBooksController Api(FinanceEnv env) => new(
        env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db), Books(env, env.T.Db));

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result.Result).StatusCode);

    [Fact]
    public async Task ElMayorYElBalanceLeenLoAsentadoDelEjercicioYElBalanceCuadra()
    {
        using var env = NewEnv();
        await Post(env, [Dto(D1, 1_000m), Dto(D2, 250m)], D3);
        var ctx = await Context(env);

        var ledger = await Books(env).LedgerAsync(ctx, null, null, null, null, withMovements: false, default);
        var bank = ledger.Accounts.Single(a => a.Code == "1.1.03");
        Assert.Equal(2026, ledger.FiscalYear);
        Assert.Equal(new DateOnly(2026, 1, 1), ledger.From);
        Assert.Equal(new DateOnly(2026, 12, 31), ledger.To);
        Assert.Equal(1_250m, bank.ClosingBalance);
        Assert.Empty(bank.Movements);                                                            // el resumen no trae el detalle
        Assert.Equal(ledger.TotalDebit, ledger.TotalCredit);

        var detail = await Books(env).LedgerAsync(ctx, null, null, null, "1.1.03", withMovements: false, default);
        Assert.Equal(["2026-000001", "2026-000002"], detail.Accounts.Single().Movements.Select(m => m.EntryLabel).ToArray());
        Assert.Equal([1_000m, 1_250m], detail.Accounts.Single().Movements.Select(m => m.Balance).ToArray());

        var balance = await Books(env).TrialBalanceAsync(ctx, null, null, null, default);
        Assert.True(balance.IsBalanced);
        Assert.Equal(1_250m, balance.Totals.ClosingDebit);
    }

    [Fact]
    public async Task ElPeriodoSePuedeAcotarPeroNoSalirDelEjercicio()
    {
        using var env = NewEnv();
        await Post(env, [Dto(D1, 1_000m), Dto(D2, 250m)], D3);
        var ctx = await Context(env);

        var narrowed = await Books(env).LedgerAsync(ctx, 2026, D2, new DateOnly(2030, 1, 1), "1.1.03", withMovements: false, default);

        Assert.Equal(D2, narrowed.From);
        Assert.Equal(new DateOnly(2026, 12, 31), narrowed.To);                                   // no pasa del fin del ejercicio
        var bank = narrowed.Accounts.Single();
        Assert.Equal(1_000m, bank.OpeningBalance);                                               // lo anterior al periodo es saldo inicial
        Assert.Single(bank.Movements);
    }

    [Fact]
    public async Task LosLibrosDeOtroEdificioNoSeMezclan()
    {
        using var env = NewEnv();
        await Post(env, [Dto(D1, 1_000m)], D3);
        var other = env.T.AddBuilding(env.Company, "Otro edificio");
        env.T.Db.JournalEntries.Add(new JournalEntry
        {
            CompanyId = env.Company.Id, BuildingId = other.Id, FiscalYear = 2026, Number = 1, Date = D1, OperationDate = D1,
            TotalDebit = 5m, TotalCredit = 5m, ContentHash = new string('a', 64)
        });
        env.T.Db.SaveChanges();

        var balance = await Books(env).TrialBalanceAsync(await Context(env), null, null, null, default);

        Assert.Equal(1_000m, balance.Totals.Debit);
    }

    [Fact]
    public async Task ElLibroImprimibleUsaLosDatosDelTitularLaRubricaYElFolioSugerido()
    {
        using var env = NewEnv();
        env.T.Db.AccountingBookSettings.Add(new AccountingBookSettings
        {
            CompanyId = env.Company.Id, BuildingId = env.Building.Id, Holder = AccountingBookHolder.AdminCompany, HolderRuc = "80012345-0",
            AccountantName = "Ana Gómez", AccountantRegistration = "CP-1", BookNumber = "3", RubricDate = new DateOnly(2026, 1, 15),
            AuthorizedFolios = 200, UsedFolios = 12
        });
        env.T.Db.SaveChanges();
        await Post(env, [Dto(D1, 100m), Dto(D2, 200m)], D3);
        var ctx = await Context(env);
        var building = env.T.Db.Buildings.Include(x => x.Condominium).Single(x => x.Id == env.Building.Id);

        var print = await Books(env).PrintAsync(ctx, building, null, null, 40, default);

        Assert.Equal(13, print.StartFolio);                                                      // folios usados + 1
        Assert.Equal(13, print.Pages[0].Folio);
        Assert.Equal("Empresa Finanzas", print.Books.Effective.LegalName);
        Assert.Equal("80012345-0", print.Books.Effective.Ruc);
        Assert.Equal("3", print.Books.BookNumber);
        Assert.Equal(2, print.EntryCount);
        Assert.Equal(300m, print.TotalDebit);
        Assert.Equal(13, await Books(env).SuggestedStartFolioAsync(env.Building.Id, default));

        var manual = await Books(env).PrintAsync(ctx, building, null, 100, 40, default);
        Assert.Equal(100, manual.Pages[0].Folio);
    }

    [Fact]
    public async Task SinRubricaElFolioSugeridoEs1()
    {
        using var env = NewEnv();

        Assert.Equal(1, await Books(env).SuggestedStartFolioAsync(env.Building.Id, default));
    }

    [Fact]
    public async Task ElPdfDelLibroDiarioSeGeneraConHojasYTambienVacio()
    {
        using var env = NewEnv();
        var building = env.T.Db.Buildings.Include(x => x.Condominium).Single(x => x.Id == env.Building.Id);
        var empty = await Books(env).PrintAsync(await Context(env), building, null, null, 40, default);
        var emptyPdf = new JournalBookPdfDocument(empty).GeneratePdf();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(emptyPdf, 0, 4));

        await Post(env, Enumerable.Range(1, 30).Select(i => Dto(D1.AddDays(i % 10), 100m * i, $"Cobro {i}")).ToList(), D3.AddDays(30));
        var print = await Books(env).PrintAsync(await Context(env), building, null, 5, 40, default);

        Assert.True(print.Pages.Count >= 2);
        var pdf = new JournalBookPdfDocument(print).GeneratePdf();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > emptyPdf.Length);
    }

    [Fact]
    public async Task ElExcelDelLibroDiarioTraeLosFoliosLasSumasPorHojaYElTotal()
    {
        using var env = NewEnv();
        await Post(env, Enumerable.Range(1, 30).Select(i => Dto(D1.AddDays(i % 10), 100m, $"Cobro {i}")).ToList(), D3.AddDays(30));
        var building = env.T.Db.Buildings.Include(x => x.Condominium).Single(x => x.Id == env.Building.Id);
        var print = await Books(env).PrintAsync(await Context(env), building, null, 20, 40, default);

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddJournalBook(workbook, print);
        var ws = workbook.Worksheet("Libro Diario");

        var rows = ws.RowsUsed().Where(r => r.RowNumber() >= 5).ToList();
        Assert.Contains(rows, r => r.Cell(6).GetString() == "Total del ejercicio");
        var total = rows.Single(r => r.Cell(6).GetString() == "Total del ejercicio");
        Assert.Equal(3_000d, total.Cell(7).GetDouble());
        Assert.Equal(3_000d, total.Cell(8).GetDouble());
        Assert.Equal(print.Pages.Count, rows.Count(r => r.Cell(6).GetString() == "Suma de la hoja"));
        Assert.Equal(20, rows[0].Cell(1).GetValue<int>());                                       // folio inicial
    }

    [Fact]
    public async Task LosExcelDelMayorYDelBalanceSeArman()
    {
        using var env = NewEnv();
        await Post(env, [Dto(D1, 1_000m), Dto(D2, 250m)], D3);
        var ctx = await Context(env);

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddJournalLedger(workbook, await Books(env).LedgerAsync(ctx, null, null, null, null, withMovements: true, default));
        FinanceExcelExporter.AddJournalTrialBalance(workbook, await Books(env).TrialBalanceAsync(ctx, null, null, null, default));

        var mayor = workbook.Worksheet("Libro Mayor");
        Assert.Contains(mayor.RowsUsed(), r => r.Cell(4).GetString() == "2026-000001");
        Assert.Contains(mayor.RowsUsed(), r => r.Cell(5).GetString() == "Totales y saldo final");
        var sumas = workbook.Worksheet("Sumas y saldos");
        Assert.Contains(sumas.RowsUsed(), r => r.Cell(1).GetString().StartsWith("El balance cuadra"));
    }

    [Fact]
    public async Task LoVenLosCuatroRolesYUnEdificioAjenoOSinFinanzasNo()
    {
        using var env = NewEnv();
        await Post(env, [Dto(D1, 1_000m)], D3);
        var api = Api(env);

        foreach (var role in new[] { "SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager" })
        {
            env.LoginAs(role);
            Assert.NotEmpty(FinanceEnv.Ok(await api.Ledger(env.Building.Id, null, null, null, null, default)).Accounts);
            Assert.True(FinanceEnv.Ok(await api.TrialBalance(env.Building.Id, null, null, null, default)).IsBalanced);
            Assert.IsType<FileContentResult>(await api.BookPdf(env.Building.Id, null, null, default));
        }

        env.LoginAs("CompanyAdmin");
        env.Access.Buildings.Clear();
        Assert.IsNotType<OkObjectResult>((await api.Ledger(env.Building.Id, null, null, null, null, default)).Result);
        Assert.IsNotType<FileContentResult>(await api.BookPdf(env.Building.Id, null, null, default));
        Assert.IsNotType<FileContentResult>(await api.BookExcel(env.Building.Id, null, null, default));
        Assert.IsNotType<FileContentResult>(await api.ExportLedger(env.Building.Id, null, null, null, default));
        Assert.IsNotType<FileContentResult>(await api.ExportTrialBalance(env.Building.Id, null, null, null, default));

        env.Access.Buildings.Add(env.Building.Id);
        env.T.Db.Buildings.Single(x => x.Id == env.Building.Id).FinanceModuleEnabled = false;
        env.T.Db.SaveChanges();
        Assert.IsNotType<OkObjectResult>((await api.TrialBalance(env.Building.Id, null, null, null, default)).Result);
    }

    [Fact]
    public async Task ElPrimerFolioDebeSerPositivoYLosArchivosSalenConSuNombre()
    {
        using var env = NewEnv();
        await Post(env, [Dto(D1, 1_000m)], D3);
        var api = Api(env);

        Assert.IsType<BadRequestObjectResult>(await api.BookPdf(env.Building.Id, null, 0, default));
        Assert.IsType<BadRequestObjectResult>(await api.BookExcel(env.Building.Id, null, -3, default));

        var pdf = Assert.IsType<FileContentResult>(await api.BookPdf(env.Building.Id, 2026, 12, default));
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.EndsWith("-2026.pdf", pdf.FileDownloadName);
        Assert.StartsWith("finanzas-libro-diario-", pdf.FileDownloadName);

        var excel = Assert.IsType<FileContentResult>(await api.BookExcel(env.Building.Id, 2026, 12, default));
        Assert.EndsWith("-2026.xlsx", excel.FileDownloadName);
        Assert.IsType<FileContentResult>(await api.ExportLedger(env.Building.Id, null, null, null, default));
        Assert.IsType<FileContentResult>(await api.ExportTrialBalance(env.Building.Id, null, null, null, default));
    }

    [Fact]
    public async Task ElLibroDiarioDeLaPantallaTraeElFolioSugerido()
    {
        using var env = NewEnv();
        env.T.Db.AccountingBookSettings.Add(new AccountingBookSettings { CompanyId = env.Company.Id, BuildingId = env.Building.Id, UsedFolios = 40 });
        env.T.Db.SaveChanges();
        var journal = new FinanceJournalController(
            env.T.Db, env.Access, env.Tenant, new FinanceModuleGate(env.T.Db), new FinanceLedgerService(env.T.Db),
            new JournalPostingService(env.T.Db, new AccountingEntriesService(env.T.Db, new FinanceLedgerService(env.T.Db), env.Tenant), env.Tenant),
            Books(env, env.T.Db));

        var dto = FinanceEnv.Ok(await journal.Get(env.Building.Id, null, null, null, null, 1, 100, default));

        Assert.Equal(41, dto.SuggestedStartFolio);
    }
}
