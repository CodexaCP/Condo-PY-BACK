namespace Condo.Application.Models;

// ── Cancelaciones, reembolsos, reclamos y aviso de inicio (fase 7) ────────────

public class MarketplaceCancelRequest
{
    // Obligatorio cuando cancela el propietario; opcional para el comprador.
    public string? Reason { get; set; }
}

// Lo que pasa si el usuario cancela, calculado por el servidor para avisarlo ANTES de confirmar.
public class MarketplaceCancelPreviewDto
{
    public Guid ReservationId { get; set; }
    // Buyer | Owner: desde que lado se cancela.
    public string Role { get; set; } = string.Empty;
    public bool CanCancel { get; set; }
    // Por que no se puede cancelar (cuando CanCancel es falso).
    public string? BlockedReason { get; set; }
    // Comprador: solo la base (la comision no se devuelve). Propietario: se le devuelve todo al comprador.
    public decimal RefundAmount { get; set; }
    // Comprador: comision que NO se le devuelve. Propietario: comision que asume (se le descuenta de su saldo a favor o queda
    // como deuda por gestion).
    public decimal CommissionAmount { get; set; }
    public bool RequiresReason { get; set; }
    // Reserva todavia sin pagar: se cancela sin comision ni reembolso.
    public bool BeforePayment { get; set; }
}

// ── Reembolsos (lista del Encargado) ──────────────────────────────────────────

public class MarketplaceRefundDto
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    // BuyerCancellation | OwnerCancellation | ClaimResolution
    public string Origin { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    // Pending | Returned
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    // Plazo maximo para devolver (72 horas desde que se creo).
    public DateTime DueAtUtc { get; set; }
    public bool Overdue { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public string? ReturnedByName { get; set; }
}

// Deuda por gestion de un propietario: visible para el Encargado mientras este pendiente.
public class MarketplaceOwnerDebtDto
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Remaining { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

// ── Reclamos ──────────────────────────────────────────────────────────────────

public class MarketplaceClaimRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class MarketplaceClaimResolveRequest
{
    // InFavorOfOwner | InFavorOfBuyer
    public string Outcome { get; set; } = string.Empty;
    // Explicacion visible para las dos partes.
    public string Note { get; set; } = string.Empty;
}

// Reclamo para el Encargado, con lo necesario para decidir (importes, partes y lo que respondio el comprador al aviso de inicio).
public class MarketplaceClaimDto
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public Guid BuildingId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerUnits { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal TotalAmount { get; set; }
    // Buyer | Owner
    public string OpenedBy { get; set; } = string.Empty;
    public string OpenedByName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    // Open | Resolved
    public string Status { get; set; } = string.Empty;
    // InFavorOfOwner | InFavorOfBuyer
    public string? Resolution { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    // Attending | NotUsing | nulo (sin respuesta: se asume que la uso).
    public string? BuyerStartResponse { get; set; }
    public string? BuyerStartResponseReason { get; set; }
}

// ── Aviso de inicio ───────────────────────────────────────────────────────────

public class MarketplaceStartResponseRequest
{
    // true = "Sí, voy"; false = "No la voy a usar" (el motivo es obligatorio).
    public bool Attending { get; set; }
    public string? Reason { get; set; }
}

// ── Reservas en mis publicaciones (el propietario ve quien reservo) ───────────

public class MarketplaceOwnerReservationDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid ListingId { get; set; }
    public Guid BuildingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    // Solo nombre y unidad de quien reservo: nada mas.
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerUnits { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Hours { get; set; }
    // Lo que va a recibir el propietario (sin comision).
    public decimal OwnerNetAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    // None | Pending | Held | Credited | Reversed
    public string CreditStatus { get; set; } = string.Empty;
    public DateTime? CreditedAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public bool CanCancel { get; set; }
    public bool CanReportProblem { get; set; }
    public string? ClaimStatus { get; set; }
    public string? ClaimResolution { get; set; }
    public string? ClaimResolutionNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
