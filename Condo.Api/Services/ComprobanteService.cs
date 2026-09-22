using System.Globalization;
using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Interruptor de la aplicacion MANUAL/legacy de saldo a favor (cargo por cargo, vía
/// OwnerCreditService.ApplyCreditAsync desde /apply-credit). Sigue apagada porque ese algoritmo es
/// anterior a la regla "un comprobante es un pago completo" y puede dejar un comprobante a medio
/// pagar. El saldo a favor SI esta activo por otras dos vias, ninguna gobernada por este flag:
/// (1) se genera cuando una nota de credito reduce un cargo ya pagado (ver CreditNotesController),
/// (2) se consume automaticamente y completo en SettlePaymentAsync al aprobar el proximo pago del
/// propietario (nunca elige el propietario a que cargo se aplica).
/// </summary>
public static class OwnerCreditFeature
{
    public static readonly bool Enabled = false;
}

/// <summary>
/// Comprobante de deuda = todo lo pendiente de una unidad en un periodo (expensas y mora).
/// Es la unica unidad de pago: se paga completo o no se paga.
/// </summary>
public sealed class Comprobante
{
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;
    public string BuildingName { get; init; } = string.Empty;
    public Guid ExpensePeriodId { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public List<(ExpenseCharge Charge, decimal Pending)> Lines { get; init; } = new();

    public decimal Total => Lines.Sum(l => l.Pending);
    public string Label => $"{Month:00}/{Year} unidad {UnitCode}";
}

public sealed record ComprobanteCoverage(bool Exact, List<Comprobante> Covered, decimal CoveredTotal);

public class ComprobanteService(ICondoDbContext dbContext, OwnerCreditService credits)
{
    // El guarani no tiene decimales y el usuario declara montos enteros, pero los cargos (sobre todo la
    // mora diaria, redondeada a 2 decimales) suman fracciones. Se acepta hasta medio guarani de diferencia,
    // igual que la app; al liquidar se imputa el pendiente exacto de cada linea.
    private const decimal Tolerance = 0.5m;

    /// <summary>
    /// Comprobantes pendientes de las unidades (solo periodos publicados), del mas antiguo al mas
    /// reciente; a igual periodo, por edificio y unidad.
    /// </summary>
    public async Task<List<Comprobante>> LoadAsync(
        IReadOnlyCollection<Guid> unitIds, Guid companyId, bool tracking, CancellationToken ct)
    {
        var (charges, pendingById) = await credits.LoadPendingChargesAsync(unitIds, companyId, tracking, ct);

        return charges
            .Where(c => pendingById[c.Id] > 0)
            .GroupBy(c => (c.UnitId, c.ExpensePeriodId))
            .Select(g =>
            {
                var first = g.First();
                return new Comprobante
                {
                    UnitId = g.Key.UnitId,
                    UnitCode = first.Unit?.Code ?? string.Empty,
                    BuildingName = first.Unit?.Building?.Name ?? string.Empty,
                    ExpensePeriodId = g.Key.ExpensePeriodId,
                    Year = first.ExpensePeriod!.Year,
                    Month = first.ExpensePeriod.Month,
                    Lines = g.OrderBy(c => c.Concept).ThenBy(c => c.CreatedAtUtc)
                        .Select(c => (c, pendingById[c.Id])).ToList()
                };
            })
            .OrderBy(c => c.Year)
            .ThenBy(c => c.Month)
            .ThenBy(c => c.BuildingName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.UnitCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Pendiente de un comprobante puntual (unidad + periodo) sin exigir que el periodo este publicado.
    /// Lo usa el registro manual de pagos del administrador.
    /// </summary>
    public async Task<Comprobante?> LoadUnitPeriodAsync(
        Guid unitId, Guid expensePeriodId, Guid companyId, CancellationToken ct)
    {
        var reversedIds = (await dbContext.ExpenseCharges
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsReversal && x.ReversalOfChargeId != null
                            && x.UnitId == unitId && x.ExpensePeriodId == expensePeriodId)
                .Select(x => x.ReversalOfChargeId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var charges = (await dbContext.ExpenseCharges
                .AsNoTracking()
                .Include(x => x.ExpensePeriod)
                .Include(x => x.Unit).ThenInclude(u => u!.Building)
                .Include(x => x.Allocations.Where(a => !a.IsDeleted))
                .Where(x => !x.IsDeleted && !x.IsReversal && x.CompanyId == companyId
                            && x.UnitId == unitId && x.ExpensePeriodId == expensePeriodId)
                .ToListAsync(ct))
            .Where(c => !reversedIds.Contains(c.Id))
            .Select(c => (Charge: c, Pending: c.Amount - c.Allocations.Sum(a => a.AllocatedAmount)))
            .Where(x => x.Pending > 0)
            .ToList();

        if (charges.Count == 0) return null;

        var first = charges[0].Charge;
        return new Comprobante
        {
            UnitId = unitId,
            UnitCode = first.Unit?.Code ?? string.Empty,
            BuildingName = first.Unit?.Building?.Name ?? string.Empty,
            ExpensePeriodId = expensePeriodId,
            Year = first.ExpensePeriod!.Year,
            Month = first.ExpensePeriod.Month,
            Lines = charges.OrderBy(x => x.Charge.Concept).ThenBy(x => x.Charge.CreatedAtUtc)
                .Select(x => (x.Charge, x.Pending)).ToList()
        };
    }

    /// <summary>
    /// Cubre comprobantes completos, del mas antiguo al mas reciente, hasta agotar el monto.
    /// Es exacto solo si el monto coincide con la suma de los comprobantes cubiertos (sin resto).
    /// </summary>
    public static ComprobanteCoverage Cover(decimal amount, IReadOnlyList<Comprobante> comprobantes)
    {
        var covered = new List<Comprobante>();
        var running = 0m;

        foreach (var comprobante in comprobantes)
        {
            var next = running + comprobante.Total;
            if (next - amount > Tolerance) break;

            covered.Add(comprobante);
            running = next;
            if (Math.Abs(amount - running) <= Tolerance) break;
        }

        return new ComprobanteCoverage(covered.Count > 0 && Math.Abs(amount - running) <= Tolerance, covered, running);
    }

    /// <summary>Mensaje para el usuario cuando el monto no corresponde a comprobantes completos.</summary>
    public static string MismatchMessage(decimal amount, IReadOnlyList<Comprobante> comprobantes)
    {
        if (comprobantes.Count == 0)
            return "No tiene comprobantes pendientes de pago.";

        var options = new List<string>();
        var running = 0m;
        foreach (var comprobante in comprobantes.Take(3))
        {
            running += comprobante.Total;
            options.Add(options.Count == 0
                ? $"Gs. {Gs(running)} (comprobante {comprobante.Label})"
                : $"Gs. {Gs(running)} (hasta {comprobante.Label})");
        }

        return $"El monto Gs. {Gs(amount)} no cubre la totalidad de un comprobante. " +
               "No se aceptan pagos parciales: el pago (sumado al saldo a favor si lo hay, aplicado automáticamente) debe cubrir exactamente " +
               string.Join(" o ", options) + ".";
    }

    public static string Gs(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");
}
