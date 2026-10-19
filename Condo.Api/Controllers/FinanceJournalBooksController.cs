using ClosedXML.Excel;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

/// <summary>
/// Libros oficiales del edificio a partir de lo asentado en el Libro Diario: Libro Diario imprimible (PDF y Excel) con folios, Libro Mayor y
/// balance de sumas y saldos (pantalla y Excel). Solo lectura: los ven los cuatro roles administrativos. Exige la configuracion inicial de
/// Finanzas completa.
/// </summary>
[Route("api/finance/journal")]
public class FinanceJournalBooksController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    JournalBooksService books) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    // ─── Libro Mayor ──────────────────────────────────────────────────────────

    [HttpGet("ledger")]
    public async Task<ActionResult<JournalLedgerDto>> Ledger(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? account, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await books.LedgerAsync(ctx!, fiscalYear, from, to, account, withMovements: false, cancellationToken));
    }

    [HttpGet("ledger/export")]
    public async Task<IActionResult> ExportLedger(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        var ledger = await books.LedgerAsync(ctx!, fiscalYear, from, to, account: null, withMovements: true, cancellationToken);
        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddJournalLedger(workbook, ledger);
        return File(FinanceExcelExporter.Save(workbook), FinanceExcelExporter.ContentType,
            FinanceExcelExporter.FileName("libro-mayor", ctx!.BuildingName, $"{ledger.FiscalYear}"));
    }

    // ─── Balance de sumas y saldos ────────────────────────────────────────────

    [HttpGet("trial-balance")]
    public async Task<ActionResult<JournalTrialBalanceDto>> TrialBalance(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await books.TrialBalanceAsync(ctx!, fiscalYear, from, to, cancellationToken));
    }

    [HttpGet("trial-balance/export")]
    public async Task<IActionResult> ExportTrialBalance(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        var balance = await books.TrialBalanceAsync(ctx!, fiscalYear, from, to, cancellationToken);
        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddJournalTrialBalance(workbook, balance);
        return File(FinanceExcelExporter.Save(workbook), FinanceExcelExporter.ContentType,
            FinanceExcelExporter.FileName("sumas-y-saldos", ctx!.BuildingName, $"{balance.FiscalYear}"));
    }

    // ─── Balance general y estado de resultados ───────────────────────────────

    [HttpGet("statements")]
    public async Task<ActionResult<JournalStatementsDto>> Statements(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        return Ok(await books.StatementsAsync(ctx!, fiscalYear, asOf, cancellationToken));
    }

    [HttpGet("statements/export")]
    public async Task<IActionResult> ExportStatements(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;

        var statements = await books.StatementsAsync(ctx!, fiscalYear, asOf, cancellationToken);
        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddJournalStatements(workbook, statements);
        return File(FinanceExcelExporter.Save(workbook), FinanceExcelExporter.ContentType,
            FinanceExcelExporter.FileName("balance-y-resultados", ctx!.BuildingName, $"{statements.FiscalYear}"));
    }

    // ─── Libro Diario imprimible ──────────────────────────────────────────────

    [HttpGet("book/pdf")]
    public async Task<IActionResult> BookPdf(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] int? startFolio, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (startFolio is < 1 or > JournalBooksService.MaxStartFolio) return BadRequest("El primer folio debe ser un número positivo.");

        var print = await LoadPrintAsync(ctx!, fiscalYear, startFolio, cancellationToken);
        if (print is null) return NotFound();

        var pdf = new JournalBookPdfDocument(print).GeneratePdf();
        return File(pdf, "application/pdf", FileName(ctx!.BuildingName, print.FiscalYear, "pdf"));
    }

    [HttpGet("book/export")]
    public async Task<IActionResult> BookExcel(
        [FromQuery] Guid buildingId, [FromQuery] int? fiscalYear, [FromQuery] int? startFolio, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(buildingId, cancellationToken);
        if (denied is not null) return denied;
        if (startFolio is < 1 or > JournalBooksService.MaxStartFolio) return BadRequest("El primer folio debe ser un número positivo.");

        var print = await LoadPrintAsync(ctx!, fiscalYear, startFolio, cancellationToken);
        if (print is null) return NotFound();

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddJournalBook(workbook, print);
        return File(FinanceExcelExporter.Save(workbook), FinanceExcelExporter.ContentType,
            FinanceExcelExporter.FileName("libro-diario", ctx!.BuildingName, $"{print.FiscalYear}"));
    }

    private async Task<JournalBookPrint?> LoadPrintAsync(LedgerContext ctx, int? fiscalYear, int? startFolio, CancellationToken cancellationToken)
    {
        var building = await Db.Buildings.AsNoTracking().Include(x => x.Condominium)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == ctx.BuildingId, cancellationToken);
        return building is null
            ? null
            : await books.PrintAsync(ctx, building, fiscalYear, startFolio, JournalBookPdfDocument.RowsPerPage, cancellationToken);
    }

    private static string FileName(string buildingName, int year, string extension)
    {
        var excel = FinanceExcelExporter.FileName("libro-diario", buildingName, $"{year}");
        return Path.ChangeExtension(excel, extension);
    }
}
