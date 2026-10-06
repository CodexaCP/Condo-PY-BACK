using Condo.Domain.Enums;

namespace Condo.Application.Models;

// Ficha de registro del edificio: lo que se guarda y se devuelve igual en el formulario (alta y edicion) y en el detalle.
// Todo es opcional; el formulario lo agrupa en Generales, Legal y registral, Facturacion, Contabilidad y pagos, Configuracion.
public abstract class BuildingProfileData
{
    // Datos generales
    public BuildingPropertyType? PropertyType { get; set; }
    public string? Department { get; set; }
    public string? City { get; set; }
    public string? Neighborhood { get; set; }
    public string? LocationReference { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? YearBuilt { get; set; }
    public int? TowersCount { get; set; }
    public int? FloorsCount { get; set; }
    public int? UnitsCount { get; set; }
    public string? LogoUrl { get; set; }
    public string? WhatsAppPhone { get; set; }
    public string? OfficeHours { get; set; }

    // Legal y registral
    public string? FincaNumber { get; set; }
    public string? PadronNumber { get; set; }
    public string? CadastralAccount { get; set; }
    public string? LegalEntityNumber { get; set; }
    public DateOnly? LegalEntityDate { get; set; }
    public string? BylawsUrl { get; set; }
    public string? BylawsFileName { get; set; }
    public string? AdministratorName { get; set; }
    public string? AdministratorPhone { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    // Facturacion (datos fiscales)
    public string? Ruc { get; set; }
    public string? LegalName { get; set; }
    public TaxpayerType? TaxpayerType { get; set; }
    public VatRegime? VatRegime { get; set; }
    public string? EconomicActivity { get; set; }
    public string? FiscalAddress { get; set; }
    public string? InvoiceEmail { get; set; }

    // Contabilidad y pagos
    public int? DefaultDueDay { get; set; }
    public int? GraceDays { get; set; }
    public string? PaymentInstructions { get; set; }

    // Configuracion
    public string? TimeZoneId { get; set; }
}

public class BuildingBankAccountDto
{
    // Vacio/null = cuenta nueva; con Id = se actualiza la existente.
    public Guid? Id { get; set; }
    public string BankName { get; set; } = string.Empty;
    public BankAccountType AccountType { get; set; } = BankAccountType.Checking;
    public string AccountNumber { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public string? HolderDocument { get; set; }
    public string? Alias { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BuildingUpsertRequest : BuildingProfileData
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
    public IncomeTreatment? IncomeTreatment { get; set; }
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

    // Cuentas bancarias de cobro. Null = no tocar (formularios que no las editan); una lista reemplaza a las guardadas.
    public List<BuildingBankAccountDto>? BankAccounts { get; set; }
}

public class BuildingDto : BuildingProfileData
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
    public IncomeTreatment IncomeTreatment { get; set; }
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

    // Modulos contratados (solo lectura: los opera el SuperAdmin desde su propia pantalla).
    public bool FinanceModuleEnabled { get; set; }
    public bool MarketplaceEnabled { get; set; }
    public bool AdsEnabled { get; set; }

    // Solo en el detalle (GET por id y respuestas de guardado); en el listado va vacio.
    public List<BuildingBankAccountDto> BankAccounts { get; set; } = new();
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
