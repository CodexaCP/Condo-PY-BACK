using Condo.Application.Abstractions;
using Condo.Application.Localization;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Condo.Api.Services;

/// <summary>Pais y moneda resueltos de una empresa. Sin empresa (o desconocida) es Paraguay y guaranies.</summary>
public sealed record CompanyLocale(CountryProfile Country, CurrencyInfo Currency, CompanyLocaleDto Dto)
{
    public static CompanyLocale Of(Company? company) =>
        new(CountryProfiles.FindOrDefault(company?.CountryCode),
            Currencies.FindOrDefault(company?.CurrencyCode ?? CountryProfiles.FindOrDefault(company?.CountryCode).DefaultCurrencyCode),
            CompanyLocaleDto.From(company));

    public static CompanyLocale Default { get; } = Of(null);
}

/// <summary>
/// Entrega la configuracion regional de una empresa con una cache corta. La editan pocas veces (alta de la empresa) y se lee en cada
/// peticion para fijar la moneda de los documentos, asi que no se consulta la base cada vez.
/// </summary>
public sealed class CompanyLocaleService(ICondoDbContext dbContext, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<CompanyLocale> GetAsync(Guid? companyId, CancellationToken ct)
    {
        if (companyId is null || companyId == Guid.Empty) return CompanyLocale.Default;

        if (cache.TryGetValue(Key(companyId.Value), out CompanyLocale? hit) && hit is not null) return hit;

        var company = await dbContext.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == companyId.Value, ct);
        var locale = CompanyLocale.Of(company);
        cache.Set(Key(companyId.Value), locale, Ttl);
        return locale;
    }

    public void Invalidate(Guid companyId) => cache.Remove(Key(companyId));

    private static string Key(Guid companyId) => $"company-locale:{companyId}";
}
