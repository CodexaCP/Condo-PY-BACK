namespace Condo.Application.Models;

// ── Nota de cambio de propietario principal ───────────────────────────────────

// Estado ACTUAL de una reserva afectada por el cambio de propietario principal.
public class MarketplaceHandoverOperationDto
{
    public Guid ReservationId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    // None | Pending | Held | Credited | Reversed
    public string CreditStatus { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    // Lo que le corresponde al propietario con el que se creo la reserva.
    public decimal OwnerNetAmount { get; set; }
    public bool HasOpenClaim { get; set; }
    // Pending | Returned | nulo
    public string? RefundStatus { get; set; }
}

public class MarketplaceHandoverNoteDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string PreviousOwnerName { get; set; } = string.Empty;
    public string? NewOwnerName { get; set; }
    // PrimaryRemoved | PrimaryReplaced
    public string Trigger { get; set; } = string.Empty;
    // Lo que paso y la situacion al momento del cambio (texto fijo).
    public string Content { get; set; } = string.Empty;
    public int ReservationCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public string? ReadByName { get; set; }
    // Situacion actual de cada operacion afectada.
    public List<MarketplaceHandoverOperationDto> Operations { get; set; } = [];
}

// ── Historial economico de una operacion ──────────────────────────────────────

public class MarketplaceHistoryItemDto
{
    public DateTime TimestampUtc { get; set; }
    // Codigo de la accion registrada (p. ej. payment.approved).
    public string Action { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    // Nulo = lo hizo el sistema.
    public string? ActorName { get; set; }
    public decimal? Amount { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
}

// La operacion de punta a punta: publicacion -> reserva -> importes -> pago -> acreditacion, tal como quedo registrada.
public class MarketplaceOperationHistoryDto
{
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreditStatus { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Hours { get; set; }
    // Importes congelados al crear la reserva.
    public decimal HourlyPrice { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal OwnerNetAmount { get; set; }
    public List<MarketplaceHistoryItemDto> Items { get; set; } = [];
}
