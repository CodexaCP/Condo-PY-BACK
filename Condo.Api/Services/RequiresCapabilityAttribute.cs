using Condo.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Condo.Api.Services;

/// <summary>
/// Exige que el pais de la empresa del usuario tenga activada una capacidad (por ejemplo emitir facturas o cobrar online). Se evalua con la
/// empresa de la sesion: el SuperAdmin, que no tiene empresa, no se bloquea. Capacidades: EmitsInvoices, OnlinePayments, PurchaseTaxBook.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresCapabilityAttribute(string capability) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenant.IsAuthenticated || tenant.CompanyId is null)
        {
            await next();
            return;
        }

        var locales = context.HttpContext.RequestServices.GetRequiredService<CompanyLocaleService>();
        var locale = await locales.GetAsync(tenant.CompanyId, context.HttpContext.RequestAborted);
        var caps = locale.Country.Capabilities;
        var enabled = capability switch
        {
            "EmitsInvoices" => caps.EmitsInvoices,
            "OnlinePayments" => caps.OnlinePayments,
            "PurchaseTaxBook" => caps.PurchaseTaxBook,
            _ => true
        };

        if (enabled)
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(new
        {
            code = "capability_disabled",
            message = $"Esta función no está habilitada para {locale.Country.Name}."
        })
        { StatusCode = StatusCodes.Status400BadRequest };
    }
}
