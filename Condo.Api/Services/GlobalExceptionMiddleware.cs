using System.Security.Claims;

namespace Condo.Api.Services;

/// <summary>
/// Atrapa cualquier excepcion no controlada y la convierte en una respuesta 500 en JSON con un codigo de referencia, que tambien
/// queda en el log junto con la excepcion completa. Va despues de CORS y no limpia la respuesta: asi las cabeceras CORS se
/// conservan y el navegador muestra el mensaje del servidor. Sin esto, un error 500 llegaba sin esas cabeceras y la pantalla solo
/// decia "Failed to fetch", sin pistas de que fallo.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // El cliente cerro la conexion o cancelo la peticion: no es un error del servidor.
        }
        catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            // Peticion mal formada o demasiado grande: se responde con el codigo que corresponde, no como 500.
            logger.LogWarning(exception, "Peticion rechazada en {Method} {Path}.", context.Request.Method, context.Request.Path);
            await WriteAsync(context, exception.StatusCode, "La solicitud no es válida o es demasiado grande.", null, exception);
        }
        catch (Exception exception)
        {
            var errorId = Guid.NewGuid().ToString("N")[..8];
            logger.LogError(
                exception, "Error no controlado en {Method} {Path} (referencia {ErrorId}, usuario {UserId}).",
                context.Request.Method, context.Request.Path, errorId, context.User.FindFirstValue(ClaimTypes.NameIdentifier));

            // Si la respuesta ya empezo a enviarse no se puede cambiar el codigo: se deja cortar como antes.
            if (context.Response.HasStarted) throw;

            await WriteAsync(
                context, StatusCodes.Status500InternalServerError,
                $"Ocurrió un error inesperado en el servidor. Si se repite, avisá a soporte con este código: {errorId}.", errorId, exception);
        }
    }

    private async Task WriteAsync(HttpContext context, int statusCode, string message, string? errorId, Exception exception)
    {
        // Se conservan las cabeceras que ya puso CORS; solo se reemplaza el cuerpo.
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new
        {
            message,
            errorId,
            // El detalle tecnico solo se muestra en desarrollo.
            detail = environment.IsDevelopment() ? exception.ToString() : null
        });
    }
}
