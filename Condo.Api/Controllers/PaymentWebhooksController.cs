using System.Text;
using Condo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Aviso (callback) del proveedor de pagos: anonimo, sin sesion y server-to-server, por eso no lleva CORS. La conexion se identifica por el
/// identificador aleatorio de la direccion. Siempre responde JSON {"status":"success"} solo cuando el pago quedo registrado (o ya lo estaba);
/// cualquier otra respuesta le dice al proveedor que no se aplico.
/// </summary>
[ApiController]
[AllowAnonymous]
[DisableCors]
[Route("api/payment-webhooks")]
public class PaymentWebhooksController(OnlinePaymentService service) : ControllerBase
{
    // Un aviso real pesa unos cientos de bytes: un cuerpo grande no es del proveedor.
    private const int MaxBodyChars = 64 * 1024;

    [HttpPost("{publicId}")]
    public async Task<IActionResult> Receive(string publicId, CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true))
        {
            var buffer = new char[MaxBodyChars + 1];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (read > MaxBodyChars) return StatusCode(StatusCodes.Status413PayloadTooLarge, new { status = "error", message = "Aviso demasiado grande." });
            body = new string(buffer, 0, read);
        }

        var headers = Request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());
        var result = await service.HandleCallbackAsync(publicId, body, headers, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return StatusCode(result.StatusCode, result.Success
            ? new { status = "success" }
            : new { status = "error", message = result.Message });
    }
}
