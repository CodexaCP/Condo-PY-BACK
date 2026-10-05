namespace Condo.Application.Models;

public class OwnerPaymentCreateRequest
{
    public DateOnly PaymentDate { get; set; }
    public string? ComprobanteUrl { get; set; }
    public decimal DeclaredAmount { get; set; }
    public List<Guid> UnitIds { get; set; } = [];
}

public class OwnerPaymentReviewRequest
{
    public decimal ReviewedAmount { get; set; }
}

public class OwnerPaymentRejectRequest
{
    public string RejectionReason { get; set; } = string.Empty;
}

public class OwnerPaymentDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerFullName { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public string ComprobanteUrl { get; set; } = string.Empty;
    public decimal DeclaredAmount { get; set; }
    public decimal? ReviewedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
    public string? ReviewedByUserFullName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Channel { get; set; } = "App";
    public string Method { get; set; } = "BankTransfer";
    public string ExternalReference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime? ReversedAt { get; set; }
    public List<OwnerPaymentUnitDto> Units { get; set; } = [];
    public List<OwnerPaymentApplicationDto> Applications { get; set; } = [];

    // Solo para el personal: false cuando el pago incluye unidades de edificios que el usuario no tiene
    // asignados (puede verlo por tener una unidad en su edificio, pero no revisarlo/aprobarlo/rechazarlo).
    public bool CanProcess { get; set; } = true;
}

public class OwnerPaymentApplicationDto
{
    public string UnitCode { get; set; } = string.Empty;
    public string Concept { get; set; } = string.Empty;
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal Amount { get; set; }
}

public class OwnerPaymentUnitDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public decimal AllocatedAmount { get; set; }
}

public class OwnerDebtUnitDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public decimal TotalDebt { get; set; }
    public List<OwnerDebtChargeDto> Charges { get; set; } = [];
}

public class OwnerCreditDto
{
    public decimal Amount { get; set; }
}

public class OwnerPaymentInvoiceDto
{
    public Guid Id { get; set; }
    public string? NumeroFormateado { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal MontoTotal { get; set; }
    public DateTime? FechaEmisionUtc { get; set; }
}

public class OwnerCreditMovementDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ApplyMode { get; set; }
    public decimal Amount { get; set; }
    public string? SourceReference { get; set; }
    public Guid? PaymentId { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>De donde viene cada parte del saldo a favor del propietario (lotes) y como se uso.</summary>
public class OwnerCreditBreakdownDto
{
    /// <summary>Saldo a favor vigente (el mismo que muestra la ficha).</summary>
    public decimal Amount { get; set; }
    /// <summary>Parte del saldo sin lote que la respalde: es anterior al historial de lotes.</summary>
    public decimal UntracedAmount { get; set; }
    /// <summary>Saldo retenido (la unidad quedo sin propietario principal): no cuenta en el saldo hasta que haya uno nuevo.</summary>
    public decimal HeldAmount { get; set; }
    public List<OwnerCreditLotDto> Lots { get; set; } = [];
    public List<OwnerCreditMovementDto> Uses { get; set; } = [];
}

public class OwnerCreditLotDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>OwnerPayment, Marketplace, CreditNote, SupplierCreditNote o Previous.</summary>
    public string Origin { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? BuildingName { get; set; }
    public string? UnitCode { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool OnHold { get; set; }
    public Guid? OwnerPaymentId { get; set; }
    public Guid? MarketplaceReservationId { get; set; }
}

public class OwnerDebtChargeDto
{
    public Guid ChargeId { get; set; }
    public string Concept { get; set; } = string.Empty;
    public string ChargeType { get; set; } = string.Empty;
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal Amount { get; set; }
    public decimal PendingAmount { get; set; }
}

public class ApplyCreditResultDto
{
    public decimal SettledAmount { get; set; }
    public decimal RemainingCredit { get; set; }
    public int ChargesSettled { get; set; }
}

// ─── Pago registrado por el sistema (canal Web) ──────────────────────────────

public class OwnerPaymentRegisterRequest
{
    public Guid OwnerId { get; set; }
    public DateOnly PaymentDate { get; set; }
    // Lo realmente recibido; el sistema lo valida contra los comprobantes completos (mas antiguo primero).
    public decimal Amount { get; set; }
    public Condo.Domain.Enums.PaymentMethod Method { get; set; } = Condo.Domain.Enums.PaymentMethod.BankTransfer;
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
}

public class OwnerPaymentReverseRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class RegisterPreviewDto
{
    public Guid OwnerId { get; set; }
    public string OwnerFullName { get; set; } = string.Empty;
    public decimal AvailableCredit { get; set; }
    // Pagos que el propietario envio desde la app y siguen sin resolver: bloquean el registro manual.
    public List<RegisterPreviewPendingPaymentDto> PendingOwnerPayments { get; set; } = [];
    public List<RegisterComprobanteDto> Comprobantes { get; set; } = [];
}

public class RegisterPreviewPendingPaymentDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal DeclaredAmount { get; set; }
}

public class RegisterComprobanteDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid ExpensePeriodId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal Total { get; set; }
    // Suma de este comprobante y todos los anteriores (del mas antiguo al mas nuevo).
    public decimal CumulativeTotal { get; set; }
    // Lo que hay que recibir para cubrir hasta este comprobante, ya descontado el saldo a favor.
    public decimal AmountToReceive { get; set; }
    // false: el edificio no esta en el alcance del usuario (no puede cubrirse este ni los siguientes).
    public bool InScope { get; set; } = true;
    public List<RegisterComprobanteLineDto> Lines { get; set; } = [];
}

public class RegisterComprobanteLineDto
{
    public string Concept { get; set; } = string.Empty;
    public decimal Pending { get; set; }
}
