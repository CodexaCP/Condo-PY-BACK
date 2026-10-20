using Condo.Domain.Enums;

namespace Condo.Application.Services;

// Contrato de un proveedor de pagos online (Bancard, Pagopar...). El orquestador de cobros no conoce a ningun proveedor: solo habla con esta
// interfaz. Sumar un proveedor es una clase que la implementa, una entrada en el catalogo y pruebas; no toca el modelo de datos.

/// <summary>Datos de la conexion que el proveedor necesita: ya descifrados, en memoria y solo durante la llamada.</summary>
public sealed record ProviderConnectionInfo(
    Guid ConnectionId,
    string PublicId,
    PaymentEnvironment Environment,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets);

/// <summary>Lo que se le pide al proveedor al abrir un cobro. <see cref="ProviderReference"/> es el identificador que sale de nuestra secuencia.</summary>
public sealed record CreateSessionRequest(
    string ProviderReference, long AttemptNumber, decimal Amount, string Currency, string Description, string CallbackUrl, DateTime ExpiresAtUtc);

/// <summary>Sesion abierta en el proveedor: con <see cref="SessionId"/> se reconoce el aviso; el propietario paga en <see cref="CheckoutUrl"/>.</summary>
public sealed record ProviderSession(string SessionId, string? CheckoutUrl, DateTime? ExpiresAtUtc);

public enum ProviderPaymentStatus
{
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
    Reversed = 4
}

/// <summary>Aviso del proveedor ya normalizado, igual para todos. El contenido redactado nunca lleva datos de tarjeta.</summary>
public sealed record ProviderCallback(
    string? SessionId,
    string? ProviderReference,
    ProviderPaymentStatus Status,
    decimal Amount,
    string Currency,
    string? TransactionId,
    string? AuthorizationCode,
    DateTime? ConfirmedAtUtc,
    string? CardBrand,
    string? CardLast4,
    string StatusRaw,
    // Proveedor + transaccion + estado: un mismo aviso repetido tiene siempre la misma clave.
    string DedupeKey,
    string RedactedPayloadJson);

/// <summary>Resultado de verificar y normalizar un aviso. Si no es valido (firma incorrecta, cuerpo ilegible) no hay <see cref="Callback"/>.</summary>
public sealed record CallbackParseResult(bool Valid, bool? SignatureOk, ProviderCallback? Callback, string? Error)
{
    public static CallbackParseResult Ok(ProviderCallback callback, bool? signatureOk) => new(true, signatureOk, callback, null);

    public static CallbackParseResult Invalid(string error, bool? signatureOk = null) => new(false, signatureOk, null, error);
}

/// <summary>Lo que cada proveedor puede o no hacer; el orquestador lo respeta.</summary>
public sealed record ProviderCapabilities(
    // El aviso va firmado y se verifica (Tpago no).
    bool SignedCallback,
    // Si el comercio no responde "exito" al aviso, el proveedor devuelve el dinero al pagador (Tpago). Permite rechazar un cobro que no se puede
    // aplicar respondiendo error; si no, queda como devolucion manual.
    bool RefundsWhenNotAcknowledged);

public interface IPaymentProvider
{
    string ProviderCode { get; }
    string ProductCode { get; }
    ProviderCapabilities Capabilities { get; }

    Task<ProviderSession> CreateSessionAsync(ProviderConnectionInfo connection, CreateSessionRequest request, CancellationToken ct);

    /// <summary>
    /// Verifica y normaliza un aviso. <paramref name="headers"/> con las claves en minusculas. No toca la base ni aplica nada: solo dice que
    /// aviso llego (o que no es valido).
    /// </summary>
    CallbackParseResult ParseCallback(ProviderConnectionInfo connection, string rawBody, IReadOnlyDictionary<string, string> headers);
}

/// <summary>Busca el adaptador de cada proveedor y modalidad.</summary>
public sealed class PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers)
{
    private readonly List<IPaymentProvider> _providers = providers.ToList();

    public IPaymentProvider? Find(string? providerCode, string? productCode) =>
        _providers.FirstOrDefault(x =>
            string.Equals(x.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
}
