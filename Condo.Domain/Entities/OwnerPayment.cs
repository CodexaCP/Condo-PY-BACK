using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class OwnerPayment : CompanyScopedEntity
{
    public Guid OwnerId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string ComprobanteUrl { get; set; } = string.Empty;
    public decimal DeclaredAmount { get; set; }
    public decimal? ReviewedAmount { get; set; }
    public OwnerPaymentStatus Status { get; set; } = OwnerPaymentStatus.Pending;
    public string Reference { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // App: lo declara el propietario y el personal lo revisa. Web: lo registra el personal ya cobrado (nace aprobado).
    public OwnerPaymentChannel Channel { get; set; } = OwnerPaymentChannel.App;
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    // Numero de transferencia / cheque que escribe el personal; la Reference del pago sigue siendo PAY-aaaa-n.
    public string ExternalReference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    // Solo pagos de canal Web revertidos (quedan en estado Rejected con el motivo en RejectionReason).
    public DateTime? ReversedAt { get; set; }

    public ApplicationUser? Owner { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
    public Company? Company { get; set; }
    public ICollection<OwnerPaymentUnit> Units { get; set; } = new List<OwnerPaymentUnit>();
}
