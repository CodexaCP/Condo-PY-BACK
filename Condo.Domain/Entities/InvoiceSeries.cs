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

    // Datos del establecimiento, obligatorios para timbrado preimpreso.
    public string DireccionEstablecimiento { get; set; } = string.Empty;
    public string ActividadEconomica { get; set; } = string.Empty;

    // Datos de la imprenta que confecciono los formularios preimpresos (opcional).
    public string? ImprentaNumeroHabilitacion { get; set; }
    public string? ImprentaRuc { get; set; }
    public string? ImprentaRazonSocial { get; set; }

    // Calibracion de posiciones para que la factura calce sobre el papel preimpreso de este timbrado
    // (cada imprenta puede entregarlo con un desvio distinto). JSON: {"campo": {"dx": 0, "dy": 0}, ...},
    // en puntos PDF. Sin datos, se dibuja en las posiciones base de siempre (mismo resultado que hoy).
    public string? FieldPositionsJson { get; set; }

    // Escaneo del papel preimpreso de este timbrado, usado de fondo en el editor de calibracion.
    public string? ReferenceScanUrl { get; set; }

    // El papel de este cliente ya trae su propio marco/lineas/casillas impresas por su imprenta (distinto
    // al preimpreso generico del sistema): con esto en true, la factura no dibuja ningun marco propio,
    // solo el texto encima, para no pisar el diseno que ya esta impreso en el papel.
    public bool HideFrame { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<CreditNote> CreditNotes { get; set; } = new List<CreditNote>();
}
