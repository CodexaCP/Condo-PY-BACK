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

    // Cliente de la factura, guardado al emitirla: un documento fiscal emitido no cambia aunque despues se edite o cambie el
    // propietario. En borrador estos campos van vacios y el cliente se toma del propietario actual de la unidad.
    public string? ClientName { get; set; }
    public string? ClientDocumentType { get; set; }
    public string? ClientDocument { get; set; }
    public string? ClientAddress { get; set; }
    public string? ClientEmail { get; set; }
    // true = el cliente se completo despues, con el propietario vigente al migrar, en facturas emitidas antes de guardar el cliente.
    public bool ClientReconstructed { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public Unit? Unit { get; set; }
    public Payment? Payment { get; set; }
    public OwnerPayment? OwnerPayment { get; set; }
    public InvoiceSeries? Series { get; set; }
    public Invoice? ReemplazadaPorInvoice { get; set; }
}
