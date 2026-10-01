using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>
/// Arma los reportes del libro (saldos, flujo, tablero, listado de movimientos) a partir de los movimientos ya clasificados.
/// Es logica pura, sin acceso a datos, para poder probarla con numeros de ejemplo.
/// </summary>
public static class FinanceReportBuilder
{
    private static decimal Signed(LedgerBucket b) => b.Direction == LedgerDirection.In ? b.Amount : -b.Amount;

    // ── Saldos ────────────────────────────────────────────────────────────────

    public static FinanceBalancesDto BuildBalances(LedgerContext ctx, IReadOnlyCollection<LedgerBucket> buckets, DateOnly asOf, Guid? defaultAccountId)
    {
        var accounts = ctx.Accounts
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a =>
            {
                var inflows = buckets.Where(b => b.AccountId == a.Id && b.Direction == LedgerDirection.In).Sum(b => b.Amount);
                var outflows = buckets.Where(b => b.AccountId == a.Id && b.Direction == LedgerDirection.Out).Sum(b => b.Amount);
                return new FinanceAccountBalanceDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Type = a.Type,
                    IsActive = a.IsActive,
                    OpeningBalance = a.OpeningBalance,
                    Inflows = inflows,
                    Outflows = outflows,
                    Balance = a.OpeningBalance + inflows - outflows
                };
            })
            .ToList();

        var unassigned = buckets.Where(b => b.AccountId == null).Sum(Signed);
        var warnings = new List<string>();
        if (unassigned != 0)
        {
            warnings.Add("Hay movimientos sin cuenta asignada. Elegí la cuenta por defecto en Configuración → Cuentas para que los saldos reflejen todo.");
        }

        return new FinanceBalancesDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            FinanceStartDate = ctx.StartDate,
            AsOf = asOf,
            Accounts = accounts,
            UnassignedNet = unassigned,
            TotalBalance = accounts.Sum(a => a.Balance) + unassigned,
            CashBalance = accounts.Where(a => a.Type == FinancialAccountType.Cash).Sum(a => a.Balance),
            BankBalance = accounts.Where(a => a.Type == FinancialAccountType.Bank).Sum(a => a.Balance),
            ReserveFundBalance = accounts.Where(a => a.Type == FinancialAccountType.ReserveFund).Sum(a => a.Balance),
            DefaultAccountId = defaultAccountId,
            Warnings = warnings
        };
    }

    // ── Flujo ─────────────────────────────────────────────────────────────────

    public static FinanceFlowDto Flow(IEnumerable<LedgerBucket> buckets)
    {
        var list = buckets as IReadOnlyCollection<LedgerBucket> ?? buckets.ToList();
        var inflow = list.Where(b => b.Direction == LedgerDirection.In).Sum(b => b.Amount);
        var outflow = list.Where(b => b.Direction == LedgerDirection.Out).Sum(b => b.Amount);
        return new FinanceFlowDto { In = inflow, Out = outflow, Net = inflow - outflow };
    }

    public static List<FinanceRubroAmountDto> RubroAmounts(LedgerContext ctx, IEnumerable<LedgerBucket> buckets, LedgerDirection direction) =>
        buckets
            .Where(b => b.Direction == direction)
            .GroupBy(b => b.RubroKey)
            .Select(g =>
            {
                var category = ctx.CategoryFor(g.Key);
                return new FinanceRubroAmountDto
                {
                    CategoryId = category?.Id,
                    Code = category?.Code ?? string.Empty,
                    Name = category?.Name ?? g.Key,
                    Amount = g.Sum(b => b.Amount)
                };
            })
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToList();

    // Ultimos `months` meses hasta el indicado, sin pasar antes del mes de arranque. El saldo de cierre incluye lo que no tiene cuenta.
    public static List<FinanceMonthPointDto> Series(LedgerContext ctx, IReadOnlyCollection<LedgerBucket> buckets, int endYear, int endMonth, int months = 12)
    {
        var end = new DateOnly(endYear, endMonth, 1);
        var startMonth = new DateOnly(ctx.StartDate.Year, ctx.StartDate.Month, 1);
        var result = new List<FinanceMonthPointDto>();

        for (var i = months - 1; i >= 0; i--)
        {
            var d = end.AddMonths(-i);
            if (d < startMonth)
            {
                continue;
            }

            var inMonth = buckets.Where(b => b.Year == d.Year && b.Month == d.Month).ToList();
            var upTo = buckets.Where(b => b.Year < d.Year || (b.Year == d.Year && b.Month <= d.Month)).Sum(Signed);
            var flow = Flow(inMonth);
            result.Add(new FinanceMonthPointDto
            {
                Year = d.Year,
                Month = d.Month,
                In = flow.In,
                Out = flow.Out,
                Net = flow.Net,
                EndBalance = ctx.TotalOpening + upTo
            });
        }

        return result;
    }

    // ── Flujo de caja del ejercicio ───────────────────────────────────────────

    public static FinanceCashFlowDto CashFlow(LedgerContext ctx, IReadOnlyCollection<LedgerBucket> buckets, int fiscalYear, DateOnly asOf)
    {
        var months = FinancePeriods.FiscalMonths(fiscalYear, ctx.FiscalYearStartMonth);
        var (start, end) = FinancePeriods.FiscalYearRange(fiscalYear, ctx.FiscalYearStartMonth);
        var first = months[0];

        int Index(LedgerBucket b) => months.IndexOf((b.Year, b.Month));

        var before = buckets.Where(b => b.Year < first.Year || (b.Year == first.Year && b.Month < first.Month)).Sum(Signed);
        var inYear = buckets.Where(b => Index(b) >= 0).ToList();

        List<FinanceCashFlowLineDto> Lines(LedgerDirection direction) =>
            inYear
                .Where(b => b.Direction == direction)
                .GroupBy(b => b.RubroKey)
                .Select(g =>
                {
                    var category = ctx.CategoryFor(g.Key);
                    var group = category?.ParentId is Guid pid && ctx.ById.TryGetValue(pid, out var p) ? p : null;
                    var amounts = new decimal[12];
                    foreach (var b in g)
                    {
                        amounts[Index(b)] += b.Amount;
                    }

                    return new FinanceCashFlowLineDto
                    {
                        CategoryId = category?.Id,
                        Code = category?.Code ?? string.Empty,
                        Name = category?.Name ?? g.Key,
                        GroupCode = group?.Code ?? string.Empty,
                        GroupName = group?.Name ?? string.Empty,
                        Direction = direction,
                        Amounts = amounts,
                        Total = amounts.Sum()
                    };
                })
                .OrderBy(l => l.Code, StringComparer.Ordinal)
                .ToList();

        var totalIn = new decimal[12];
        var totalOut = new decimal[12];
        foreach (var b in inYear)
        {
            if (b.Direction == LedgerDirection.In) totalIn[Index(b)] += b.Amount; else totalOut[Index(b)] += b.Amount;
        }

        var net = totalIn.Zip(totalOut, (i, o) => i - o).ToArray();
        var opening = ctx.TotalOpening + before;
        var closing = new decimal[12];
        var running = opening;
        for (var i = 0; i < 12; i++)
        {
            running += net[i];
            closing[i] = running;
        }

        return new FinanceCashFlowDto
        {
            BuildingId = ctx.BuildingId,
            FinanceStartDate = ctx.StartDate,
            FiscalYear = fiscalYear,
            FiscalYearStart = start,
            FiscalYearEnd = end,
            AsOf = asOf,
            Months = months.Select(m => new FinanceMonthRefDto { Year = m.Year, Month = m.Month }).ToList(),
            InLines = Lines(LedgerDirection.In),
            OutLines = Lines(LedgerDirection.Out),
            TotalIn = totalIn,
            TotalOut = totalOut,
            Net = net,
            OpeningBalance = opening,
            ClosingBalance = closing
        };
    }

    // ── Fondo de reserva ──────────────────────────────────────────────────────

    // Cuenta del fondo: la activa o, si no hay, cualquiera de ese tipo.
    public static FinancialAccount? FundAccountOf(LedgerContext ctx) =>
        ctx.Accounts.FirstOrDefault(a => a.Type == FinancialAccountType.ReserveFund && a.IsActive)
        ?? ctx.Accounts.FirstOrDefault(a => a.Type == FinancialAccountType.ReserveFund);

    /// <summary>
    /// Libro del fondo de reserva: aportes (cobrados a los propietarios y, si el edificio los manda al fondo, los ingresos propios) y
    /// usos (gastos pagados por el fondo), mes a mes desde la fecha de arranque, con el saldo de apertura de su cuenta.
    /// <paramref name="buckets"/> debe cubrir desde la fecha de arranque hasta <paramref name="asOf"/>.
    /// </summary>
    public static FinanceReserveFundDto ReserveFund(
        LedgerContext ctx, IReadOnlyCollection<LedgerBucket> buckets, DateOnly asOf, decimal? reservePercentage, FinanceMovementsPageDto movements)
    {
        var fund = FundAccountOf(ctx);
        var dto = new FinanceReserveFundDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            HasFundAccount = fund is not null,
            AccountId = fund?.Id,
            AccountName = fund?.Name ?? string.Empty,
            FinanceStartDate = ctx.StartDate,
            AsOf = asOf,
            ReserveFundPercentage = reservePercentage,
            Movements = movements
        };

        if (fund is null)
        {
            return dto;
        }

        var own = buckets.Where(b => b.AccountId == fund.Id).ToList();
        var months = new List<FinanceReserveMonthDto>();
        var running = fund.OpeningBalance;
        var cursor = new DateOnly(ctx.StartDate.Year, ctx.StartDate.Month, 1);
        var last = new DateOnly(asOf.Year, asOf.Month, 1);

        while (cursor <= last)
        {
            var inMonth = own.Where(b => b.Year == cursor.Year && b.Month == cursor.Month).ToList();
            var contributions = inMonth.Where(b => b.Direction == LedgerDirection.In).Sum(b => b.Amount);
            var uses = inMonth.Where(b => b.Direction == LedgerDirection.Out).Sum(b => b.Amount);
            months.Add(new FinanceReserveMonthDto
            {
                Year = cursor.Year,
                Month = cursor.Month,
                Opening = running,
                Contributions = contributions,
                Uses = uses,
                Closing = running + contributions - uses
            });
            running += contributions - uses;
            cursor = cursor.AddMonths(1);
        }

        dto.OpeningBalance = fund.OpeningBalance;
        dto.Contributions = own.Where(b => b.Direction == LedgerDirection.In).Sum(b => b.Amount);
        dto.Uses = own.Where(b => b.Direction == LedgerDirection.Out).Sum(b => b.Amount);
        dto.Balance = fund.OpeningBalance + dto.Contributions - dto.Uses;
        dto.Months = months;
        return dto;
    }

    public static FinanceReserveSummaryDto ReserveSummary(LedgerContext ctx, IReadOnlyCollection<LedgerBucket> buckets, int year, int month)
    {
        var fund = FundAccountOf(ctx);
        if (fund is null)
        {
            return new FinanceReserveSummaryDto();
        }

        var own = buckets.Where(b => b.AccountId == fund.Id).ToList();
        var inMonth = own.Where(b => b.Year == year && b.Month == month).ToList();
        return new FinanceReserveSummaryDto
        {
            HasFundAccount = true,
            Balance = fund.OpeningBalance + own.Where(b => b.Direction == LedgerDirection.In).Sum(b => b.Amount) - own.Where(b => b.Direction == LedgerDirection.Out).Sum(b => b.Amount),
            MonthContributions = inMonth.Where(b => b.Direction == LedgerDirection.In).Sum(b => b.Amount),
            MonthUses = inMonth.Where(b => b.Direction == LedgerDirection.Out).Sum(b => b.Amount)
        };
    }

    // ── Listado de movimientos ────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="rows"/> son todos los movimientos del rango (ya acotado a la fecha de arranque), en orden cronologico.
    /// <paramref name="openingForScope"/> es el saldo del alcance filtrado (una cuenta o todas) al comienzo del rango.
    /// </summary>
    public static FinanceMovementsPageDto MovementsPage(
        LedgerContext ctx,
        IReadOnlyList<LedgerRow> rows,
        DateOnly from,
        DateOnly to,
        Guid? accountId,
        bool onlyUnassigned,
        Guid? categoryId,
        LedgerDirection? direction,
        bool descending,
        int page,
        int pageSize,
        decimal? openingForScope)
    {
        IEnumerable<LedgerRow> filtered = rows;

        if (accountId.HasValue)
        {
            filtered = filtered.Where(r => r.AccountId == accountId.Value);
        }
        else if (onlyUnassigned)
        {
            filtered = filtered.Where(r => r.AccountId == null);
        }

        if (categoryId.HasValue)
        {
            // Un rubro principal incluye a sus subrubros.
            var ids = ctx.Categories.Where(c => c.Id == categoryId.Value || c.ParentId == categoryId.Value).Select(c => c.Id).ToHashSet();
            var keys = ctx.Categories.Where(c => ids.Contains(c.Id) && c.SystemKey != null).Select(c => c.SystemKey!).ToHashSet();
            filtered = filtered.Where(r => keys.Contains(r.RubroKey));
        }

        if (direction.HasValue)
        {
            filtered = filtered.Where(r => r.Direction == direction.Value);
        }

        var ordered = filtered.ToList();

        // El saldo corrido solo tiene sentido sobre el alcance completo (sin filtrar por rubro ni por sentido).
        var hasRunning = openingForScope.HasValue && !categoryId.HasValue && !direction.HasValue;
        var running = openingForScope ?? 0m;

        var dtos = new List<FinanceMovementDto>(ordered.Count);
        foreach (var r in ordered)
        {
            var category = ctx.CategoryFor(r.RubroKey);
            running += r.Signed;
            dtos.Add(new FinanceMovementDto
            {
                Date = r.Date,
                AccountId = r.AccountId,
                AccountName = r.AccountId.HasValue ? ctx.Accounts.FirstOrDefault(a => a.Id == r.AccountId.Value)?.Name ?? string.Empty : "Sin cuenta asignada",
                CategoryId = category?.Id,
                CategoryCode = category?.Code ?? string.Empty,
                CategoryName = category?.Name ?? r.RubroKey,
                Direction = r.Direction,
                Amount = r.Amount,
                SignedAmount = r.Signed,
                Description = r.Description,
                ThirdParty = r.ThirdParty,
                Reference = r.Reference,
                SourceType = r.SourceType,
                SourceId = r.SourceId,
                RunningBalance = hasRunning ? running : null
            });
        }

        var totalIn = ordered.Where(r => r.Direction == LedgerDirection.In).Sum(r => r.Amount);
        var totalOut = ordered.Where(r => r.Direction == LedgerDirection.Out).Sum(r => r.Amount);

        if (descending)
        {
            dtos.Reverse();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        return new FinanceMovementsPageDto
        {
            Items = dtos.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = dtos.Count,
            Page = page,
            PageSize = pageSize,
            From = from,
            To = to,
            TotalIn = totalIn,
            TotalOut = totalOut,
            OpeningBalance = hasRunning ? openingForScope : null,
            ClosingBalance = hasRunning ? openingForScope + totalIn - totalOut : null
        };
    }
}
