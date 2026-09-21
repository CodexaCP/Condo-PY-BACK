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
    public async Task<ApplyCreditResultDto?> ApplyCreditAsync(Guid ownerId, Guid companyId, CancellationToken ct)
    {
        var credit = await dbContext.OwnerCredits
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.OwnerId == ownerId && x.CompanyId == companyId, ct);

        if (credit is null || credit.Amount <= 0)
            return null;

        var unitIds = await LoadLinkedUnitIdsAsync(ownerId, companyId, ct);

        var available = credit.Amount;
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var reference = $"CREDIT-{DateTime.UtcNow:yyyyMMddHHmmss}";

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
                Method = PaymentMethod.BankTransfer,
                Reference = reference,
                Notes = $"Aplicación de saldo a favor. Ref: {reference}"
            };
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
}
