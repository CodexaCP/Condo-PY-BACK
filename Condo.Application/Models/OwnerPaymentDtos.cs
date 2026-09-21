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
    public List<OwnerPaymentUnitDto> Units { get; set; } = [];
    public List<OwnerPaymentApplicationDto> Applications { get; set; } = [];
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
