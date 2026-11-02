using Condo.Application.Abstractions;
using Condo.Application.Localization;

namespace Condo.Api.Services;

/// <summary>
/// Fija la moneda del contexto (<see cref="Money"/>) segun la empresa del usuario, para que PDF, Excel, correos y avisos de esa peticion
/// escriban los importes en su moneda. El SuperAdmin no tiene empresa: sigue en guaranies salvo que el codigo fije otra con <c>Money.Use</c>.
/// </summary>
public sealed class CompanyLocaleMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, CompanyLocaleService localeService)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || !tenantContext.IsAuthenticated || tenantContext.CompanyId is null)
        {
            await next(context);
            return;
        }

        var locale = await localeService.GetAsync(tenantContext.CompanyId, context.RequestAborted);
        using (Money.Use(locale.Currency))
        {
            await next(context);
        }
    }
}
