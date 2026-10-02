namespace Condo.Application.Models;

// ── Pago de la reserva (comprador) ────────────────────────────────────────────

// Lo que necesita el comprador para pagar. Los datos para transferir solo se devuelven mientras la reserva espera el pago.
public class MarketplacePaymentInfoDto
{
    public Guid ReservationId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    // Texto que carga el SuperAdmin por edificio (banco, titular, numero, alias). Vacio si el edificio no cargo nada.
    public string TransferInfo { get; set; } = string.Empty;
}

public class MarketplaceSubmitPaymentRequest
{
    // Ruta que devolvio /api/uploads (p. ej. /uploads/abc.png).
    public string ComprobanteUrl { get; set; } = string.Empty;
}

// ── Revision (personal del edificio) ──────────────────────────────────────────

public class MarketplaceReviewItemDto
{
    public Guid PaymentId { get; set; }
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    // Unidades del comprador en este edificio (p. ej. "101, 102").
    public string BuyerUnits { get; set; } = string.Empty;
    // Aviso solo para quien revisa: alguna unidad del comprador tiene pagos atrasados. No se informa a otros vecinos.
    public bool BuyerUnitOverdue { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Hours { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    // Total que tiene que figurar en el comprobante.
    public decimal ExpectedAmount { get; set; }
    public string ComprobanteUrl { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
    // Submitted | Approved | Rejected
    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    // La reserva ya terminó: ya no se puede aprobar (solo rechazar).
    public bool ReservationEnded { get; set; }
}

public class MarketplaceApproveRequest
{
    // Monto que el revisor vio en el comprobante: solo se aprueba si coincide con el total esperado.
    public decimal ReviewedAmount { get; set; }
}

public class MarketplaceRejectRequest
{
    public string Reason { get; set; } = string.Empty;
}
