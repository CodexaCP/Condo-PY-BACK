using System.Globalization;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Deuda pendiente de propietarios y aplicacion de su saldo a favor. Lo comparten los pagos de
/// propietario (aprobacion y "Aplicar" manual) y la publicacion de un periodo (aplicacion automatica).
/// </summary>
public class OwnerCreditService(ICondoDbContext dbContext)
{
public async Task<List<Guid>> LoadLinkedUnitIdsAsync(Guid userId, Guid companyId, CancellationToken ct)
    {
        var owned = await dbContext.UnitOwners
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OwnerId == userId && x.CompanyId == companyId)
            .Select(x => x.UnitId)
            .ToListAsync(ct);

        var resided = await dbContext.UnitResidents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null && x.CompanyId == companyId
                     && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId == userId)
            .Select(x => x.UnitId)
            .ToListAsync(ct);

        return owned.Union(resided).ToList();
    }

public async Task<(List<ExpenseCharge> Charges, Dictionary<Guid, decimal> PendingById)> LoadPendingChargesAsync(
        IReadOnlyCollection<Guid> unitIds, Guid companyId, bool tracking, CancellationToken ct)
    {
        IQueryable<ExpenseCharge> query = dbContext.ExpenseCharges
            .Include(x => x.ExpensePeriod)
            .Include(x => x.Unit).ThenInclude(u => u!.Building)
            .Include(x => x.Allocations.Where(a => !a.IsDeleted));
        if (!tracking) query = query.AsNoTracking();

        var reversedIds = (await dbContext.ExpenseCharges
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsReversal && x.ReversalOfChargeId != null
                            && unitIds.Contains(x.UnitId) && x.CompanyId == companyId)
                .Select(x => x.ReversalOfChargeId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var charges = (await query
                .Where(x => !x.IsDeleted && !x.IsReversal && unitIds.Contains(x.UnitId) && x.CompanyId == companyId
                            && x.ExpensePeriod!.Status == ExpensePeriodStatus.Published)
                .ToListAsync(ct))
            .Where(c => !reversedIds.Contains(c.Id))
            .ToList();

        var pendingById = charges.ToDictionary(c => c.Id, c => c.Amount - c.Allocations.Sum(a => a.AllocatedAmount));

        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsReversed && unitIds.Contains(p.UnitId) && p.CompanyId == companyId
                        && p.ExpensePeriod!.Status == ExpensePeriodStatus.Published)
            .Select(p => new
            {
                p.UnitId,
                p.Amount,
                Allocated = p.Allocations.Where(a => !a.IsDeleted).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m
            })
            .ToListAsync(ct);

        var unallocatedByUnit = payments
            .GroupBy(p => p.UnitId)
            .ToDictionary(g => g.Key, g => g.Sum(p => decimal.Max(p.Amount - p.Allocated, 0m)));

        foreach (var unitGroup in charges.GroupBy(c => c.UnitId))
        {
            if (!unallocatedByUnit.TryGetValue(unitGroup.Key, out var remaining) || remaining <= 0) continue;

            foreach (var charge in unitGroup
                         .OrderBy(c => c.ExpensePeriod!.Year)
                         .ThenBy(c => c.ExpensePeriod!.Month)
                         .ThenBy(c => c.CreatedAtUtc))
            {
                if (remaining <= 0) break;
                var open = pendingById[charge.Id];
                if (open <= 0) continue;

                var take = decimal.Min(open, remaining);
                pendingById[charge.Id] = open - take;
                remaining -= take;
            }
        }

        return (charges, pendingById);
    }

    /// <summary>
    /// Aplica el saldo a favor del propietario a sus cargos pendientes, del mas antiguo al mas reciente
    /// (solo cargos completos; si el saldo no alcanza para uno se sigue con el siguiente que si alcance).
    /// Devuelve null si el propietario no tiene saldo. Guarda los cambios si aplico algo.
    /// </summary>
    public async Task<ApplyCreditResultDto?> ApplyCreditAsync(
        Guid ownerId, Guid companyId, CreditApplyMode mode, string? periodName, CancellationToken ct)
    {
        var credit = await dbContext.OwnerCredits
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId, ct);

        if (credit is null || credit.Amount <= 0)
            return null;

        var unitIds = await LoadLinkedUnitIdsAsync(ownerId, companyId, ct);

        var available = credit.Amount;
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var reference = "SALDO A FAVOR";

        var lots = await EnsureLotsAsync(ownerId, companyId, credit.Amount, ct);
        var (charges, pendingById) = await LoadPendingChargesAsync(unitIds, companyId, true, ct);

        var pending = charges
            .Where(c => pendingById[c.Id] > 0)
            .OrderBy(c => c.ExpensePeriod!.Year)
            .ThenBy(c => c.ExpensePeriod!.Month)
            .ThenByDescending(c => c.Amount)
            // Desempate fijo entre unidades con el mismo periodo y monto: edificio y luego unidad.
            .ThenBy(c => c.Unit!.Building!.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Unit!.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CreatedAtUtc)
            .ToList();

        var settled = 0m;
        var count = 0;

        foreach (var charge in pending)
        {
            if (available <= 0) break;
            var pendingAmount = pendingById[charge.Id];
            if (available < pendingAmount) continue;

            var paymentRecord = new Payment
            {
                CompanyId = companyId,
                ExpensePeriodId = charge.ExpensePeriodId,
                UnitId = charge.UnitId,
                PaymentDate = paymentDate,
                Amount = pendingAmount,
                Method = PaymentMethod.Other,
                Reference = reference,
                Notes = string.Empty
            };

            var slices = ConsumeLots(lots, pendingAmount, ownerId, companyId, mode, paymentRecord.Id, charge.Id, DescribeCharge(charge));
            paymentRecord.Reference = slices.FirstOrDefault(x => x.Reference is not null).Reference ?? "SALDO A FAVOR";
            paymentRecord.Notes = BuildCreditNote(slices, mode, periodName);
            dbContext.Payments.Add(paymentRecord);
            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                CompanyId = companyId,
                PaymentId = paymentRecord.Id,
                ExpenseChargeId = charge.Id,
                AllocatedAmount = pendingAmount
            });

            available -= pendingAmount;
            settled += pendingAmount;
            count++;
        }

        if (count > 0)
        {
            credit.Amount = available;
            await dbContext.SaveChangesAsync(ct);
        }

        return new ApplyCreditResultDto
        {
            SettledAmount = settled,
            RemainingCredit = available,
            ChargesSettled = count
        };
    }

    // ─── Historial (lotes de saldo) ──────────────────────────────────────────

    /// <summary>
    /// Lotes de saldo con algo por usar, del mas antiguo al mas reciente. Si el saldo actual del
    /// propietario es mayor que la suma de los lotes (saldo anterior al historial), crea un lote
    /// "saldo a favor anterior" que se consume primero.
    /// </summary>
    public async Task<List<OwnerCreditMovement>> EnsureLotsAsync(Guid ownerId, Guid companyId, decimal creditAmount, CancellationToken ct)
    {
        var lots = await dbContext.OwnerCreditMovements
            .Where(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId
                        && x.Kind == OwnerCreditMovementKind.Generated && x.RemainingAmount > 0)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var missing = creditAmount - lots.Sum(x => x.RemainingAmount);
        if (missing > 0)
        {
            var legacy = new OwnerCreditMovement
            {
                CompanyId = companyId,
                OwnerId = ownerId,
                Kind = OwnerCreditMovementKind.Generated,
                Amount = missing,
                RemainingAmount = missing,
                SourceReference = null,
                Description = "Saldo a favor anterior",
                CreatedAtUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            dbContext.OwnerCreditMovements.Add(legacy);
            lots.Insert(0, legacy);
        }

        return lots;
    }

    public void AddGeneratedLot(Guid ownerId, Guid companyId, decimal amount, string reference, Guid ownerPaymentId)
    {
        dbContext.OwnerCreditMovements.Add(new OwnerCreditMovement
        {
            CompanyId = companyId,
            OwnerId = ownerId,
            Kind = OwnerCreditMovementKind.Generated,
            Amount = amount,
            RemainingAmount = amount,
            SourceReference = reference,
            OwnerPaymentId = ownerPaymentId,
            Description = $"Saldo a favor generado por el comprobante de pago {reference}"
        });
    }

    /// <summary>Consume el monto de los lotes (mas antiguo primero) y registra cada porcion aplicada.</summary>
    public List<(string? Reference, decimal Amount)> ConsumeLots(
        List<OwnerCreditMovement> lots, decimal amount, Guid ownerId, Guid companyId,
        CreditApplyMode mode, Guid paymentId, Guid chargeId, string description)
    {
        var slices = new List<(string? Reference, decimal Amount)>();

        foreach (var lot in lots.Where(x => x.RemainingAmount > 0))
        {
            if (amount <= 0) break;

            var take = decimal.Min(lot.RemainingAmount, amount);
            lot.RemainingAmount -= take;
            amount -= take;

            dbContext.OwnerCreditMovements.Add(new OwnerCreditMovement
            {
                CompanyId = companyId,
                OwnerId = ownerId,
                Kind = OwnerCreditMovementKind.Applied,
                Amount = take,
                RemainingAmount = 0,
                SourceReference = lot.SourceReference,
                OwnerPaymentId = lot.OwnerPaymentId,
                ApplyMode = mode,
                PaymentId = paymentId,
                ExpenseChargeId = chargeId,
                Description = description
            });

            slices.Add((lot.SourceReference, take));
        }

        return slices;
    }

    public static string DescribeCharge(ExpenseCharge charge)
    {
        var unit = charge.Unit?.Code ?? string.Empty;
        var period = charge.ExpensePeriod is null ? string.Empty : $"{charge.ExpensePeriod.Month:00}/{charge.ExpensePeriod.Year}";
        return $"Aplicado a: {charge.Concept} — unidad {unit}, período {period}".Trim();
    }

    /// <summary>Texto para el comprobante: de que comprobante salio el saldo y como se aplico.</summary>
    public static string BuildCreditNote(IEnumerable<(string? Reference, decimal Amount)> slices, CreditApplyMode mode, string? periodName)
    {
        var sources = slices
            .GroupBy(x => x.Reference)
            .Select(g => g.Key is null
                ? $"saldo a favor anterior (Gs. {FormatGs(g.Sum(x => x.Amount))})"
                : $"comprobante de pago {g.Key} (Gs. {FormatGs(g.Sum(x => x.Amount))})")
            .ToList();

        var origin = sources.Count == 0 ? "Saldo a favor" : "Saldo a favor de " + string.Join(" y ", sources);
        var how = mode switch
        {
            CreditApplyMode.Automatic => string.IsNullOrWhiteSpace(periodName)
                ? "aplicado automáticamente"
                : $"aplicado automáticamente al publicar el período {periodName}",
            CreditApplyMode.ManualApp => "aplicado manualmente por el propietario desde la app",
            CreditApplyMode.ManualManager => "aplicado manualmente por el administrador",
            _ => "aplicado junto con el pago aprobado"
        };

        return $"{origin} {how}.";
    }

    private static string FormatGs(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");
}
