using Condo.Application.Abstractions;
using Condo.Application.Services;

namespace Condo.Api.Services;

/// <summary>
/// Aplica la restriccion por plan vencido a Administrador de empresa, Operador y Encargado de edificio
/// (los demas roles no se tocan). La etapa sale de las fechas del plan (<see cref="PlanAccessPolicy"/>):
///   • ReadOnly: solo consulta (GET); lo unico que se puede escribir es el pago del plan.
///   • Blocked: no se puede usar nada salvo iniciar sesion y enviar el pago.
/// Cuando el usuario trabaja con varios edificios solo se corta si TODOS estan restringidos; los edificios
/// individuales en bloqueo total ya quedan fuera de su alcance (ver AccessScopeService).
/// </summary>
public sealed class PlanRestrictionMiddleware(RequestDelegate next)
{
    // Rutas que siguen disponibles aun con el plan bloqueado: sesion, avisos, y el envio del pago.
    private static readonly string[] AllowedPrefixes =
    [
        "/api/auth",
        "/api/notifications",
        "/api/devices",
        "/api/uploads",
        "/api/building-plan-payments",
        "/api/building-plans/my-plan"
    ];

    public async Task InvokeAsync(
        HttpContext context, ITenantContext tenantContext, IAccessScopeService accessScope, ICondoDbContext dbContext)
    {
        var path = context.Request.Path;

        if (!path.StartsWithSegments("/api") ||
            !tenantContext.IsAuthenticated ||
            !IsRestrictedRole(tenantContext) ||
            AllowedPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var ct = context.RequestAborted;
        var buildingIds = await accessScope.GetAllAccessibleBuildingIdsAsync(ct);
        if (buildingIds.Count == 0)
        {
            await next(context);
            return;
        }

        var phases = await PlanAccessQueries.LoadPhasesAsync(dbContext, buildingIds, ct);

        // Con varios edificios manda el menos restringido: si alguno sigue al dia, el usuario puede operar.
        var effective = buildingIds
            .Select(id => phases.TryGetValue(id, out var phase) ? phase : PlanAccessPhase.Normal)
            .Min();

        if (effective == PlanAccessPhase.Blocked)
        {
            await Reject(context, "plan_blocked",
                "El acceso está bloqueado por plan vencido. Enviá el comprobante de pago desde \"Mi plan\"; se habilita cuando el pago sea aprobado.");
            return;
        }

        if (effective == PlanAccessPhase.ReadOnly && !IsSafeMethod(context.Request.Method))
        {
            await Reject(context, "plan_read_only",
                "El plan está vencido: el sistema está en modo solo lectura. Solo podés enviar el comprobante de pago desde \"Mi plan\".");
            return;
        }

        await next(context);
    }

    private static bool IsRestrictedRole(ITenantContext tenant) =>
        tenant.IsCompanyAdmin ||
        string.Equals(tenant.Role, "CompanyOperator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tenant.Role, "BuildingManager", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static async Task Reject(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = code, message });
    }
}
