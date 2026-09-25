using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reportes/libro-movimientos")]
public class LibroMovimientosController(IAccessScopeService accessScope, ILibroMovimientosService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid buildingId,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken)
    {
        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken))
            return Forbid();

        var report = await service.BuildReportAsync(buildingId, fromDate, toDate, cancellationToken);
        if (report is null) return NotFound();

        return Ok(report);
    }

    [HttpGet("pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadPdf(
        [FromQuery(Name = "access_token")] string? _,
        [FromQuery] Guid buildingId,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken))
            return Forbid();

        var report = await service.BuildReportAsync(buildingId, fromDate, toDate, cancellationToken);
        if (report is null) return NotFound();

        var document = new LibroMovimientosPdfDocument(report);
        var bytes = document.GeneratePdf();
        var filename = $"libro_movimientos_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    [HttpGet("excel")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadExcel(
        [FromQuery(Name = "access_token")] string? _,
        [FromQuery] Guid buildingId,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        if (!await accessScope.CanAccessBuildingAsync(buildingId, cancellationToken))
            return Forbid();

        var report = await service.BuildReportAsync(buildingId, fromDate, toDate, cancellationToken);
        if (report is null) return NotFound();

        var bytes = LibroMovimientosExcelBuilder.Build(report);
        var filename = $"libro_movimientos_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }
}
