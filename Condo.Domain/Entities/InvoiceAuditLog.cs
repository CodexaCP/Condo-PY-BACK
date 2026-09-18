using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class InvoiceAuditLog : CompanyScopedEntity
{
    public Guid? InvoiceId { get; set; }
    public Guid? InvoiceSeriesId { get; set; }
    public InvoiceAuditAction Action { get; set; }
    public Guid UserId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? DatosAntesJson { get; set; }
    public string? DatosDespuesJson { get; set; }
    public string? Detalle { get; set; }

    public Company? Company { get; set; }
    public Invoice? Invoice { get; set; }
    public InvoiceSeries? InvoiceSeries { get; set; }
}
