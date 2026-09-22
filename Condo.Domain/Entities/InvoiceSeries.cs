using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class InvoiceSeries : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public InvoiceSeriesDocumentType DocumentType { get; set; } = InvoiceSeriesDocumentType.Invoice;
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Establecimiento { get; set; } = string.Empty;
    public string PuntoExpedicion { get; set; } = string.Empty;
    public string NumeroTimbrado { get; set; } = string.Empty;
    public long RangoDesde { get; set; }
    public long RangoHasta { get; set; }
    public long CorrelativoActual { get; set; }
    public DateOnly VigenciaDesde { get; set; }
    public DateOnly VigenciaHasta { get; set; }
    public bool Activo { get; set; } = true;

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<CreditNote> CreditNotes { get; set; } = new List<CreditNote>();
}
