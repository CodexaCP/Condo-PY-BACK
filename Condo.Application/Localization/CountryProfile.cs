namespace Condo.Application.Localization;

/// <summary>Tipo de documento de identidad o fiscal que acepta un pais. El codigo es lo que se guarda en la ficha de la persona.</summary>
public sealed record CountryDocumentType(string Code, string Label, bool IsTaxId = false);

/// <summary>Lo que un pais habilita o no. Se muestra en la sesion para ocultar modulos y el servidor lo exige donde corresponde.</summary>
public sealed record CountryCapabilities(
    bool EmitsInvoices,
    bool OnlinePayments,
    bool PurchaseTaxBook,
    bool PaymentsInOtherCurrency);

/// <summary>
/// Perfil de un pais: valores iniciales que se copian a la empresa al crearla (moneda, impuesto, zona horaria) y datos de consulta que
/// no se copian (vocabulario, tipos de documento, jurisdicciones, capacidades). Un pais en borrador (<see cref="IsDraft"/>) se puede
/// elegir pero sus reglas no estan validadas con fuentes oficiales: la pantalla lo avisa.
/// </summary>
public sealed record CountryProfile(
    string Code,
    string Name,
    bool IsDraft,
    string DefaultCurrencyCode,
    string LocaleCode,
    string TimeZoneId,
    string PhonePrefix,
    string TaxName,
    decimal? TaxGeneralRate,
    decimal? TaxReducedRate,
    string TaxIdLabel,
    string RegionLabel,
    IReadOnlyList<string> Jurisdictions,
    IReadOnlyList<CountryDocumentType> DocumentTypes,
    CountryCapabilities Capabilities,
    IReadOnlyDictionary<string, string> Glossary,
    IReadOnlyDictionary<string, string> BuildingFieldLabels);
