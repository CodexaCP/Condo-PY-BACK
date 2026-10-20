using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Condo.Application.Services;

namespace Condo.Api.Services;

/// <summary>
/// Interruptores del cobro online, leidos de la configuracion del servidor (seccion "Payments"). Ambos arrancan apagados: el cobro se
/// habilita a proposito y el proveedor falso solo se enciende en entornos de prueba.
/// </summary>
public sealed class OnlinePaymentSettings(IConfiguration configuration)
{
    /// <summary>Payments:CollectionEnabled. Con el cobro apagado nadie puede abrir un pago online; la configuracion igual se puede preparar.</summary>
    public bool CollectionEnabled => configuration.GetValue("Payments:CollectionEnabled", false);

    /// <summary>Payments:EnableFakeProvider. El proveedor falso (simulador) solo aparece con esto encendido y nunca en conexiones de produccion.</summary>
    public bool FakeProviderEnabled => configuration.GetValue("Payments:EnableFakeProvider", false);

    /// <summary>Payments:IntentMinutes. Cuanto tiempo tiene el propietario para pagar un cobro abierto (por defecto 30 minutos).</summary>
    public int IntentMinutes => Math.Clamp(configuration.GetValue("Payments:IntentMinutes", 30), 5, 24 * 60);

    /// <summary>Payments:CallbackBaseUrl o, si no esta, nada: la dirección publica de la API para armar el callback.</summary>
    public string? CallbackBaseUrl => configuration["Payments:CallbackBaseUrl"];
}

/// <summary>
/// Proveedor falso para desarrollar y probar el cobro completo sin Bancard ni dinero. Firma su aviso con la clave privada de la conexion
/// (HMAC-SHA256 del cuerpo, en la cabecera x-fake-signature): asi se ejercita el descifrado de secretos y la verificacion del aviso por el mismo
/// camino que usara un proveedor real. Se comporta como Tpago en lo que importa al orquestador: si no se le responde "exito" devuelve el dinero.
/// </summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public const string Code = "Fake";
    public const string Product = "Simulator";
    public const string SignatureHeader = "x-fake-signature";
    public const string SecretKey = "privateKey";

    public string ProviderCode => Code;
    public string ProductCode => Product;
    public ProviderCapabilities Capabilities { get; } = new(SignedCallback: true, RefundsWhenNotAcknowledged: true);

    public Task<ProviderSession> CreateSessionAsync(ProviderConnectionInfo connection, CreateSessionRequest request, CancellationToken ct)
    {
        var sessionId = $"FAKE-{request.AttemptNumber}";
        return Task.FromResult(new ProviderSession(sessionId, $"{request.CallbackUrl.Split("/api/", 2)[0]}/fake-checkout/{sessionId}", request.ExpiresAtUtc));
    }

    public CallbackParseResult ParseCallback(ProviderConnectionInfo connection, string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        if (!connection.Secrets.TryGetValue(SecretKey, out var key) || string.IsNullOrEmpty(key))
            return CallbackParseResult.Invalid("La conexión no tiene la clave para verificar el aviso.");

        if (!headers.TryGetValue(SignatureHeader, out var signature) || string.IsNullOrWhiteSpace(signature))
            return CallbackParseResult.Invalid("El aviso no trae firma.", signatureOk: false);

        if (!SignatureMatches(key, rawBody, signature))
            return CallbackParseResult.Invalid("La firma del aviso no es válida.", signatureOk: false);

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            var statusRaw = Text(root, "status") ?? string.Empty;
            var status = statusRaw.ToLowerInvariant() switch
            {
                "confirmed" => ProviderPaymentStatus.Confirmed,
                "rejected" => ProviderPaymentStatus.Rejected,
                "reversed" => ProviderPaymentStatus.Reversed,
                "pending" => ProviderPaymentStatus.Pending,
                _ => (ProviderPaymentStatus?)null
            };
            if (status is null) return CallbackParseResult.Invalid("El estado del aviso no es válido.", signatureOk: true);

            if (!root.TryGetProperty("amount", out var amountElement) || !amountElement.TryGetDecimal(out var amount))
                return CallbackParseResult.Invalid("El aviso no trae un importe válido.", signatureOk: true);

            var sessionId = Text(root, "sessionId");
            var transactionId = Text(root, "transactionId");
            DateTime? confirmedAt = DateTime.TryParse(Text(root, "dateTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

            // Se vuelve a armar el contenido con los campos conocidos: lo que se guarda en el registro no es el cuerpo crudo.
            var redacted = JsonSerializer.Serialize(new
            {
                sessionId,
                reference = Text(root, "reference"),
                status = statusRaw,
                amount,
                currency = Text(root, "currency"),
                transactionId,
                authorizationCode = Text(root, "authorizationCode"),
                dateTime = Text(root, "dateTime"),
                cardBrand = Text(root, "cardBrand"),
                cardLast4 = Text(root, "cardLast4")
            });

            return CallbackParseResult.Ok(new ProviderCallback(
                SessionId: sessionId,
                ProviderReference: Text(root, "reference"),
                Status: status.Value,
                Amount: amount,
                Currency: Text(root, "currency") ?? "PYG",
                TransactionId: transactionId,
                AuthorizationCode: Text(root, "authorizationCode"),
                ConfirmedAtUtc: confirmedAt,
                CardBrand: Text(root, "cardBrand"),
                CardLast4: Text(root, "cardLast4"),
                StatusRaw: statusRaw,
                DedupeKey: $"fake:{transactionId ?? sessionId}:{statusRaw.ToLowerInvariant()}",
                RedactedPayloadJson: redacted), signatureOk: true);
        }
        catch (JsonException)
        {
            return CallbackParseResult.Invalid("El cuerpo del aviso no se pudo leer.", signatureOk: true);
        }
    }

    /// <summary>Arma el aviso firmado de un pago (lo usa el simulador y las pruebas). Devuelve el cuerpo y la firma.</summary>
    public static (string Body, string Signature) BuildSignedCallback(
        string privateKey, string sessionId, string reference, string status, decimal amount, string currency = "PYG", string? transactionId = null)
    {
        transactionId ??= $"TK-{sessionId}";
        var body = JsonSerializer.Serialize(new
        {
            sessionId,
            reference,
            status,
            amount,
            currency,
            transactionId,
            authorizationCode = "AUTH-" + Math.Abs(transactionId.GetHashCode()) % 1_000_000,
            dateTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            cardBrand = "FAKE",
            cardLast4 = "0000"
        });
        return (body, Sign(privateKey, body));
    }

    public static string Sign(string privateKey, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(privateKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static bool SignatureMatches(string key, string body, string signature)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(key, body));
        var received = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString()
        : root.TryGetProperty(name, out var other) && other.ValueKind is JsonValueKind.Number ? other.GetRawText()
        : null;
}
