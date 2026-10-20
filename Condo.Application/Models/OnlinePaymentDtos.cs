namespace Condo.Application.Models;

// ─── Opciones del propietario ────────────────────────────────────────────────

/// <summary>Un comprobante (unidad + periodo) que el propietario puede incluir en un cobro online. Se paga del mas antiguo en adelante.</summary>
public class OnlinePaymentComprobanteDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal Total { get; set; }
    // Lo que se paga si se elige "hasta este comprobante": la suma de este y de todos los anteriores, redondeada al guarani.
    public decimal CumulativeTotal { get; set; }
}

public class OnlinePaymentConnectionOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    // A nombre de quien se cobra (el titular del comercio): el propietario lo ve antes de pagar.
    public string HolderName { get; set; } = string.Empty;
    public string HolderTaxId { get; set; } = string.Empty;
    // Cobro de prueba: no se aplica a la deuda real de este edificio.
    public bool IsSimulated { get; set; }
}

public class OnlinePaymentBuildingOptionDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public List<OnlinePaymentConnectionOptionDto> Connections { get; set; } = [];
    public List<OnlinePaymentComprobanteDto> Comprobantes { get; set; } = [];
    // Si no puede pagar ahora (un pago declarado pendiente, un cobro en curso), por que.
    public string? BlockedReason { get; set; }
    // Cobro online en curso del propietario en este edificio: la app lo retoma (por ejemplo si se cerró mientras pagaba en la pagina del proveedor).
    public Guid? OpenPaymentId { get; set; }
}

public class OnlinePaymentOptionsDto
{
    public bool CollectionEnabled { get; set; }
    public List<OnlinePaymentBuildingOptionDto> Buildings { get; set; } = [];
}

// ─── Crear y consultar un cobro ──────────────────────────────────────────────

public class CreateOnlinePaymentRequest
{
    public Guid BuildingId { get; set; }
    // Obligatoria solo si el edificio tiene mas de una conexion activa.
    public Guid? ConnectionId { get; set; }
    // Ultimo comprobante a pagar ("hasta este mes"): se incluyen todos los anteriores del edificio.
    public Guid ThroughUnitId { get; set; }
    public Guid ThroughExpensePeriodId { get; set; }
}

public class OnlinePaymentItemDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal Amount { get; set; }
}

public class OnlinePaymentEventDto
{
    public DateTime ReceivedAtUtc { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool? SignatureOk { get; set; }
    public string? Ip { get; set; }
}

public class OnlinePaymentDto
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerFullName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PYG";
    // Open, Paid, Closed, ReviewRequired, Reversed.
    public string Status { get; set; } = string.Empty;
    public string? CloseReason { get; set; }
    public string? ReviewReason { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string HolderTaxId { get; set; } = string.Empty;
    public bool IsSimulated { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Guid ConnectionId { get; set; }
    public string ConnectionName { get; set; } = string.Empty;
    // Proveedor de la conexión ("Fake" = simulador): la pantalla ofrece simular la respuesta solo para el simulador.
    public string ConnectionProviderCode { get; set; } = string.Empty;
    public long? AttemptNumber { get; set; }
    public string? AttemptStatus { get; set; }
    // Donde paga el propietario (redireccion). Nulo cuando el intento ya termino.
    public string? CheckoutUrl { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public Guid? OwnerPaymentId { get; set; }
    public string? OwnerPaymentReference { get; set; }

    public List<OnlinePaymentItemDto> Items { get; set; } = [];
    // Solo para quien puede ver el detalle tecnico (SuperAdmin y Administrador de empresa).
    public List<OnlinePaymentEventDto>? Events { get; set; }
}
