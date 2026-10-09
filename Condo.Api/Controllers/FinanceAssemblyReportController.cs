using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;

namespace Condo.Api.Controllers;

/// <summary>
/// Informe para la asamblea de propietarios en PDF. Es de consulta (los cuatro roles administrativos con acceso al edificio), usa los mismos
/// servicios y numeros que las pantallas de Finanzas y exige la configuracion inicial completa. Se pide por POST porque lleva las notas del
/// administrador (texto libre que no se guarda). La morosidad va agregada: no figura ninguna unidad ni propietario.
/// </summary>
[Route("api/finance/assembly-report")]
public class FinanceAssemblyReportController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledgerService,
    AssemblyReportService assembly) : FinanceLedgerControllerBase(dbContext, accessScope, tenantContext, gate, ledgerService)
{
    // Hora local de Paraguay (UTC-3), la misma que usa el libro para decidir "hoy".
    private static DateTime NowLocal() => DateTime.UtcNow.AddHours(-3);

    /// <summary>Los datos del informe (los mismos que van al PDF), para mostrarlos en pantalla antes de descargar.</summary>
    [HttpPost]
    public async Task<ActionResult<AssemblyReportDto>> Preview([FromBody] AssemblyReportRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(request.BuildingId, cancellationToken);
        if (denied is not null) return denied;

        var (report, error) = await assembly.BuildAsync(ctx!, request.From, request.To, request.Notes, NowLocal(), cancellationToken);
        return error is not null ? BadRequest(error) : Ok(report);
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> Pdf([FromBody] AssemblyReportRequest request, CancellationToken cancellationToken)
    {
        var (denied, ctx) = await RequireLedgerAsync(request.BuildingId, cancellationToken);
        if (denied is not null) return denied;

        var (report, error) = await assembly.BuildAsync(ctx!, request.From, request.To, request.Notes, NowLocal(), cancellationToken);
        if (error is not null) return BadRequest(error);

        var bytes = new AssemblyReportPdfDocument(report!).GeneratePdf();
        return File(bytes, "application/pdf", $"informe_asamblea_{report!.From:yyyyMMdd}_{report.To:yyyyMMdd}.pdf");
    }
}
