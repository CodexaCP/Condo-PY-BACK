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

    public ApplicationUser? Owner { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
    public Company? Company { get; set; }
    public ICollection<OwnerPaymentUnit> Units { get; set; } = new List<OwnerPaymentUnit>();
}
