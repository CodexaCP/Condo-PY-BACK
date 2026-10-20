using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Intento de cobro: una sesion abierta con el proveedor de pagos para un cobro. Un reintento es un intento nuevo, pedido por el usuario y solo
// cuando el anterior terminó. Los identificadores que mandamos al proveedor (ProviderReference) salen de una secuencia global unica.
public class PaymentAttempt : CompanyScopedEntity
{
    public Guid IntentId { get; set; }
    public Guid ConnectionId { get; set; }

    // Secuencia global "maximo + 1" con indice unico.
    public long AttemptNumber { get; set; }

    // Lo que le mandamos al proveedor para reconocer el cobro (shop_process_id, reference_id, id_pedido_comercio). Unico por conexion.
    public string ProviderReference { get; set; } = string.Empty;
    // Identificador de la sesion en el proveedor (link_alias, process_id). Con el se encuentra el intento cuando llega el aviso.
    public string? ProviderSessionId { get; set; }
    public string? CheckoutUrl { get; set; }
    // Lo que el proveedor devuelve al confirmar (ticket_number, authorization_code): para auditoria y conciliacion.
    public string? ProviderTransactionId { get; set; }
    public string? AuthorizationCode { get; set; }

    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Created;
    // Estado tal como lo informo el proveedor, sin traducir.
    public string? ProviderStatusRaw { get; set; }

    public decimal Amount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    // Hora de la confirmacion segun el proveedor.
    public DateTime? ConfirmedAtUtc { get; set; }

    // Solo marca y ultimos 4 digitos: nunca el BIN completo ni otro dato de la tarjeta.
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    // Reservados: quien asume la comision y su tratamiento fiscal son decisiones pendientes de cada cliente. Sin logica que los use todavia.
    public decimal? CommissionAmount { get; set; }
    public decimal? NetAmount { get; set; }

    // Conciliacion contra el portal del proveedor y el banco (no es un estado del pago).
    public DateTime? ReconciledAtUtc { get; set; }
    public Guid? ReconciledByUserId { get; set; }
    public string? SettlementReference { get; set; }

    public Company? Company { get; set; }
    public PaymentIntent? Intent { get; set; }
    public PaymentConnection? Connection { get; set; }
}
