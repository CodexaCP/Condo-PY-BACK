using Condo.Application.Abstractions;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Lee los cobros, gastos e ingresos del edificio y los clasifica con <see cref="FinanceLedgerRules"/>. Hay dos caminos que
/// aplican exactamente las mismas reglas: los agregados por mes (<see cref="GetBucketsAsync"/>, una consulta agrupada en la
/// base, para saldos, flujo y tablero) y los movimientos linea por linea de un rango acotado (<see cref="GetRowsAsync"/>).
/// No modifica nada: los flujos de pagos y liquidacion no se tocan.
/// </summary>
public class FinanceLedgerService(ICondoDbContext dbContext)
{
    // Un rango de movimientos linea por linea no puede abarcar mas de esto (los reportes por mes no tienen limite).
    public const int MaxRowsRangeDays = 400;

    /// <summary>Contexto del libro del edificio; nulo si la configuracion inicial no esta completa (no hay fecha de arranque).</summary>
    public async Task<LedgerContext?> LoadContextAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.FinanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == buildingId, cancellationToken);
        if (settings is null || !settings.SetupCompleted || settings.FinanceStartDate is null)
        {
            return null;
        }

        var building = await dbContext.Buildings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == buildingId)
            .Select(x => new { x.Name, x.IncomeTreatment })
            .FirstOrDefaultAsync(cancellationToken);
        if (building is null)
        {
            return null;
        }

        var accounts = await dbContext.FinancialAccounts.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);
        var categories = await dbContext.LedgerCategories.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToListAsync(cancellationToken);

        return new LedgerContext
        {
            BuildingId = buildingId,
            BuildingName = building.Name,
            StartDate = settings.FinanceStartDate.Value,
            FiscalYearStartMonth = settings.FiscalYearStartMonth,
            IncomeTreatment = building.IncomeTreatment,
            Accounts = accounts,
            Resolved = LedgerAccounts.Resolve(accounts, settings.DefaultAccountId),
            Categories = categories
        };
    }

    /// <summary>
    /// Movimientos del rango sumados por mes, cuenta y rubro. Solo cuenta desde la fecha de arranque; excluye pagos revertidos
    /// y borrados. El total de los cobros siempre es la suma de los pagos: lo que no se pueda imputar a un tipo de cargo cae en
    /// expensas ordinarias.
    /// </summary>
    public async Task<List<LedgerBucket>> GetBucketsAsync(LedgerContext ctx, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        from = from < ctx.StartDate ? ctx.StartDate : from;
        var raw = new List<LedgerBucket>();
        if (to < from)
        {
            return raw;
        }

        var buildingId = ctx.BuildingId;

        var paymentTotals = await dbContext.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsReversed && p.Unit!.BuildingId == buildingId && p.PaymentDate >= from && p.PaymentDate <= to)
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month, p.Method })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Method, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var allocationTotals = await dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => !a.IsDeleted && a.Charge != null && a.Payment != null
                        && !a.Payment.IsDeleted && !a.Payment.IsReversed
                        && a.Payment.Unit!.BuildingId == buildingId
                        && a.Payment.PaymentDate >= from && a.Payment.PaymentDate <= to)
            .GroupBy(a => new { a.Payment!.PaymentDate.Year, a.Payment.PaymentDate.Month, a.Payment.Method, a.Charge!.ChargeType, a.Charge.IsLateFee })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Method, g.Key.ChargeType, g.Key.IsLateFee, Amount = g.Sum(x => x.AllocatedAmount) })
            .ToListAsync(cancellationToken);

        foreach (var pt in paymentTotals)
        {
            decimal allocated = 0m;
            var byClass = allocationTotals
                .Where(a => a.Year == pt.Year && a.Month == pt.Month && a.Method == pt.Method)
                .GroupBy(a => FinanceLedgerRules.ClassifyCharge(a.ChargeType, a.IsLateFee));

            foreach (var cls in byClass)
            {
                var amount = cls.Sum(a => a.Amount);
                allocated += amount;
                AddBucket(raw, pt.Year, pt.Month, FinanceLedgerRules.ForPayment(pt.Method, cls.Key, ctx.Resolved), amount);
            }

            // Lo no imputado a ningun cargo cae en ordinarias; asi el total cobrado siempre coincide con la suma de los pagos.
            var remainder = pt.Amount - allocated;
            if (remainder != 0m)
            {
                AddBucket(raw, pt.Year, pt.Month, FinanceLedgerRules.ForPayment(pt.Method, CollectionClass.Ordinary, ctx.Resolved), remainder);
            }
        }

        var incomeTotals = await dbContext.BuildingIncomes.AsNoTracking()
            .Where(i => !i.IsDeleted && i.BuildingId == buildingId && i.IncomeDate >= from && i.IncomeDate <= to)
            .GroupBy(i => new { i.IncomeDate.Year, i.IncomeDate.Month, i.Category, i.LedgerCategoryId })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Category, g.Key.LedgerCategoryId, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);
        foreach (var it in incomeTotals)
        {
            AddBucket(raw, it.Year, it.Month,
                FinanceLedgerRules.ForIncome(it.Category, ctx.IncomeTreatment, ctx.Resolved, ctx.IncomeKey(it.LedgerCategoryId, it.Category)), it.Amount);
        }

        var expenseTotals = await dbContext.BuildingExpenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.BuildingId == buildingId && e.ExpenseDate >= from && e.ExpenseDate <= to)
            .GroupBy(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month, e.Category, e.PaidByReserveFund, e.LedgerCategoryId })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Category, g.Key.PaidByReserveFund, g.Key.LedgerCategoryId, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);
        foreach (var et in expenseTotals)
        {
            AddBucket(raw, et.Year, et.Month,
                FinanceLedgerRules.ForExpense(et.Category, et.PaidByReserveFund, ctx.Resolved, ctx.ExpenseKey(et.LedgerCategoryId, et.Category)), et.Amount);
        }

        return raw
            .GroupBy(b => (b.Year, b.Month, b.AccountId, b.RubroKey, b.Direction))
            .Select(g => new LedgerBucket(g.Key.Year, g.Key.Month, g.Key.AccountId, g.Key.RubroKey, g.Key.Direction, g.Sum(b => b.Amount)))
            .ToList();
    }

    /// <summary>Movimientos linea por linea del rango (acotado a <see cref="MaxRowsRangeDays"/> dias), en orden cronologico.</summary>
    public async Task<List<LedgerRow>> GetRowsAsync(LedgerContext ctx, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        from = from < ctx.StartDate ? ctx.StartDate : from;
        var rows = new List<LedgerRow>();
        if (to < from)
        {
            return rows;
        }

        var buildingId = ctx.BuildingId;

        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsReversed && p.Unit!.BuildingId == buildingId && p.PaymentDate >= from && p.PaymentDate <= to)
            .Select(p => new { p.Id, p.PaymentDate, p.Amount, p.Method, p.Reference, p.Notes, UnitCode = p.Unit!.Code })
            .ToListAsync(cancellationToken);

        var allocations = await dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => !a.IsDeleted && a.Charge != null && a.Payment != null
                        && !a.Payment.IsDeleted && !a.Payment.IsReversed
                        && a.Payment.Unit!.BuildingId == buildingId
                        && a.Payment.PaymentDate >= from && a.Payment.PaymentDate <= to)
            .GroupBy(a => new { a.PaymentId, a.Charge!.ChargeType, a.Charge.IsLateFee })
            .Select(g => new { g.Key.PaymentId, g.Key.ChargeType, g.Key.IsLateFee, Amount = g.Sum(x => x.AllocatedAmount) })
            .ToListAsync(cancellationToken);
        var allocationsByPayment = allocations.ToLookup(a => a.PaymentId);

        foreach (var p in payments)
        {
            decimal allocated = 0m;
            var classes = allocationsByPayment[p.Id]
                .GroupBy(a => FinanceLedgerRules.ClassifyCharge(a.ChargeType, a.IsLateFee))
                .Select(g => (Class: g.Key, Amount: g.Sum(a => a.Amount)))
                .ToList();

            foreach (var (cls, amount) in classes)
            {
                allocated += amount;
                AddPaymentRow(rows, ctx, p.Id, p.PaymentDate, p.Method, cls, amount, p.Notes, p.UnitCode, p.Reference);
            }

            var remainder = p.Amount - allocated;
            if (remainder != 0m)
            {
                AddPaymentRow(rows, ctx, p.Id, p.PaymentDate, p.Method, CollectionClass.Ordinary, remainder, p.Notes, p.UnitCode, p.Reference);
            }
        }

        var incomes = await dbContext.BuildingIncomes.AsNoTracking()
            .Where(i => !i.IsDeleted && i.BuildingId == buildingId && i.IncomeDate >= from && i.IncomeDate <= to)
            .Select(i => new { i.Id, i.IncomeDate, i.Amount, i.Category, i.LedgerCategoryId, i.Description })
            .ToListAsync(cancellationToken);
        foreach (var i in incomes)
        {
            var rule = FinanceLedgerRules.ForIncome(i.Category, ctx.IncomeTreatment, ctx.Resolved, ctx.IncomeKey(i.LedgerCategoryId, i.Category));
            if (rule is null)
            {
                continue;
            }

            rows.Add(new LedgerRow(i.IncomeDate, rule.AccountId, rule.RubroKey, rule.Direction, i.Amount, LedgerSourceType.BuildingIncome, i.Id,
                string.IsNullOrWhiteSpace(i.Description) ? "Ingreso del edificio" : i.Description, string.Empty, string.Empty));
        }

        var expenses = await dbContext.BuildingExpenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.BuildingId == buildingId && e.ExpenseDate >= from && e.ExpenseDate <= to)
            .Select(e => new { e.Id, e.ExpenseDate, e.Amount, e.Category, e.PaidByReserveFund, e.LedgerCategoryId, e.Description, e.SupplierName })
            .ToListAsync(cancellationToken);
        foreach (var e in expenses)
        {
            var rule = FinanceLedgerRules.ForExpense(e.Category, e.PaidByReserveFund, ctx.Resolved, ctx.ExpenseKey(e.LedgerCategoryId, e.Category));
            if (rule is null)
            {
                continue;
            }

            var description = string.IsNullOrWhiteSpace(e.Description) ? "Gasto del edificio" : e.Description;
            rows.Add(new LedgerRow(e.ExpenseDate, rule.AccountId, rule.RubroKey, rule.Direction, e.Amount, LedgerSourceType.BuildingExpense, e.Id,
                e.PaidByReserveFund ? description + " (pagado por el fondo de reserva)" : description, e.SupplierName, string.Empty));
        }

        return rows
            .OrderBy(r => r.Date)
            .ThenBy(r => r.SourceType)
            .ThenBy(r => r.Description, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Signed)
            .ToList();
    }

    private static void AddBucket(List<LedgerBucket> target, int year, int month, LedgerClassification? rule, decimal amount)
    {
        if (rule is null || amount == 0m)
        {
            return;
        }

        target.Add(new LedgerBucket(year, month, rule.AccountId, rule.RubroKey, rule.Direction, amount));
    }

    private static void AddPaymentRow(
        List<LedgerRow> rows, LedgerContext ctx, Guid paymentId, DateOnly date, PaymentMethod method, CollectionClass cls,
        decimal amount, string notes, string unitCode, string reference)
    {
        var rule = FinanceLedgerRules.ForPayment(method, cls, ctx.Resolved);
        var label = cls switch
        {
            CollectionClass.ReserveFund => "Aporte al fondo de reserva",
            CollectionClass.Extraordinary => "Aporte extraordinario",
            CollectionClass.IndividualAdjustment => "Cargos individuales y ajustes",
            CollectionClass.LateFee => "Intereses por mora",
            _ => "Cobro de expensas"
        };
        var description = string.IsNullOrWhiteSpace(notes) ? label : $"{label} — {notes.Trim()}";

        rows.Add(new LedgerRow(date, rule.AccountId, rule.RubroKey, rule.Direction, amount, LedgerSourceType.OwnerPayment, paymentId,
            description, string.IsNullOrWhiteSpace(unitCode) ? string.Empty : $"Unidad {unitCode}", reference ?? string.Empty));
    }
}
