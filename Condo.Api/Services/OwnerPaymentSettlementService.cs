using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Contexto de una liquidacion: la empresa y el alcance por edificio de quien la dispara. Con <see cref="Scope"/> nulo la dispara el
/// sistema (por ejemplo, la confirmacion de un proveedor de pagos, que llega sin sesion) y no se limita por edificio.
/// </summary>
public sealed record SettlementContext(Guid CompanyId, HashSet<Guid>? Scope)
{
    public bool IsSystem => Scope is null;
}

// El pago caeria en un mes contable cerrado (Centro de configuracion): el controlador responde 409 con el codigo de cierre.
public sealed class PeriodClosedException(ClosedMonth month) : InvalidOperationException(FinancePeriodGuard.MessageFor(month))
{
    public ClosedMonth Month { get; } = month;
}

// El pago incluye comprobantes de edificios fuera del alcance de quien lo procesa.
public sealed class OutOfScopeException(string message) : InvalidOperationException(message);

/// <summary>
/// Liquidacion de un pago de propietario: la unica que crea los Payment y sus asignaciones, para la aprobacion de un pago de la app, el
/// registro del personal y (despues) el cobro online. No depende de la sesion: recibe el contexto de ejecucion.
/// Un comprobante = un pago completo. Se cubren comprobantes enteros, del mas antiguo al mas reciente, y el monto (sumado al saldo a
/// favor disponible) debe coincidir exactamente: sin pagos parciales ni pago por linea. El Payment de cada comprobante siempre vale su
/// monto completo (eso es lo que factura Invoice); el saldo a favor es un detalle de como se financio, se descuenta aparte via
/// OwnerCreditMovement, nunca reduce el Payment ni la factura.
/// </summary>
public class OwnerPaymentSettlementService(ICondoDbContext dbContext, OwnerCreditService credits, ComprobanteService comprobantes)
{
    public const string OutOfScopeMessage =
        "Este pago incluye unidades o comprobantes de edificios que no tenés asignados. " +
        "Debe procesarlo un Administrador de empresa o un encargado con acceso a todos esos edificios.";

    // Comprobantes pendientes del propietario: todas sus unidades vinculadas mas las declaradas en el pago.
    public async Task<List<Comprobante>> LoadOpenComprobantesAsync(OwnerPayment ownerPayment, Guid companyId, CancellationToken ct)
    {
        var unitIds = await credits.LoadLinkedUnitIdsAsync(ownerPayment.OwnerId, companyId, ct);

        foreach (var declaredUnit in ownerPayment.Units.Where(u => !u.IsDeleted))
        {
            if (!unitIds.Contains(declaredUnit.UnitId)) unitIds.Add(declaredUnit.UnitId);
        }

        return await comprobantes.LoadAsync(unitIds, companyId, true, ct);
    }

    public async Task<decimal> GetAvailableCreditAsync(Guid ownerId, Guid companyId, CancellationToken ct) =>
        (await dbContext.OwnerCredits.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId, ct))?.Amount ?? 0m;

    // Prueba primero el monto solo (comportamiento de siempre, no se toca un pago que ya cerraba
    // exacto aunque el propietario tenga saldo a favor sin usar) y, solo si no alcanza a cubrir
    // comprobantes completos, prueba sumando el saldo a favor disponible. Nunca fuerza el credito
    // cuando no hace falta: sumarlo siempre rompia pagos exactos si quedaba un resto de credito.
    public static (ComprobanteCoverage Coverage, decimal CreditUsed) CoverWithCredit(
        decimal amount, decimal availableCredit, IReadOnlyList<Comprobante> comprobantes)
    {
        var withoutCredit = ComprobanteService.Cover(amount, comprobantes);
        if (withoutCredit.Exact) return (withoutCredit, 0m);
        if (availableCredit <= 0) return (withoutCredit, 0m);

        var withCredit = ComprobanteService.Cover(amount + availableCredit, comprobantes);
        if (!withCredit.Exact) return (withCredit, 0m);

        var creditUsed = Math.Min(availableCredit, Math.Max(0, withCredit.CoveredTotal - amount));
        return (withCredit, creditUsed);
    }

    public static bool CoverageInScope(ComprobanteCoverage coverage, HashSet<Guid> scope) =>
        coverage.Covered.All(c => scope.Contains(c.BuildingId));

    // Cierre contable de un mes: el primer mes cerrado entre los edificios que cubre el pago, segun la fecha del pago.
    public async Task<ClosedMonth?> FindClosedForCoverageAsync(ComprobanteCoverage coverage, DateOnly paymentDate, CancellationToken ct)
    {
        var guard = new FinancePeriodGuard(dbContext);
        foreach (var buildingId in coverage.Covered.Select(c => c.BuildingId).Distinct())
        {
            var closed = await guard.FindClosedAsync(buildingId, paymentDate, ct);
            if (closed is not null) return closed;
        }

        return null;
    }

    // ─── REFERENCIA DEL PAGO ─────────────────────────────────────────────────
    // PAY-{año}-{n de 6 digitos}, numerada POR EMPRESA (dos empresas pueden tener el mismo PAY-2026-000001;
    // por eso toda busqueda por referencia debe filtrar tambien por empresa).
    public async Task<string> NextReferenceAsync(Guid companyId, int year, CancellationToken ct)
    {
        var prefix = $"PAY-{year}-";

        // El sufijo lleva ceros a la izquierda: el mayor en orden alfabetico es el ultimo numero usado
        // (incluye pagos eliminados, para no reutilizar numeros).
        var last = await dbContext.OwnerPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Reference.StartsWith(prefix))
            .OrderByDescending(x => x.Reference)
            .Select(x => x.Reference)
            .FirstOrDefaultAsync(ct);

        var next = last is not null && int.TryParse(last[prefix.Length..], out var number) ? number + 1 : 1;
        return $"{prefix}{next:D6}";
    }

    /// <summary>
    /// Guarda un pago nuevo (ya agregado al contexto) con su referencia. Dos pagos de la misma empresa enviados a la vez pueden calcular la
    /// misma referencia: el indice unico (empresa, referencia) rechaza al segundo y se reintenta con la siguiente. Las entidades siguen en
    /// estado Added tras el error, asi que alcanza con cambiar la referencia y volver a guardar.
    /// </summary>
    public async Task SaveNewAsync(OwnerPayment ownerPayment, Guid companyId, int year, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dbContext.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException exception) when (attempt < 5 && IsReferenceCollision(exception))
            {
                ownerPayment.Reference = await NextReferenceAsync(companyId, year, ct);
            }
        }
    }

    private static bool IsReferenceCollision(DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && (sql.Number == 2601 || sql.Number == 2627)
        && sql.Message.Contains("IX_OwnerPayments_", StringComparison.OrdinalIgnoreCase);

    // ─── LIQUIDACION ─────────────────────────────────────────────────────────

    /// <summary>
    /// Crea los Payment (uno por comprobante cubierto), sus asignaciones a cargos y consume el saldo a favor que haga falta. No cambia el
    /// estado del OwnerPayment ni guarda los cambios: eso lo hace quien llama, en su propia transaccion.
    /// Lanza <see cref="PeriodClosedException"/>, <see cref="OutOfScopeException"/> o <see cref="InvalidOperationException"/> (monto que no
    /// cubre comprobantes completos) sin haber tocado nada.
    /// </summary>
    public async Task SettleAsync(OwnerPayment ownerPayment, SettlementContext context, CancellationToken ct)
    {
        var companyId = context.CompanyId;
        var open = await LoadOpenComprobantesAsync(ownerPayment, companyId, ct);
        var reviewedAmount = ownerPayment.ReviewedAmount!.Value;

        var ownerCredit = await dbContext.OwnerCredits
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerPayment.OwnerId && x.CompanyId == companyId, ct);
        var availableCredit = ownerCredit?.Amount ?? 0m;

        var (coverage, creditNeeded) = CoverWithCredit(reviewedAmount, availableCredit, open);

        if (!coverage.Exact)
            throw new InvalidOperationException(ComprobanteService.MismatchMessage(reviewedAmount + availableCredit, open)
                                                + " Rechace el pago para que el propietario lo envíe nuevamente.");

        // Se valida antes de tocar nada: no se liquida ningun comprobante de un edificio fuera del alcance.
        if (context.Scope is not null && !CoverageInScope(coverage, context.Scope))
            throw new OutOfScopeException(OutOfScopeMessage);

        // Cierre contable de un mes: los pagos nuevos llevan la fecha del pago.
        var closedMonth = await FindClosedForCoverageAsync(coverage, ownerPayment.PaymentDate, ct);
        if (closedMonth is not null)
            throw new PeriodClosedException(closedMonth);

        var allocatedPerUnit = ownerPayment.Units
            .Where(u => !u.IsDeleted)
            .ToDictionary(u => u.UnitId, u => 0m);

        var createdPayments = new List<(Comprobante Comprobante, Payment Payment)>();

        foreach (var comprobante in coverage.Covered)
        {
            var paymentRecord = new Payment
            {
                CompanyId = companyId,
                ExpensePeriodId = comprobante.ExpensePeriodId,
                UnitId = comprobante.UnitId,
                PaymentDate = ownerPayment.PaymentDate,
                Amount = comprobante.Total,
                Method = ownerPayment.Method,
                Reference = ownerPayment.Reference,
                Notes = ownerPayment.Channel == OwnerPaymentChannel.Web
                    ? $"Pago registrado por el sistema. Comprobante {comprobante.Label}. Ref: {ownerPayment.Reference}" +
                      (string.IsNullOrWhiteSpace(ownerPayment.ExternalReference) ? "" : $". Doc.: {ownerPayment.ExternalReference}")
                    : $"Pago de propietario aprobado. Comprobante {comprobante.Label}. Ref: {ownerPayment.Reference}"
            };
            dbContext.Payments.Add(paymentRecord);
            createdPayments.Add((comprobante, paymentRecord));

            foreach (var (charge, pending) in comprobante.Lines)
            {
                dbContext.PaymentAllocations.Add(new PaymentAllocation
                {
                    CompanyId = companyId,
                    PaymentId = paymentRecord.Id,
                    ExpenseChargeId = charge.Id,
                    AllocatedAmount = pending
                });
            }

            if (!allocatedPerUnit.ContainsKey(comprobante.UnitId))
            {
                // Una unidad que el pago cubre pero no se declaro. Se agrega al contexto (la clave ya viene asignada, asi que EF la trataria
                // como existente si solo se sumara a la coleccion) y EF la incorpora sola a ownerPayment.Units por su OwnerPaymentId: sumarla
                // tambien a mano la dejaria repetida en la coleccion.
                dbContext.OwnerPaymentUnits.Add(new OwnerPaymentUnit
                {
                    CompanyId = companyId,
                    OwnerPaymentId = ownerPayment.Id,
                    UnitId = comprobante.UnitId,
                    AllocatedAmount = 0
                });
                allocatedPerUnit[comprobante.UnitId] = 0m;
            }

            allocatedPerUnit[comprobante.UnitId] += comprobante.Total;
        }

        foreach (var pUnit in ownerPayment.Units.Where(u => !u.IsDeleted))
        {
            if (allocatedPerUnit.TryGetValue(pUnit.UnitId, out var allocated))
                pUnit.AllocatedAmount = allocated;
        }

        // Saldo a favor consumido automaticamente (nunca lo elige el propietario), del comprobante
        // mas antiguo cubierto en adelante. creditNeeded ya viene en 0 si el monto solo alcanzaba.
        var creditUsed = creditNeeded;
        if (creditUsed > 0 && ownerCredit is not null)
        {
            var lots = await credits.EnsureLotsAsync(ownerPayment.OwnerId, companyId, ownerCredit.Amount, ct);
            var remaining = creditUsed;
            foreach (var (comprobante, paymentRecord) in createdPayments)
            {
                if (remaining <= 0) break;
                var take = Math.Min(remaining, comprobante.Total);
                credits.ConsumeLots(lots, take, ownerPayment.OwnerId, companyId, CreditApplyMode.OnPaymentApproval,
                    paymentRecord.Id, null, $"Aplicado automáticamente al comprobante {comprobante.Label}.");
                paymentRecord.Notes += $" Incluye Gs. {ComprobanteService.Gs(take)} de saldo a favor.";
                remaining -= take;
            }

            ownerCredit.Amount -= creditUsed;
        }
    }
}
