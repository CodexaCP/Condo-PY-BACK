using ClosedXML.Excel;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// IVA de las compras del edificio: el libro de compras por rango de fechas (por fecha de la factura del proveedor) con el IVA incluido
/// discriminado por tasa, y su Excel para el contador. Es de consulta (los cuatro roles administrativos con acceso al edificio) y exige el
/// modulo Finanzas disponible; no exige la configuracion inicial porque no depende del libro de caja.
/// </summary>
[Route("api/finance/vat")]
public class FinanceVatController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceVatService vat) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("purchases")]
    public async Task<ActionResult<VatPurchasesBookDto>> Purchases(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (book, error, denied) = await LoadAsync(buildingId, from, to, cancellationToken);
        if (denied is not null) return denied;
        return error is null ? Ok(book) : BadRequest(error);
    }

    [HttpGet("purchases/export")]
    public async Task<IActionResult> ExportPurchases(
        [FromQuery] Guid buildingId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (book, error, denied) = await LoadAsync(buildingId, from, to, cancellationToken);
        if (denied is not null) return denied;
        if (error is not null) return BadRequest(error);

        using var workbook = new XLWorkbook();
        FinanceExcelExporter.AddVatPurchases(workbook, book!);
        return File(
            FinanceExcelExporter.Save(workbook), ExcelContentType,
            FinanceExcelExporter.FileName("libro-de-compras", book!.BuildingName, $"{book.From:yyyyMMdd}-{book.To:yyyyMMdd}"));
    }

    // Por defecto, el mes en curso (hora de Paraguay).
    private async Task<(VatPurchasesBookDto? Book, string? Error, ActionResult? Denied)> LoadAsync(
        Guid buildingId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var denied = await RequireModuleAsync(buildingId, write: false, cancellationToken);
        if (denied is not null) return (null, null, denied);

        var today = FinancePeriods.Today();
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? FinancePeriods.EndOfMonth(start.Year, start.Month);

        var name = await Db.Buildings.AsNoTracking().Where(x => x.Id == buildingId).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var (book, error) = await vat.PurchasesAsync(buildingId, name, start, end, cancellationToken);
        return (book, error, null);
    }
}
