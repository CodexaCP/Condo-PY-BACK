using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class BuildingUpsertRequest
{
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public decimal? LateFeeRatePercentage { get; set; }
    public decimal? ReserveFundPercentage { get; set; }
    public decimal? ExtraordinaryPercentage { get; set; }
    public LateFeeFrequency? LateFeeFrequency { get; set; }
    public bool BlockOverdueAmenityReservations { get; set; } = false;

    // Solo lo configura el SuperAdmin; obligatorio para el/en su formulario, se ignora para el resto.
    public InvoicingMode? InvoicingMode { get; set; }

    // Null = no tocar la configuracion de modelos (formularios que no la editan); al crear, null = estandar.
    public bool? UseStandardTemplates { get; set; }
    public string? InvoiceTemplateUrl { get; set; }
    public string? InvoiceTemplateFileName { get; set; }
    public string? CreditNoteTemplateUrl { get; set; }
    public string? CreditNoteTemplateFileName { get; set; }
    public string? SettlementTemplateUrl { get; set; }
    public string? SettlementTemplateFileName { get; set; }
}

public class BuildingDto
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? CondominiumId { get; set; }
    public string CondominiumName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public decimal? LateFeeRatePercentage { get; set; }
    public decimal? ReserveFundPercentage { get; set; }
    public decimal? ExtraordinaryPercentage { get; set; }
    public LateFeeFrequency? LateFeeFrequency { get; set; }
    public bool BlockOverdueAmenityReservations { get; set; }
    public InvoicingMode InvoicingMode { get; set; }
    public bool UseStandardTemplates { get; set; }
    public string? InvoiceTemplateUrl { get; set; }
    public string? InvoiceTemplateFileName { get; set; }
    public string? CreditNoteTemplateUrl { get; set; }
    public string? CreditNoteTemplateFileName { get; set; }
    public string? SettlementTemplateUrl { get; set; }
    public string? SettlementTemplateFileName { get; set; }
    public string? SettlementFieldPositionsJson { get; set; }
    public bool SettlementHideFrame { get; set; }
}


public class SettlementFieldOffsetDto
{
    public float Dx { get; set; }
    public float Dy { get; set; }
    public float? FontSize { get; set; }
    // Ancho del bloque (columnas y bloques de la liquidacion), alto de fila (solo la key "filas") y ocultar.
    public float? Width { get; set; }
    public float? RowHeight { get; set; }
    // null = usar el valor por defecto del bloque (algunos, como las columnas del cuerpo, arrancan ocultos)
    public bool? Hidden { get; set; }
}

public class UpdateSettlementCalibrationRequest
{
    public Dictionary<string, SettlementFieldOffsetDto> Positions { get; set; } = new();
    public bool HideFrame { get; set; } = true;
}
