using Condo.Application.Localization;
using Condo.Domain.Entities;

namespace Condo.Application.Models;

public class CountryDocumentTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsTaxId { get; set; }
}

public class CountryCapabilitiesDto
{
    public bool EmitsInvoices { get; set; }
    public bool OnlinePayments { get; set; }
    public bool PurchaseTaxBook { get; set; }
    public bool PaymentsInOtherCurrency { get; set; }
}

/// <summary>Un pais del catalogo, con los valores iniciales que se copian a la empresa al crearla.</summary>
public class CountryProfileDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDraft { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string LocaleCode { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string PhonePrefix { get; set; } = string.Empty;
    public string TaxName { get; set; } = string.Empty;
    public decimal? TaxGeneralRate { get; set; }
    public decimal? TaxReducedRate { get; set; }
    public string TaxIdLabel { get; set; } = string.Empty;
    public string RegionLabel { get; set; } = string.Empty;
    public List<string> Jurisdictions { get; set; } = [];
    public List<CountryDocumentTypeDto> DocumentTypes { get; set; } = [];
    public CountryCapabilitiesDto Capabilities { get; set; } = new();
    public Dictionary<string, string> Glossary { get; set; } = [];
    public Dictionary<string, string> BuildingFieldLabels { get; set; } = [];

    public static CountryProfileDto From(CountryProfile p) => new()
    {
        Code = p.Code,
        Name = p.Name,
        IsDraft = p.IsDraft,
        CurrencyCode = p.DefaultCurrencyCode,
        LocaleCode = p.LocaleCode,
        TimeZoneId = p.TimeZoneId,
        PhonePrefix = p.PhonePrefix,
        TaxName = p.TaxName,
        TaxGeneralRate = p.TaxGeneralRate,
        TaxReducedRate = p.TaxReducedRate,
        TaxIdLabel = p.TaxIdLabel,
        RegionLabel = p.RegionLabel,
        Jurisdictions = p.Jurisdictions.ToList(),
        DocumentTypes = p.DocumentTypes.Select(x => new CountryDocumentTypeDto { Code = x.Code, Label = x.Label, IsTaxId = x.IsTaxId }).ToList(),
        Capabilities = new CountryCapabilitiesDto
        {
            EmitsInvoices = p.Capabilities.EmitsInvoices,
            OnlinePayments = p.Capabilities.OnlinePayments,
            PurchaseTaxBook = p.Capabilities.PurchaseTaxBook,
            PaymentsInOtherCurrency = p.Capabilities.PaymentsInOtherCurrency
        },
        Glossary = new Dictionary<string, string>(p.Glossary),
        BuildingFieldLabels = new Dictionary<string, string>(p.BuildingFieldLabels)
    };
}

/// <summary>
/// Configuracion regional de la empresa del usuario: pais, moneda (con su formato), impuesto, vocabulario y capacidades. La reciben el WEB y la
/// APP al iniciar sesion para formatear importes y ocultar lo que el pais no usa.
/// </summary>
public class CompanyLocaleDto
{
    public string CountryCode { get; set; } = "PY";
    public string CountryName { get; set; } = "Paraguay";
    public bool IsDraftCountry { get; set; }
    public string CurrencyCode { get; set; } = "PYG";
    public string CurrencySymbol { get; set; } = "Gs.";
    public int CurrencyDecimals { get; set; }
    public string GroupSeparator { get; set; } = ".";
    public string DecimalSeparator { get; set; } = ",";
    public string LocaleCode { get; set; } = "es-PY";
    public string TimeZoneId { get; set; } = "America/Asuncion";
    public string PhonePrefix { get; set; } = "+595";
    public string TaxName { get; set; } = "IVA";
    public decimal? TaxGeneralRate { get; set; }
    public decimal? TaxReducedRate { get; set; }
    public string TaxIdLabel { get; set; } = "RUC";
    public string RegionLabel { get; set; } = "Departamento";
    public List<string> Jurisdictions { get; set; } = [];
    public List<CountryDocumentTypeDto> DocumentTypes { get; set; } = [];
    public CountryCapabilitiesDto Capabilities { get; set; } = new();
    public Dictionary<string, string> Glossary { get; set; } = [];
    public Dictionary<string, string> BuildingFieldLabels { get; set; } = [];

    /// <summary>Configuracion de una empresa; sin empresa (o con datos incompletos) sale la de Paraguay, que es lo de siempre.</summary>
    public static CompanyLocaleDto From(Company? company)
    {
        var profile = CountryProfiles.FindOrDefault(company?.CountryCode);
        var currency = Currencies.FindOrDefault(company?.CurrencyCode ?? profile.DefaultCurrencyCode);
        var country = CountryProfileDto.From(profile);

        return new CompanyLocaleDto
        {
            CountryCode = profile.Code,
            CountryName = profile.Name,
            IsDraftCountry = profile.IsDraft,
            CurrencyCode = currency.Code,
            CurrencySymbol = currency.Symbol,
            CurrencyDecimals = currency.Decimals,
            GroupSeparator = currency.GroupSeparator,
            DecimalSeparator = currency.DecimalSeparator,
            LocaleCode = string.IsNullOrWhiteSpace(company?.LocaleCode) ? profile.LocaleCode : company.LocaleCode,
            TimeZoneId = profile.TimeZoneId,
            PhonePrefix = profile.PhonePrefix,
            TaxName = string.IsNullOrWhiteSpace(company?.TaxName) ? profile.TaxName : company.TaxName,
            TaxGeneralRate = company is null ? profile.TaxGeneralRate : company.TaxGeneralRate,
            TaxReducedRate = company is null ? profile.TaxReducedRate : company.TaxReducedRate,
            TaxIdLabel = profile.TaxIdLabel,
            RegionLabel = profile.RegionLabel,
            Jurisdictions = country.Jurisdictions,
            DocumentTypes = country.DocumentTypes,
            Capabilities = country.Capabilities,
            Glossary = country.Glossary,
            BuildingFieldLabels = country.BuildingFieldLabels
        };
    }
}
