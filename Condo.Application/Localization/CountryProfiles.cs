namespace Condo.Application.Localization;

/// <summary>
/// Catalogo de paises. Paraguay reproduce lo que el sistema hacia antes de ser multipais. Argentina sale de las respuestas de la Fase 0
/// (docs/VALIDACIONES_MULTIPAIS_AR_VE_RESPONDIDO.md, Codigo Civil y Comercial arts. 2037-2072). Venezuela es un borrador: no hay fuente oficial
/// verificada, asi que no trae reglas fiscales ni tasa de impuesto y queda marcada <c>IsDraft</c>.
/// </summary>
public static class CountryProfiles
{
    public const string Paraguay = "PY";
    public const string Argentina = "AR";
    public const string Venezuela = "VE";

    /// <summary>Jurisdiccion con reglas propias de administradores (Ley 941 de la Ciudad Autonoma de Buenos Aires).</summary>
    public const string CabaJurisdiction = "Ciudad Autónoma de Buenos Aires";

    private static readonly CountryProfile Py = new(
        Code: Paraguay,
        Name: "Paraguay",
        IsDraft: false,
        DefaultCurrencyCode: "PYG",
        LocaleCode: "es-PY",
        TimeZoneId: "America/Asuncion",
        PhonePrefix: "+595",
        TaxName: "IVA",
        TaxGeneralRate: 10m,
        TaxReducedRate: 5m,
        TaxIdLabel: "RUC",
        RegionLabel: "Departamento",
        Jurisdictions: [],
        DocumentTypes:
        [
            new("RUC", "RUC", IsTaxId: true),
            new("CedulaParaguaya", "Cédula paraguaya"),
            new("Pasaporte", "Pasaporte"),
            new("Otro", "Otro")
        ],
        Capabilities: new(EmitsInvoices: true, OnlinePayments: true, PurchaseTaxBook: true, PaymentsInOtherCurrency: false),
        Glossary: new Dictionary<string, string>
        {
            ["expensas"] = "Expensas",
            ["liquidacion"] = "Liquidación de expensas",
            ["coeficiente"] = "Coeficiente",
            ["consorcio"] = "Consorcio",
            ["administradora"] = "Administradora",
            ["unidad"] = "Unidad",
            ["propietario"] = "Propietario",
            ["residente"] = "Residente",
            ["encargado"] = "Encargado de edificio",
            ["portero"] = "Portero",
            ["presidente"] = "Presidente del consorcio",
            ["fondoReserva"] = "Fondo de reserva",
            ["mora"] = "Mora",
            ["recibo"] = "Recibo",
            ["asamblea"] = "Asamblea"
        },
        BuildingFieldLabels: new Dictionary<string, string>
        {
            ["region"] = "Departamento",
            ["finca"] = "Finca",
            ["padron"] = "Padrón",
            ["cadastral"] = "Cuenta corriente catastral",
            ["legalEntity"] = "Personería jurídica",
            ["taxId"] = "RUC"
        });

    private static readonly CountryProfile Ar = new(
        Code: Argentina,
        Name: "Argentina",
        IsDraft: false,
        DefaultCurrencyCode: "ARS",
        LocaleCode: "es-AR",
        TimeZoneId: "America/Argentina/Buenos_Aires",
        PhonePrefix: "+54",
        TaxName: "IVA",
        // La alicuota general es 21 % (Ley 23.349, art. 28). La reducida depende de la operacion y no se fija por defecto.
        TaxGeneralRate: 21m,
        TaxReducedRate: null,
        TaxIdLabel: "CUIT",
        RegionLabel: "Provincia",
        Jurisdictions:
        [
            CabaJurisdiction, "Buenos Aires", "Catamarca", "Chaco", "Chubut", "Córdoba", "Corrientes", "Entre Ríos", "Formosa",
            "Jujuy", "La Pampa", "La Rioja", "Mendoza", "Misiones", "Neuquén", "Río Negro", "Salta", "San Juan", "San Luis",
            "Santa Cruz", "Santa Fe", "Santiago del Estero", "Tierra del Fuego", "Tucumán"
        ],
        DocumentTypes:
        [
            new("DNI", "DNI"),
            new("CUIT", "CUIT", IsTaxId: true),
            new("CUIL", "CUIL", IsTaxId: true),
            new("Pasaporte", "Pasaporte"),
            new("Otro", "Otro")
        ],
        // El consorcio no emite factura por la expensa (liquidacion, recibo y comprobante fiscal son documentos distintos): hasta que un
        // contador local lo decida, la facturacion y el libro de compras quedan apagados. Los cobros son por transferencia y comprobante.
        Capabilities: new(EmitsInvoices: false, OnlinePayments: false, PurchaseTaxBook: false, PaymentsInOtherCurrency: false),
        Glossary: new Dictionary<string, string>
        {
            ["expensas"] = "Expensas",
            ["liquidacion"] = "Liquidación de expensas",
            ["coeficiente"] = "Porcentual",
            ["consorcio"] = "Consorcio",
            ["administradora"] = "Administradora",
            ["unidad"] = "Unidad funcional",
            ["propietario"] = "Propietario",
            ["residente"] = "Residente",
            // En Argentina «encargado» es el portero (personal del edificio): el rol que supervisa el edificio se llama «Responsable».
            ["encargado"] = "Responsable de edificio",
            ["portero"] = "Encargado / portero",
            ["presidente"] = "Consejo de propietarios",
            ["fondoReserva"] = "Fondo de reserva",
            ["mora"] = "Intereses por mora",
            ["recibo"] = "Recibo",
            ["asamblea"] = "Asamblea"
        },
        BuildingFieldLabels: new Dictionary<string, string>
        {
            ["region"] = "Provincia",
            ["finca"] = "Matrícula",
            ["padron"] = "Partida",
            ["cadastral"] = "Nomenclatura catastral",
            ["legalEntity"] = "Reglamento inscripto (N.º)",
            ["taxId"] = "CUIT"
        });

    private static readonly CountryProfile Ve = new(
        Code: Venezuela,
        Name: "Venezuela",
        IsDraft: true,
        DefaultCurrencyCode: "VES",
        LocaleCode: "es-VE",
        TimeZoneId: "America/Caracas",
        PhonePrefix: "+58",
        TaxName: "IVA",
        TaxGeneralRate: null,
        TaxReducedRate: null,
        TaxIdLabel: "RIF",
        RegionLabel: "Estado",
        Jurisdictions: [],
        DocumentTypes:
        [
            new("CedulaVE", "Cédula (V/E)"),
            new("RIF", "RIF", IsTaxId: true),
            new("Pasaporte", "Pasaporte"),
            new("Otro", "Otro")
        ],
        Capabilities: new(EmitsInvoices: false, OnlinePayments: false, PurchaseTaxBook: false, PaymentsInOtherCurrency: true),
        Glossary: new Dictionary<string, string>
        {
            ["expensas"] = "Gastos comunes",
            ["liquidacion"] = "Recibo de condominio",
            ["coeficiente"] = "Alícuota",
            ["consorcio"] = "Condominio",
            ["administradora"] = "Administradora",
            ["unidad"] = "Inmueble",
            ["propietario"] = "Propietario",
            ["residente"] = "Residente",
            ["encargado"] = "Encargado",
            ["portero"] = "Conserje",
            ["presidente"] = "Junta de Condominio",
            ["fondoReserva"] = "Fondo de reserva",
            ["mora"] = "Intereses por mora",
            ["recibo"] = "Recibo",
            ["asamblea"] = "Asamblea"
        },
        BuildingFieldLabels: new Dictionary<string, string>
        {
            ["region"] = "Estado",
            ["finca"] = "Documento de condominio",
            ["padron"] = "Registro inmobiliario",
            ["cadastral"] = "Código catastral",
            ["legalEntity"] = "Documento registrado (N.º)",
            ["taxId"] = "RIF"
        });

    public static IReadOnlyList<CountryProfile> All { get; } = [Py, Ar, Ve];

    public static CountryProfile Default => Py;

    public static CountryProfile? Find(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : All.FirstOrDefault(x => string.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    public static CountryProfile FindOrDefault(string? code) => Find(code) ?? Default;
}
