using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Documentos y trazabilidad del marketplace (fase 8): comprobante interno de reserva en PDF, notas de cambio de propietario
/// principal y el historial economico de una operacion. Cada endpoint valida en el servidor el edificio y que parte es quien pide.
/// Los PDF aceptan <c>access_token</c> en la URL (como el resto de los PDF del sistema) para abrirse desde el navegador o la app.
/// </summary>
[ApiController]
[Authorize]
[Route("api/marketplace")]
public class MarketplaceDocumentsController(
    MarketplaceDocumentService documents,
    MarketplaceHandoverService handovers) : ControllerBase
{
    // Comprobante interno de la reserva (PDF, no fiscal). Comprador, propietario de la reserva o personal del edificio.
    [HttpGet("reservations/{id:guid}/receipt-pdf")]
    public async Task<IActionResult> GetReceipt(Guid id, CancellationToken ct) =>
        ToFile(await documents.GetReceiptAsync(id, ct));

    // Historial economico de la operacion (solo personal del edificio).
    [HttpGet("reservations/{id:guid}/history")]
    public async Task<ActionResult<MarketplaceOperationHistoryDto>> GetHistory(Guid id, CancellationToken ct) =>
        (await documents.GetHistoryAsync(id, ct)).ToActionResult(this);

    // Notas de cambio de propietario principal del edificio (las no leidas primero).
    [HttpGet("handover-notes")]
    public async Task<ActionResult<List<MarketplaceHandoverNoteDto>>> GetNotes(
        [FromQuery] Guid buildingId, [FromQuery] bool includeRead, CancellationToken ct) =>
        (await handovers.GetNotesAsync(buildingId, includeRead, ct)).ToActionResult(this);

    // Abre la nota con la situacion actual de cada operacion (la primera vez queda marcada como leida).
    [HttpGet("handover-notes/{id:guid}")]
    public async Task<ActionResult<MarketplaceHandoverNoteDto>> GetNote(Guid id, CancellationToken ct) =>
        (await handovers.GetNoteAsync(id, ct)).ToActionResult(this);

    [HttpGet("handover-notes/{id:guid}/pdf")]
    public async Task<IActionResult> GetNotePdf(Guid id, CancellationToken ct) =>
        ToFile(await documents.GetHandoverPdfAsync(id, ct));

    private IActionResult ToFile(MarketplaceResult<MarketplaceDocumentFile> result)
    {
        if (!result.Ok)
        {
            return result.ToActionResult(this).Result!;
        }

        var file = result.Value!;
        return File(file.Bytes, "application/pdf", file.FileName);
    }
}
