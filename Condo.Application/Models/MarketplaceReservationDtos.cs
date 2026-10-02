namespace Condo.Application.Models;

// ── Explorar y reservar ───────────────────────────────────────────────────────

// Tramo ya ocupado de una publicacion (intervalo semiabierto [inicio, fin)).
public class MarketplaceIntervalDto
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
}

// Publicacion de otro vecino que se puede reservar: con la ventana, el precio y lo que ya esta ocupado.
public class MarketplaceExploreItemDto
{
    public Guid ListingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public decimal HourlyPrice { get; set; }
    // Comision de gestion del edificio (se muestra al comprador en el desglose).
    public decimal CommissionPercent { get; set; }
    public List<MarketplaceIntervalDto> Occupied { get; set; } = [];
}

public class MarketplaceQuoteRequest
{
    public Guid ListingId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
}

// Desglose para el comprador. El servidor lo calcula siempre: el precio nunca viene del cliente.
public class MarketplaceQuoteDto
{
    public Guid ListingId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Hours { get; set; }
    public decimal HourlyPrice { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class MarketplaceReservationDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid ListingId { get; set; }
    public Guid BuildingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Hours { get; set; }
    public decimal HourlyPrice { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal TotalAmount { get; set; }
    // PendingPayment | InReview | Confirmed | Completed | Cancelled | Expired | Rejected
    public string Status { get; set; } = string.Empty;
    // Solo mientras espera el pago: hasta cuando puede pagar.
    public DateTime? ExpiresAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
