using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Pago de una reserva (comprador) y su revision (personal del edificio). El edificio, el monto esperado y los permisos se
/// validan siempre en el servidor. Con el plan del edificio vencido, el middleware de planes ya bloquea las escrituras del
/// personal (confirmar y rechazar), igual que en los pagos de expensas.
/// </summary>
[ApiController]
[Authorize]
[Route("api/marketplace")]
public class MarketplacePaymentsController(MarketplacePaymentService payments) : ControllerBase
{
    // ── Comprador ────────────────────────────────────────────────────────────

    // Datos para pagar (incluye los datos para transferir, solo mientras la reserva espera el pago).
    [HttpGet("reservations/{id:guid}/payment-info")]
    public async Task<ActionResult<MarketplacePaymentInfoDto>> GetPaymentInfo(Guid id, CancellationToken ct) =>
        (await payments.GetPaymentInfoAsync(id, ct)).ToActionResult(this);

    // Sube el comprobante: la reserva pasa a "en revision" y se avisa a quienes revisan.
    [HttpPost("reservations/{id:guid}/payment")]
    public async Task<ActionResult<MarketplaceReservationDto>> SubmitPayment(
        Guid id, [FromBody] MarketplaceSubmitPaymentRequest request, CancellationToken ct) =>
        (await payments.SubmitPaymentAsync(id, request, ct)).ToActionResult(this);

    // ── Personal del edificio ────────────────────────────────────────────────

    // Pagos esperando revision en el edificio.
    [HttpGet("payments/pending")]
    public async Task<ActionResult<List<MarketplaceReviewItemDto>>> GetPending([FromQuery] Guid buildingId, CancellationToken ct) =>
        (await payments.GetPendingForReviewAsync(buildingId, ct)).ToActionResult(this);

    [HttpPost("payments/{id:guid}/approve")]
    public async Task<ActionResult<MarketplaceReviewItemDto>> Approve(
        Guid id, [FromBody] MarketplaceApproveRequest request, CancellationToken ct) =>
        (await payments.ApproveAsync(id, request, ct)).ToActionResult(this);

    [HttpPost("payments/{id:guid}/reject")]
    public async Task<ActionResult<MarketplaceReviewItemDto>> Reject(
        Guid id, [FromBody] MarketplaceRejectRequest request, CancellationToken ct) =>
        (await payments.RejectAsync(id, request, ct)).ToActionResult(this);
}
