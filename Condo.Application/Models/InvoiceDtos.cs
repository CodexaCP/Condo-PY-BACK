using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class InvoiceSeriesDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public InvoiceSeriesDocumentType DocumentType { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Establecimiento { get; set; } = string.Empty;
    public string PuntoExpedicion { get; set; } = string.Empty;
    public string NumeroTimbrado { get; set; } = string.Empty;
    public long RangoDesde { get; set; }
    public long RangoHasta { get; set; }
    public long CorrelativoActual { get; set; }
    public long NumerosDisponibles { get; set; }
    public DateOnly VigenciaDesde { get; set; }
    public DateOnly VigenciaHasta { get; set; }
    public bool Activo { get; set; }
    public bool ProximoAAgotarse { get; set; }
    public bool ProximoAVencer { get; set; }
    public string DireccionEstablecimiento { get; set; } = string.Empty;
    public string ActividadEconomica { get; set; } = string.Empty;
    public string? ImprentaNumeroHabilitacion { get; set; }
    public string? ImprentaRuc { get; set; }
    public string? ImprentaRazonSocial { get; set; }
    public string? FieldPositionsJson { get; set; }
    public string? ReferenceScanUrl { get; set; }
}

public class FieldOffsetDto
{
    public float Dx { get; set; }
    public float Dy { get; set; }
}

public class UpdateInvoiceSeriesCalibrationRequest
{
    public Dictionary<string, FieldOffsetDto> Positions { get; set; } = new();
    public string? ReferenceScanUrl { get; set; }
}

public class CreateInvoiceSeriesRequest
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
    public long ProximoNumero { get; set; }
    public DateOnly VigenciaDesde { get; set; }
    public DateOnly VigenciaHasta { get; set; }
    public string DireccionEstablecimiento { get; set; } = string.Empty;
    public string ActividadEconomica { get; set; } = string.Empty;
    public string? ImprentaNumeroHabilitacion { get; set; }
    public string? ImprentaRuc { get; set; }
    public string? ImprentaRazonSocial { get; set; }
}

public class InvoiceLineDto
{
    public string Concepto { get; set; } = string.Empty;
    public ExpenseChargeType? ChargeType { get; set; }
    public decimal Monto { get; set; }
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid PaymentId { get; set; }
    public Guid? InvoiceSeriesId { get; set; }
    public string? SeriesRazonSocial { get; set; }
    public string? SeriesRuc { get; set; }
    public string? SeriesNumeroTimbrado { get; set; }
    public string? SeriesEstablecimiento { get; set; }
    public string? SeriesPuntoExpedicion { get; set; }
    public DateOnly? SeriesVigenciaDesde { get; set; }
    public DateOnly? SeriesVigenciaHasta { get; set; }
    public string? BuildingAddress { get; set; }
    public string? BuildingPhone { get; set; }
    public DateOnly? PeriodDueDate { get; set; }
    public int? PeriodYear { get; set; }
    public int? PeriodMonth { get; set; }
    public decimal UnitCoefficient { get; set; }
    // Suma de cargos Ordinary de TODAS las unidades del edificio en el periodo (presupuesto sobre el que
    // se aplica el coeficiente). Se calcula aparte porque no depende de esta factura sino del periodo entero.
    public decimal BuildingOrdinaryTotal { get; set; }
    public string? FieldPositionsJson { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ClienteDocumento { get; set; }
    public InvoiceStatus Status { get; set; }
    public long? Numero { get; set; }
    public string? NumeroFormateado { get; set; }
    public decimal MontoTotal { get; set; }
    public List<InvoiceLineDto> Detalle { get; set; } = new();
    public DateTime? FechaEmisionUtc { get; set; }
    public DateTime? FechaAnulacionUtc { get; set; }
    public string? MotivoAnulacion { get; set; }
    public Guid? ReemplazadaPorInvoiceId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// ─── Consulta / trazabilidad de facturas ────────────────────────────────────

public class InvoiceLedgerQuery
{
    public Guid? BuildingId { get; set; }
    public Guid? UnitId { get; set; }
    public InvoiceStatus? Status { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    public Guid? OwnerPaymentId { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class InvoiceLedgerRowDto
{
    // Factura
    public Guid Id { get; set; }
    public InvoiceStatus Status { get; set; }
    public long? Numero { get; set; }
    public string? NumeroFormateado { get; set; }
    public decimal MontoTotal { get; set; }
    public DateTime? FechaEmisionUtc { get; set; }
    public DateTime? FechaAnulacionUtc { get; set; }
    public string? MotivoAnulacion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int LineCount { get; set; }
    public decimal MoraTotal { get; set; }

    // Emisor / timbrado
    public string? SeriesRazonSocial { get; set; }
    public string? SeriesRuc { get; set; }
    public string? SeriesNumeroTimbrado { get; set; }
    public string? SeriesEstablecimiento { get; set; }
    public string? SeriesPuntoExpedicion { get; set; }

    // Edificio, unidad y cliente
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string? ClienteNombre { get; set; }
    public string? ClienteDocumento { get; set; }

    // Comprobante (unidad + periodo) y liquidacion
    public Guid ExpensePeriodId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string PeriodName { get; set; } = string.Empty;
    public string PeriodStatus { get; set; } = string.Empty;
    public DateOnly PeriodDueDate { get; set; }
    public decimal ComprobanteTotal { get; set; }
    public string? LiquidationStatus { get; set; }
    public DateTime? LiquidationApprovedAtUtc { get; set; }
    public string? LiquidationApprovedBy { get; set; }
    public DateTime? LiquidationPublishedAtUtc { get; set; }
    public string? LiquidationPublishedBy { get; set; }

    // Pago
    public Guid PaymentId { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public decimal PaymentAmount { get; set; }
    public Guid? OwnerPaymentId { get; set; }
    public string? OwnerPaymentReference { get; set; }
    public string? OwnerPaymentStatus { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerPaymentReviewedBy { get; set; }
    public DateTime? OwnerPaymentResolvedAtUtc { get; set; }
}

public class InvoiceLedgerSummaryDto
{
    public int DraftCount { get; set; }
    public int IssuedCount { get; set; }
    public int VoidedCount { get; set; }
    public decimal DraftAmount { get; set; }
    public decimal IssuedAmount { get; set; }
    public decimal VoidedAmount { get; set; }
}

public class InvoiceLedgerDto
{
    public List<InvoiceLedgerRowDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public InvoiceLedgerSummaryDto Summary { get; set; } = new();
}

public class CreateInvoiceDraftRequest
{
    public Guid PaymentId { get; set; }
}

public class CreateOwnerPaymentInvoiceDraftsRequest
{
    public Guid OwnerPaymentId { get; set; }
}

public class EmitInvoiceRequest
{
    public Guid InvoiceSeriesId { get; set; }
}

public class VoidInvoiceRequest
{
    public string Motivo { get; set; } = string.Empty;
}
