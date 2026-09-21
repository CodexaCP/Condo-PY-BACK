using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Invoice : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public Guid UnitId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid? InvoiceSeriesId { get; set; }
    // Pago de propietario aprobado del que sale esta factura (una factura por unidad cubre todo lo aplicado a esa unidad).
    public Guid? OwnerPaymentId { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public long? Numero { get; set; }
    public string? NumeroFormateado { get; set; }

    public decimal MontoTotal { get; set; }
    public string DetalleSnapshotJson { get; set; } = "[]";

    public DateTime? FechaEmisionUtc { get; set; }
    public DateTime? FechaAnulacionUtc { get; set; }
    public string? MotivoAnulacion { get; set; }
    public Guid? ReemplazadaPorInvoiceId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public Unit? Unit { get; set; }
    public Payment? Payment { get; set; }
    public OwnerPayment? OwnerPayment { get; set; }
    public InvoiceSeries? Series { get; set; }
    public Invoice? ReemplazadaPorInvoice { get; set; }
}
