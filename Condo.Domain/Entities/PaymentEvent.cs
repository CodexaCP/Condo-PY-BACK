using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Registro de auditoria de los cobros online: cada aviso recibido del proveedor, cada pedido que le hicimos y cada decision propia (vencer,
// pasar a revision). Solo se inserta: nunca se modifica ni se borra un evento ya registrado. El contenido guardado esta redactado: nunca
// lleva datos de tarjeta ni claves.
public class PaymentEvent : CompanyScopedEntity
{
    public Guid ConnectionId { get; set; }
    public Guid? AttemptId { get; set; }

    public PaymentEventDirection Direction { get; set; }
    // Que fue: Create, Callback, Query, Reverse, Expire, Review...
    public string Kind { get; set; } = string.Empty;

    // Clave de deduplicacion (proveedor + id de transaccion + estado): un mismo aviso repetido no se procesa dos veces. Unica cuando existe.
    public string? DedupeKey { get; set; }

    // Verdadero/falso si el aviso traia firma y se verifico; nulo cuando el proveedor no firma (Tpago).
    public bool? SignatureOk { get; set; }
    public string? Ip { get; set; }

    public string? PayloadJson { get; set; }

    // Resultado de procesarlo: Applied, Duplicate, Rejected, Ignored, Failed...
    public string Outcome { get; set; } = string.Empty;
    public string? Error { get; set; }

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }

    public Company? Company { get; set; }
    public PaymentConnection? Connection { get; set; }
    public PaymentAttempt? Attempt { get; set; }
}
