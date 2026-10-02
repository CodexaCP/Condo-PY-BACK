using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>Base de los controladores que leen el libro: ademas del acceso al modulo exige la configuracion inicial completa.</summary>
public abstract class FinanceLedgerControllerBase(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    FinanceModuleGate gate,
    FinanceLedgerService ledger) : FinanceControllerBase(dbContext, accessScope, tenantContext, gate)
{
    public const string SetupIncompleteCode = "finance_setup_incomplete";

    protected FinanceLedgerService Ledger => ledger;

    // Modulo disponible + configuracion inicial completa. Si falta la configuracion responde 409 con un mensaje claro.
    protected async Task<(ActionResult? Denied, LedgerContext? Context)> RequireLedgerAsync(
        Guid buildingId, CancellationToken cancellationToken, bool write = false, bool budget = false)
    {
        var denied = await RequireModuleAsync(buildingId, write, cancellationToken, budget: budget);
        if (denied is not null)
        {
            return (denied, null);
        }

        var ctx = await ledger.LoadContextAsync(buildingId, cancellationToken);
        if (ctx is null)
        {
            return (StatusCode(StatusCodes.Status409Conflict, new
            {
                error = SetupIncompleteCode,
                message = "Completá la configuración inicial de Finanzas del edificio (fecha de arranque, cuentas y plan de cuentas) para ver los saldos, movimientos y reportes."
            }), null);
        }

        return (null, ctx);
    }

    // Mes pedido (por defecto, el actual), acotado entre el mes de arranque y el mes actual.
    protected static DateOnly ResolveMonth(int? year, int? month, DateOnly today, DateOnly start, out string? error)
    {
        error = null;
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            error = "El año o el mes no son válidos.";
            return today;
        }

        var requested = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        var current = new DateOnly(today.Year, today.Month, 1);
        var first = new DateOnly(start.Year, start.Month, 1);
        if (requested > current) requested = current;
        if (requested < first) requested = first;
        return requested;
    }
}
