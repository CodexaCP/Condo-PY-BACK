using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reportes/comparativo-edificios")]
public class BuildingComparisonController(IAccessScopeService accessScope, IBuildingComparisonService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var buildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
        var report = await service.BuildReportAsync(buildingIds, fromDate, toDate, cancellationToken);
        return Ok(report);
    }

    [HttpGet("pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadPdf(
        [FromQuery(Name = "access_token")] string? _,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var buildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
        var report = await service.BuildReportAsync(buildingIds, fromDate, toDate, cancellationToken);

        var document = new BuildingComparisonPdfDocument(report);
        var bytes = document.GeneratePdf();
        var filename = $"comparativo_edificios_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    [HttpGet("excel")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadExcel(
        [FromQuery(Name = "access_token")] string? _,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var buildingIds = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);
        var report = await service.BuildReportAsync(buildingIds, fromDate, toDate, cancellationToken);

        var bytes = BuildingComparisonExcelBuilder.Build(report);
        var filename = $"comparativo_edificios_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }
}
