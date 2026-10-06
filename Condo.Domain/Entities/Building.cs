using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Building : BaseEntity
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

    // Aportes de la liquidacion (ambos opcionales; vacio = no aplica): el de fondo de reserva es un % de los
    // gastos comunes y el extraordinario un % del sub total (gastos comunes + aporte de reserva).
    // Tratamiento de los ingresos del periodo al liquidar (por defecto se acreditan a los propietarios).
    public IncomeTreatment IncomeTreatment { get; set; } = IncomeTreatment.CreditToOwners;
    public decimal? ReserveFundPercentage { get; set; }
    public decimal? ExtraordinaryPercentage { get; set; }
    public LateFeeFrequency? LateFeeFrequency { get; set; }
    public bool BlockOverdueAmenityReservations { get; set; } = false;

    // Propietario marcado como presidente del consorcio. Unico por edificio; firma la liquidacion
    // de expensas antes de que el CompanyAdmin la publique.
    public Guid? PresidentUserId { get; set; }
    public DateTime? PresidentAssignedAtUtc { get; set; }
    public Guid? PresidentAssignedByUserId { get; set; }

    // Modo de facturacion del edificio. Solo lo configura el superadmin (creacion/edicion del edificio).
    public InvoicingMode InvoicingMode { get; set; } = InvoicingMode.Preimpresa;

    // Interruptor del modulo "Finanzas del edificio". Solo lo opera el SuperAdmin y solo si el plan vigente del
    // edificio incluye el modulo (Plan.IncludesFinanceModule). Apagarlo conserva los datos ya cargados.
    public bool FinanceModuleEnabled { get; set; }

    // Interruptor del "Marketplace de espacios temporales". Solo lo opera el SuperAdmin y solo si el plan vigente del
    // edificio lo incluye (Plan.IncludesMarketplace). Apagarlo conserva los datos ya cargados.
    public bool MarketplaceEnabled { get; set; }
    // Comision de gestion (porcentaje 0-100) que se suma al precio del propietario. La fija solo el SuperAdmin.
    public decimal MarketplaceCommissionPercent { get; set; } = 10m;
    // Datos para transferir (banco, titular, numero, alias). Se muestran solo en el pago de una reserva en curso.
    public string? MarketplaceTransferInfo { get; set; }

    // Interruptor del modulo "Publicidad" (banners en la app del propietario y del residente). Solo lo opera el SuperAdmin.
    // Apagado, el edificio no muestra anuncios ni puede recibir campanas; apagarlo conserva las campanas ya cargadas.
    public bool AdsEnabled { get; set; }
    // Segundos que se muestra cada banner antes de pasar al siguiente en la app (3 a 60). Lo fija el SuperAdmin por edificio.
    public int AdsRotationSeconds { get; set; } = 10;

    // Modelos de documentos (factura, nota de credito y liquidacion). Con UseStandardTemplates los PDF
    // salen con el diseno estandar de CONDOPY (colores de la marca); si no, el edificio adjunta sus
    // propios modelos, uno por concepto, y los PDF salen con el formato clasico preimpreso.
    public bool UseStandardTemplates { get; set; } = true;
    public string? InvoiceTemplateUrl { get; set; }
    public string? InvoiceTemplateFileName { get; set; }
    public string? CreditNoteTemplateUrl { get; set; }
    public string? CreditNoteTemplateFileName { get; set; }
    public string? SettlementTemplateUrl { get; set; }
    public string? SettlementTemplateFileName { get; set; }
    // Calibracion de la liquidacion sobre el modelo propio: JSON { campo: { dx, dy, fontSize } } (puntos PDF).
    public string? SettlementFieldPositionsJson { get; set; }
    // El papel del edificio ya trae su propio marco/lineas impresas: la liquidacion dibuja solo el texto.
    public bool SettlementHideFrame { get; set; } = true;

    // ── Ficha de registro del edificio (todo opcional salvo lo que ya era obligatorio) ──────────────────
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
    // Cantidad declarada de unidades (informativa: no reemplaza a las unidades realmente cargadas).
    public int? UnitsCount { get; set; }
    public string? LogoUrl { get; set; }
    // Telefonos guardados completos, con el prefijo del pais (ej. +595981123456).
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

    // Datos fiscales (Facturacion). Las series de timbrado nuevas toman de aca el RUC, la razon social,
    // la direccion y la actividad cuando no se los cargan a mano.
    public string? Ruc { get; set; }
    public string? LegalName { get; set; }
    public TaxpayerType? TaxpayerType { get; set; }
    public VatRegime? VatRegime { get; set; }
    public string? EconomicActivity { get; set; }
    public string? FiscalAddress { get; set; }
    public string? InvoiceEmail { get; set; }

    // Contabilidad y pagos: valores por defecto al crear un periodo de expensas (vacio = el sistema propone el dia 10).
    public int? DefaultDueDay { get; set; }
    public int? GraceDays { get; set; }
    public string? PaymentInstructions { get; set; }

    // Configuracion
    public string? TimeZoneId { get; set; }

    public Company? Company { get; set; }
    public Condominium? Condominium { get; set; }
    public ApplicationUser? PresidentUser { get; set; }
    public ICollection<BuildingBankAccount> BankAccounts { get; set; } = new List<BuildingBankAccount>();
    public ICollection<Unit> Units { get; set; } = new List<Unit>();
    public ICollection<BuildingExpense> BuildingExpenses { get; set; } = new List<BuildingExpense>();
    public ICollection<BuildingIncome> BuildingIncomes { get; set; } = new List<BuildingIncome>();
    public ICollection<ExpenseSettlement> ExpenseSettlements { get; set; } = new List<ExpenseSettlement>();
    public ICollection<ExpensePeriod> ExpensePeriods { get; set; } = new List<ExpensePeriod>();
    public ICollection<UserBuildingAccess> UserAccesses { get; set; } = new List<UserBuildingAccess>();
}
