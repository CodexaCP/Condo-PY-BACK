using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

public class FakePaymentRequest
{
    // Para simular un aviso con un importe distinto al del cobro (nulo = el importe del cobro).
    public decimal? Amount { get; set; }
}

/// <summary>
/// Simulador del proveedor falso: confirma o rechaza un cobro como lo haria el proveedor. NO confirma por atajo: arma el aviso firmado con la
/// clave de la conexion y lo procesa por el mismo camino que el webhook real. Solo existe si el servidor lo habilita
/// (Payments:EnableFakeProvider) y solo para conexiones del simulador.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payment-fake")]
public class PaymentFakeController(
    ICondoDbContext dbContext, OnlinePaymentService service, ITenantContext tenantContext, IAccessScopeService accessScope,
    ISecretProtector protector, OnlinePaymentSettings settings) : ControllerBase
{
    private bool CanSimulate => tenantContext.IsSuperAdmin || (tenantContext.IsCompanyAdmin && accessScope.HasFullCompanyScope);

    [HttpPost("{attemptNumber:long}/confirm")]
    public Task<IActionResult> Confirm(long attemptNumber, [FromBody] FakePaymentRequest? request, CancellationToken ct) =>
        SimulateAsync(attemptNumber, "confirmed", request, ct);

    [HttpPost("{attemptNumber:long}/reject")]
    public Task<IActionResult> Reject(long attemptNumber, CancellationToken ct) =>
        SimulateAsync(attemptNumber, "rejected", null, ct);

    private async Task<IActionResult> SimulateAsync(long attemptNumber, string status, FakePaymentRequest? request, CancellationToken ct)
    {
        if (!settings.FakeProviderEnabled) return NotFound();
        if (!CanSimulate) return Forbid();

        var attempt = await dbContext.PaymentAttempts.AsNoTracking()
            .Include(x => x.Connection)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.AttemptNumber == attemptNumber, ct);
        if (attempt?.Connection is null) return NotFound();
        if (!tenantContext.IsSuperAdmin && attempt.CompanyId != tenantContext.CompanyId) return NotFound();

        var connection = attempt.Connection;
        if (!string.Equals(connection.ProviderCode, FakePaymentProvider.Code, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "not_fake", message = "El cobro no es de una conexión del simulador." });
        if (attempt.Status is not (PaymentAttemptStatus.Pending or PaymentAttemptStatus.Created or PaymentAttemptStatus.Expired))
            return Conflict(new { error = "attempt_closed", message = $"El intento está en estado {attempt.Status}: no se puede simular otro aviso." });
        if (string.IsNullOrEmpty(connection.SecretsCipher) || string.IsNullOrEmpty(attempt.ProviderSessionId))
            return Conflict(new { error = "attempt_not_ready", message = "El intento todavía no tiene sesión con el proveedor." });

        string privateKey;
        try
        {
            privateKey = protector.Unprotect(connection.SecretsCipher, connection.KeyId ?? string.Empty, connection.PublicId)[FakePaymentProvider.SecretKey];
        }
        catch (Exception exception) when (exception is SecretProtectorException or KeyNotFoundException)
        {
            return Conflict(new { error = "secrets_unreadable", message = "No se pudo leer la clave de firma de la conexión." });
        }

        var (body, signature) = FakePaymentProvider.BuildSignedCallback(
            privateKey, attempt.ProviderSessionId!, attempt.ProviderReference, status, request?.Amount ?? attempt.Amount);
        var headers = new Dictionary<string, string> { [FakePaymentProvider.SignatureHeader] = signature };

        var result = await service.HandleCallbackAsync(
            connection.PublicId, body, headers, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        var intent = await service.GetForStaffAsync(attempt.IntentId, attempt.CompanyId, null, includeEvents: true, ct);

        return Ok(new { webhookStatusCode = result.StatusCode, success = result.Success, message = result.Message, payment = intent });
    }
}
