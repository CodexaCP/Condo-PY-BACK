using Condo.Application.Abstractions;
using Condo.Application.Localization;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Fija la moneda del contexto a la de una empresa concreta. El middleware ya lo hace con la empresa del usuario; esto es para el
/// SuperAdmin (sin empresa) y para los procesos de fondo, que generan documentos de una empresa que no es la de la sesion.
/// </summary>
public static class MoneyScope
{
    public static async Task<IDisposable> ForCompanyAsync(ICondoDbContext dbContext, Guid companyId, CancellationToken ct)
    {
        var code = await dbContext.Companies.AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => x.CurrencyCode)
            .FirstOrDefaultAsync(ct);
        return Money.Use(Currencies.FindOrDefault(code));
    }
}
