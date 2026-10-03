using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Condo.Api.Services;

/// <summary>Que tipo de pedido es, para decidir cuanto se limita.</summary>
public enum MarketplaceRateTier
{
    // No se limita (lecturas comunes y todo lo que no es del marketplace).
    None,
    // Escrituras comunes (publicar, editar, suspender, confirmar, marcar devuelto...).
    Write,
    // Acciones que mueven dinero u ocupan horarios (reservar, pagar, cancelar, reclamar...): mas estrictas.
    Sensitive,
    // Lecturas pesadas (PDF y Excel): generan documentos en el servidor.
    HeavyRead
}

/// <summary>
/// Limite de tasa de las escrituras del marketplace (fase 9). Cuenta por USUARIO (no por IP: detras del proxy todos comparten la
/// IP) y por tipo de accion, en una ventana de un minuto. Protege contra scripts que reservan o confirman en rafaga y contra la
/// generacion masiva de PDF. Un uso normal de la app no se acerca a estos topes.
/// </summary>
public static class MarketplaceRateLimiting
{
    public const string PathPrefix = "/api/marketplace";
    public const int WritesPerMinute = 60;
    public const int SensitivePerMinute = 12;
    public const int HeavyReadsPerMinute = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int SegmentsPerWindow = 6;

    public const string RejectedCode = "marketplace_rate_limited";
    public const string RejectedMessage = "Hiciste demasiadas acciones seguidas. Esperá un momento e intentá de nuevo.";

    /// <summary>Clasifica un pedido por metodo y ruta (la ruta ya sin el dominio).</summary>
    public static MarketplaceRateTier Classify(string method, PathString path)
    {
        if (!path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return MarketplaceRateTier.None;
        }

        var text = path.Value ?? string.Empty;
        var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

        if (isRead)
        {
            return text.EndsWith("/pdf", StringComparison.OrdinalIgnoreCase)
                   || text.EndsWith("/receipt-pdf", StringComparison.OrdinalIgnoreCase)
                   || text.EndsWith("/export", StringComparison.OrdinalIgnoreCase)
                ? MarketplaceRateTier.HeavyRead
                : MarketplaceRateTier.None;
        }

        string[] sensitive =
        [
            "/reservations", "/payment", "/cancel", "/owner-cancel", "/claim", "/start-response",
            "/approve", "/reject", "/return", "/resolve"
        ];
        return sensitive.Any(s => text.EndsWith(s, StringComparison.OrdinalIgnoreCase))
            ? MarketplaceRateTier.Sensitive
            : MarketplaceRateTier.Write;
    }

    /// <summary>Particion del pedido: una por usuario y tipo de accion (sin sesion, por IP).</summary>
    public static RateLimitPartition<string> GetPartition(HttpContext context)
    {
        var tier = Classify(context.Request.Method, context.Request.Path);
        if (tier == MarketplaceRateTier.None)
        {
            return RateLimitPartition.GetNoLimiter("none");
        }

        var who = context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } id
            ? $"user:{id}"
            : $"ip:{context.Connection.RemoteIpAddress}";
        var limit = tier switch
        {
            MarketplaceRateTier.Sensitive => SensitivePerMinute,
            MarketplaceRateTier.HeavyRead => HeavyReadsPerMinute,
            _ => WritesPerMinute
        };

        return RateLimitPartition.GetSlidingWindowLimiter($"{who}:{tier}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = Window,
            SegmentsPerWindow = SegmentsPerWindow,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    public static PartitionedRateLimiter<HttpContext> CreateLimiter() => PartitionedRateLimiter.Create<HttpContext, string>(GetPartition);

    /// <summary>Se llama desde Program.cs: registra el limitador y la respuesta 429 en el mismo formato de error del marketplace.</summary>
    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = CreateLimiter();
        options.OnRejected = async (context, token) =>
        {
            // La ventana deslizante no siempre informa cuanto falta: se avisa como minimo el tamano de un segmento (la ventana se libera de a poco).
            var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromSeconds(Window.TotalSeconds / SegmentsPerWindow);
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();

            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsJsonAsync(new { error = RejectedCode, message = RejectedMessage }, token);
        };
    }
}
