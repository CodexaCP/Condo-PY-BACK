using Condo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Pagina publica a la que lleva el QR de un documento (sin sesion). Responde un resumen minimo con el estado del documento; un token
/// inexistente o mal formado responde igual que cualquier otro desconocido, sin pistas para recorrerlos.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/verify")]
public class DocumentVerificationController(DocumentVerificationService verification) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<DocumentVerificationResult>> Get(string token, CancellationToken cancellationToken)
    {
        // Se consulta siempre el estado actual: nada de guardarlo en cache (un documento reemplazado tiene que avisarlo enseguida).
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex";
        return Ok(await verification.LookupAsync(token, cancellationToken));
    }
}
