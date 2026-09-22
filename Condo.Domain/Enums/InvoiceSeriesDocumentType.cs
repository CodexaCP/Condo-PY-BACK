namespace Condo.Domain.Enums;

// Un mismo timbrado autoriza rangos de numeracion separados por tipo de comprobante (factura,
// nota de credito). Cada InvoiceSeries es UNA de esas series: misma Establecimiento/PuntoExpedicion/
// NumeroTimbrado puede repetirse en dos filas, una por cada DocumentType, cada una con su propio
// RangoDesde/RangoHasta/CorrelativoActual.
public enum InvoiceSeriesDocumentType
{
    Invoice = 1,
    CreditNote = 2
}
