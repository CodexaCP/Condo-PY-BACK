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
    public LateFeeFrequency? LateFeeFrequency { get; set; }
    public bool BlockOverdueAmenityReservations { get; set; } = false;

    // Null = no tocar la configuracion de modelos (formularios que no la editan); al crear, null = estandar.
    public bool? UseStandardTemplates { get; set; }
    public string? InvoiceTemplateUrl { get; set; }
    public string? InvoiceTemplateFileName { get; set; }
    public string? CreditNoteTemplateUrl { get; set; }
    public string? CreditNoteTemplateFileName { get; set; }
    public string? ReceiptTemplateUrl { get; set; }
    public string? ReceiptTemplateFileName { get; set; }
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
    public LateFeeFrequency? LateFeeFrequency { get; set; }
    public bool BlockOverdueAmenityReservations { get; set; }
    public bool UseStandardTemplates { get; set; }
    public string? InvoiceTemplateUrl { get; set; }
    public string? InvoiceTemplateFileName { get; set; }
    public string? CreditNoteTemplateUrl { get; set; }
    public string? CreditNoteTemplateFileName { get; set; }
    public string? ReceiptTemplateUrl { get; set; }
    public string? ReceiptTemplateFileName { get; set; }
}
