using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Un mes calendario cerrado.</summary>
public sealed record ClosedMonth(int Year, int Month)
{
    public string Label => $"{Month:00}/{Year}";
}

/// <summary>
/// Guarda del cierre de periodo: el unico lugar donde se decide si un mes de un edificio esta cerrado. Todos los puntos que
/// escriben lo que alimenta el libro (gastos, ingresos, pagos, notas de credito de proveedor y presupuesto) le preguntan antes de
/// escribir, con la fecha del movimiento. El cierre solo rige si el edificio lo tiene encendido, el modulo Finanzas esta disponible
/// y el mes tiene un cierre vigente (no reabierto). Sin cierre encendido no consulta nada mas: el costo para el resto es una consulta.
/// </summary>
public sealed class FinancePeriodGuard(ICondoDbContext dbContext)
{
    public const string ClosedCode = "finance_period_closed";

    /// <summary>Mensaje comun cuando algo cae en un mes cerrado.</summary>
    public static string MessageFor(ClosedMonth month) =>
        $"El mes {month.Label} está cerrado: no se pueden agregar, modificar ni eliminar movimientos de ese mes. " +
        "Pedí que lo reabran desde Configuración del edificio → Período y cierre.";

    /// <summary>La respuesta 409 uniforme { error, message } que usan todas las pantallas que escriben.</summary>
    public static ObjectResult ClosedResponse(ClosedMonth month) =>
        new(new { error = ClosedCode, message = MessageFor(month) }) { StatusCode = StatusCodes.Status409Conflict };

    /// <summary>
    /// 409 con el mismo codigo de cierre pero con un mensaje propio: para lo que no es un movimiento con fecha pero cambiaria los
    /// numeros de los meses cerrados (fecha de arranque, saldos iniciales, reemplazo del plan de cuentas).
    /// </summary>
    public static ObjectResult ClosedMonthsExistResponse(string what) =>
        new(new
        {
            error = ClosedCode,
            message = $"Hay meses cerrados: {what} cambiaría sus números. Reabrí primero los meses cerrados desde Configuración del edificio → Período y cierre."
        })
        { StatusCode = StatusCodes.Status409Conflict };

    /// <summary>El cierre rige en el edificio: interruptor encendido y modulo Finanzas disponible.</summary>
    public async Task<bool> IsEnabledAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var enabled = await dbContext.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => x.PeriodClosingEnabled)
            .FirstOrDefaultAsync(cancellationToken);
        if (!enabled)
        {
            return false;
        }

        return (await new FinanceModuleGate(dbContext).GetStateAsync(buildingId, cancellationToken)).IsAvailable;
    }

    /// <summary>Hay al menos un mes cerrado vigente (con el cierre rigiendo).</summary>
    public async Task<bool> AnyClosedAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(buildingId, cancellationToken))
        {
            return false;
        }

        return await dbContext.FinancePeriodClosures.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.BuildingId == buildingId && x.ReopenedAtUtc == null, cancellationToken);
    }

    public Task<ClosedMonth?> FindClosedAsync(Guid buildingId, DateOnly date, CancellationToken cancellationToken) =>
        FindClosedMonthsAsync(buildingId, [(date.Year, date.Month)], cancellationToken);

    /// <summary>El primer mes cerrado (el mas antiguo) entre las fechas indicadas, o null si ninguna cae en un mes cerrado.</summary>
    public Task<ClosedMonth?> FindClosedAsync(Guid buildingId, IEnumerable<DateOnly> dates, CancellationToken cancellationToken) =>
        FindClosedMonthsAsync(buildingId, dates.Select(d => (d.Year, d.Month)), cancellationToken);

    public async Task<ClosedMonth?> FindClosedMonthsAsync(
        Guid buildingId, IEnumerable<(int Year, int Month)> months, CancellationToken cancellationToken)
    {
        var wanted = months.Distinct().ToList();
        if (wanted.Count == 0 || !await IsEnabledAsync(buildingId, cancellationToken))
        {
            return null;
        }

        var closed = await ClosedSetAsync(buildingId, cancellationToken);
        return wanted.Where(closed.Contains).OrderBy(x => x.Year).ThenBy(x => x.Month)
            .Select(x => new ClosedMonth(x.Year, x.Month)).FirstOrDefault();
    }

    /// <summary>
    /// El primer mes cerrado entre los pagos indicados: cada pago cuenta en el edificio de su periodo de expensas y en el mes de su
    /// fecha de pago.
    /// </summary>
    public async Task<ClosedMonth?> FindClosedForPaymentsAsync(IEnumerable<Payment> payments, CancellationToken cancellationToken)
    {
        var list = payments.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var periodIds = list.Select(p => p.ExpensePeriodId).Distinct().ToList();
        var buildingByPeriod = await dbContext.ExpensePeriods.AsNoTracking()
            .Where(p => periodIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.BuildingId, cancellationToken);

        foreach (var group in list.Where(p => buildingByPeriod.ContainsKey(p.ExpensePeriodId)).GroupBy(p => buildingByPeriod[p.ExpensePeriodId]))
        {
            var closed = await FindClosedAsync(group.Key, group.Select(p => p.PaymentDate), cancellationToken);
            if (closed is not null)
            {
                return closed;
            }
        }

        return null;
    }

    /// <summary>Los meses cerrados vigentes del edificio (sin mirar el interruptor: para armar la pantalla del cierre).</summary>
    public async Task<HashSet<(int Year, int Month)>> ClosedSetAsync(Guid buildingId, CancellationToken cancellationToken) =>
        (await dbContext.FinancePeriodClosures.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.ReopenedAtUtc == null)
            .Select(x => new { x.Year, x.Month })
            .ToListAsync(cancellationToken))
        .Select(x => (x.Year, x.Month))
        .ToHashSet();
}
