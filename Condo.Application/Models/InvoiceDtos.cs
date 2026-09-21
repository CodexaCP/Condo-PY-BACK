using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class InvoiceSeriesDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
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
}

public class CreateInvoiceSeriesRequest
{
    public Guid BuildingId { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Establecimiento { get; set; } = string.Empty;
    public string PuntoExpedicion { get; set; } = string.Empty;
    public string NumeroTimbrado { get; set; } = string.Empty;
    public long RangoDesde { get; set; }
    public long RangoHasta { get; set; }
    public DateOnly VigenciaDesde { get; set; }
    public DateOnly VigenciaHasta { get; set; }
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
