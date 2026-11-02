namespace Condo.Application.Models;

public class CompanyUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    // Pais de la empresa. Vacio = Paraguay. La moneda y el impuesto se toman del pais cuando no se mandan.
    public string? CountryCode { get; set; }
    public string? CurrencyCode { get; set; }
    public string? TaxName { get; set; }
    public decimal? TaxGeneralRate { get; set; }
    public decimal? TaxReducedRate { get; set; }
    // true = el SuperAdmin fija el impuesto a mano (aunque venga vacio, p. ej. un pais sin tasa reducida); false = completar desde el pais.
    public bool TaxConfigured { get; set; }
}

public class CompanyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public string? ContactPhonePrefix { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string CountryCode { get; set; } = "PY";
    public string CountryName { get; set; } = "Paraguay";
    public bool IsDraftCountry { get; set; }
    public string CurrencyCode { get; set; } = "PYG";
    public string LocaleCode { get; set; } = "es-PY";
    public string TaxName { get; set; } = "IVA";
    public decimal? TaxGeneralRate { get; set; }
    public decimal? TaxReducedRate { get; set; }
}
