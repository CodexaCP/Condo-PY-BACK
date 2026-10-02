using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Services;

/// <summary>Error de negocio del marketplace con el codigo HTTP que le corresponde.</summary>
public sealed record MarketplaceError(int StatusCode, string Message, string? Code = null)
{
    public static MarketplaceError NotFound(string message = "No se encontró lo que buscás.") => new(StatusCodes.Status404NotFound, message);
    public static MarketplaceError BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static MarketplaceError Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static MarketplaceError Forbidden(string message) => new(StatusCodes.Status403Forbidden, message, MarketplaceModuleGate.ForbiddenCode);

    // Falta de acceso al edificio: no existe/ajeno -> 404 (no se revela); sin edificio -> 400; modulo apagado o sin plan -> 403 con codigo.
    public static MarketplaceError FromAccess(MarketplaceAccess access) => access.ErrorCode switch
    {
        MarketplaceScope.NotFoundCode => new MarketplaceError(StatusCodes.Status404NotFound, access.Message ?? "No se encontró el edificio."),
        MarketplaceScope.BuildingRequiredCode => new MarketplaceError(StatusCodes.Status400BadRequest, access.Message ?? "El edificio es obligatorio."),
        _ => new MarketplaceError(StatusCodes.Status403Forbidden, access.Message ?? "Sin acceso.", access.ErrorCode)
    };
}

public sealed class MarketplaceResult<T>
{
    private MarketplaceResult(T? value, MarketplaceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public MarketplaceError? Error { get; }
    public bool Ok => Error is null;

    public static MarketplaceResult<T> Success(T value) => new(value, null);
    public static MarketplaceResult<T> Fail(MarketplaceError error) => new(default, error);
}

public static class MarketplaceResultExtensions
{
    // Traduce el resultado al formato de respuesta del resto de la API: texto plano en 400/409, { error, message } en 403.
    public static ActionResult<T> ToActionResult<T>(this MarketplaceResult<T> result, ControllerBase controller)
    {
        if (result.Ok)
        {
            return controller.Ok(result.Value);
        }

        var error = result.Error!;
        return error.StatusCode switch
        {
            StatusCodes.Status400BadRequest => controller.BadRequest(error.Message),
            StatusCodes.Status404NotFound => controller.NotFound(),
            StatusCodes.Status409Conflict => controller.Conflict(error.Message),
            _ => controller.StatusCode(error.StatusCode, new { error = error.Code ?? MarketplaceModuleGate.ForbiddenCode, message = error.Message })
        };
    }
}
