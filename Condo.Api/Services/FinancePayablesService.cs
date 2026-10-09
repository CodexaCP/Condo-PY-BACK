using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Cuentas por pagar y por cobrar del modulo Finanzas. Las cuentas por pagar son los gastos con vencimiento: pendientes hasta que se
/// registra su pago. Las cuentas por cobrar y la morosidad son por lo devengado (decision de Finanzas): lo cobrado a las unidades en los
/// periodos publicados, menos lo pagado (los pagos revertidos no cuentan). Los importes se suman en memoria, como el resto de los reportes.
/// </summary>
public class FinancePayablesService(ICondoDbContext dbContext)
{
    public const string StatusAll = "all";
    public const string StatusPending = "pending";
    public const string StatusOverdue = "overdue";
    public const string StatusPaid = "paid";

    private static readonly (string Label, int From, int To)[] AgingBands =
    [
        ("1 a 30 días", 1, 30),
        ("31 a 60 días", 31, 60),
        ("61 a 90 días", 61, 90),
        ("Más de 90 días", 91, int.MaxValue)
    ];

    public async Task<PayablesPageDto> ListAsync(
        Guid buildingId, string? status, Guid? supplierId, DateOnly? from, DateOnly? to, int page, int pageSize, DateOnly today, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var normalized = (status ?? StatusAll).Trim().ToLowerInvariant();

        var query = dbContext.BuildingExpenses.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.DueDate != null);

        query = normalized switch
        {
            StatusPending => query.Where(x => x.PaidAt == null),
            StatusOverdue => query.Where(x => x.PaidAt == null && x.DueDate < today),
            StatusPaid => query.Where(x => x.PaidAt != null),
            _ => query
        };

        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId.Value);
        if (from.HasValue) query = query.Where(x => x.DueDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.DueDate <= to.Value);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(x => x.PaidAt != null)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id, x.ExpensePeriodId, PeriodName = x.ExpensePeriod != null ? x.ExpensePeriod.Name : string.Empty,
                x.SupplierId, x.SupplierName, SupplierRuc = x.Supplier != null ? x.Supplier.Ruc : null,
                x.Description, x.InvoiceNumber, x.ExpenseDate, x.DueDate, x.Amount, x.PaidAt,
                PaidFromAccountName = x.PaidFromAccount != null ? x.PaidFromAccount.Name : null,
                CategoryCode = x.LedgerCategory != null ? x.LedgerCategory.Code : null,
                CategoryName = x.LedgerCategory != null ? x.LedgerCategory.Name : null,
                x.VatRate
            })
            .ToListAsync(ct);

        return new PayablesPageDto
        {
            BuildingId = buildingId,
            Summary = await SummaryAsync(buildingId, today, ct),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            Items = rows.Select(x => new PayableRowDto
            {
                ExpenseId = x.Id,
                ExpensePeriodId = x.ExpensePeriodId,
                ExpensePeriodName = x.PeriodName,
                SupplierId = x.SupplierId,
                SupplierName = x.SupplierName,
                SupplierRuc = x.SupplierRuc,
                Description = x.Description,
                InvoiceNumber = x.InvoiceNumber,
                ExpenseDate = x.ExpenseDate,
                DueDate = x.DueDate!.Value,
                Amount = x.Amount,
                Status = ExpensePayables.StatusOf(x.DueDate, x.PaidAt, today),
                DaysOverdue = x.PaidAt is null && x.DueDate!.Value < today ? today.DayNumber - x.DueDate.Value.DayNumber : 0,
                PaidAt = x.PaidAt,
                PaidFromAccountName = x.PaidFromAccountName,
                LedgerCategoryCode = x.CategoryCode,
                LedgerCategoryName = x.CategoryName,
                VatRate = x.VatRate,
                VatAmount = VatMath.VatOf(x.Amount, x.VatRate)
            }).ToList()
        };
    }

    public async Task<PayablesSummaryDto> SummaryAsync(Guid buildingId, DateOnly today, CancellationToken ct)
    {
        var unpaid = await dbContext.BuildingExpenses.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.DueDate != null && x.PaidAt == null)
            .Select(x => new { x.Amount, DueDate = x.DueDate!.Value })
            .ToListAsync(ct);

        var overdue = unpaid.Where(x => x.DueDate < today).ToList();
        var next7 = unpaid.Where(x => x.DueDate >= today && x.DueDate <= today.AddDays(7)).Sum(x => x.Amount);

        return new PayablesSummaryDto
        {
            PendingCount = unpaid.Count,
            PendingTotal = unpaid.Sum(x => x.Amount),
            OverdueCount = overdue.Count,
            OverdueTotal = overdue.Sum(x => x.Amount),
            DueNext7DaysTotal = next7,
            Aging = AgingBands.Select(band =>
            {
                var inBand = overdue.Where(x =>
                {
                    var days = today.DayNumber - x.DueDate.DayNumber;
                    return days >= band.From && days <= band.To;
                }).ToList();
                return new PayableAgingBucketDto { Label = band.Label, Count = inBand.Count, Total = inBand.Sum(x => x.Amount) };
            }).ToList()
        };
    }

    /// <summary>Lo cobrado, lo pendiente y la morosidad de las unidades del edificio, por lo devengado (periodos publicados).</summary>
    public async Task<ReceivablesSummaryDto> ReceivablesAsync(Guid buildingId, DateOnly today, CancellationToken ct)
    {
        var periods = await dbContext.ExpensePeriods.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status != ExpensePeriodStatus.Draft)
            .Select(x => new { x.Id, x.DueDate })
            .ToListAsync(ct);
        if (periods.Count == 0) return new ReceivablesSummaryDto { Aging = EmptyAging() };

        var periodIds = periods.Select(x => x.Id).ToList();
        var dueByPeriod = periods.ToDictionary(x => x.Id, x => x.DueDate);

        var charged = (await dbContext.ExpenseCharges.AsNoTracking()
                .Where(x => !x.IsDeleted && periodIds.Contains(x.ExpensePeriodId))
                .Select(x => new { x.UnitId, x.ExpensePeriodId, x.Amount })
                .ToListAsync(ct))
            .GroupBy(x => (x.UnitId, x.ExpensePeriodId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var paid = (await dbContext.Payments.AsNoTracking()
                .Where(x => !x.IsDeleted && !x.IsReversed && periodIds.Contains(x.ExpensePeriodId))
                .Select(x => new { x.UnitId, x.ExpensePeriodId, x.Amount })
                .ToListAsync(ct))
            .GroupBy(x => (x.UnitId, x.ExpensePeriodId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        decimal totalCharged = 0m, totalCollected = 0m, totalPending = 0m, overdueAmount = 0m;
        var overdueUnits = new HashSet<Guid>();
        var aging = AgingBands.Select(b => (Band: b, Count: 0, Total: 0m)).ToArray();

        foreach (var (key, amount) in charged)
        {
            var paidAmount = paid.GetValueOrDefault(key, 0m);
            var collected = Math.Min(paidAmount, amount);
            var pending = Math.Max(amount - paidAmount, 0m);
            totalCharged += amount;
            totalCollected += collected;
            totalPending += pending;

            if (pending > 0m && dueByPeriod[key.ExpensePeriodId] < today)
            {
                overdueAmount += pending;
                overdueUnits.Add(key.UnitId);
                var days = today.DayNumber - dueByPeriod[key.ExpensePeriodId].DayNumber;
                for (var i = 0; i < aging.Length; i++)
                {
                    if (days >= aging[i].Band.From && days <= aging[i].Band.To)
                    {
                        aging[i] = (aging[i].Band, aging[i].Count + 1, aging[i].Total + pending);
                        break;
                    }
                }
            }
        }

        return new ReceivablesSummaryDto
        {
            TotalCharged = totalCharged,
            TotalCollected = totalCollected,
            TotalPending = totalPending,
            CollectionRatePercentage = totalCharged > 0m ? decimal.Round(totalCollected / totalCharged * 100m, 1) : 0m,
            OverdueAmount = overdueAmount,
            OverdueUnits = overdueUnits.Count,
            Aging = aging.Select(a => new PayableAgingBucketDto { Label = a.Band.Label, Count = a.Count, Total = a.Total }).ToList()
        };
    }

    private static List<PayableAgingBucketDto> EmptyAging() =>
        AgingBands.Select(b => new PayableAgingBucketDto { Label = b.Label }).ToList();
}
