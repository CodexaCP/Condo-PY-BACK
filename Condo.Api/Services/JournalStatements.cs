using Condo.Application.Models;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

/// <summary>Una cuenta de resultado (ingreso o gasto) con su saldo (debe menos haber) en un conjunto de renglones.</summary>
public sealed record ResultAccountBalance(string Code, string? ExternalCode, string Name, Guid? CategoryId, LedgerCategoryType Type, decimal Balance);

/// <summary>
/// Calculos puros del cierre del ejercicio y de los estados (balance general y estado de resultados) a partir de lo asentado. La clase de cada
/// cuenta sale del renglon (se guarda al asentar); en lo asentado antes, de la cuenta del plan por su id y, si ya no esta, del primer digito
/// del codigo del plan generico (1 activo, 2 pasivo, 3 fondos, 4 ingresos, 5 gastos).
/// </summary>
public static class JournalStatementsCalculator
{
    public static LedgerCategoryType? TypeOf(BookLine line, IReadOnlyDictionary<Guid, LedgerCategoryType> plan)
    {
        if (line.AccountType.HasValue) return line.AccountType;
        if (line.LedgerCategoryId is { } id && plan.TryGetValue(id, out var type)) return type;

        return line.Code.Length > 0 ? line.Code[0] switch
        {
            '1' => LedgerCategoryType.Asset,
            '2' => LedgerCategoryType.Liability,
            '3' => LedgerCategoryType.Fund,
            '4' => LedgerCategoryType.Income,
            '5' => LedgerCategoryType.Expense,
            _ => null
        } : null;
    }

    /// <summary>Cuentas de ingresos y gastos con saldo en los renglones dados (en general los del ejercicio), ordenadas por codigo.</summary>
    public static List<ResultAccountBalance> ResultAccounts(IEnumerable<BookLine> lines, IReadOnlyDictionary<Guid, LedgerCategoryType> plan)
    {
        var result = new List<ResultAccountBalance>();
        foreach (var group in lines.GroupBy(l => l.Code))
        {
            var latest = group.OrderBy(l => l.Date).ThenBy(l => l.FiscalYear).ThenBy(l => l.Number).ThenBy(l => l.LineNo).Last();
            var type = group.Select(l => TypeOf(l, plan)).FirstOrDefault(t => t.HasValue);
            if (type is not (LedgerCategoryType.Income or LedgerCategoryType.Expense)) continue;

            var balance = group.Sum(l => l.Debit - l.Credit);
            if (balance == 0m) continue;
            result.Add(new ResultAccountBalance(group.Key, latest.ExternalCode, latest.Name, latest.LedgerCategoryId, type.Value, balance));
        }

        return result.OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Balance general a una fecha (todo lo asentado hasta ella) y estado de resultados del ejercicio (lo asentado desde el inicio, sin los asientos de
    /// cierre). El resultado que todavia esta en las cuentas de ingresos y gastos (ejercicio sin cerrar) se suma al patrimonio para que el balance cuadre.
    /// </summary>
    public static JournalStatementsDto Build(
        IReadOnlyList<BookLine> lines, IReadOnlyDictionary<Guid, LedgerCategoryType> plan, DateOnly from, DateOnly asOf)
    {
        var upTo = lines.Where(l => l.Date <= asOf).ToList();
        var assets = new List<StatementAccountDto>();
        var liabilities = new List<StatementAccountDto>();
        var equity = new List<StatementAccountDto>();
        decimal unclassified = 0m;

        foreach (var group in upTo.GroupBy(l => l.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var latest = group.OrderBy(l => l.Date).ThenBy(l => l.FiscalYear).ThenBy(l => l.Number).ThenBy(l => l.LineNo).Last();
            var type = group.Select(l => TypeOf(l, plan)).FirstOrDefault(t => t.HasValue);
            var net = group.Sum(l => l.Debit - l.Credit);
            if (net == 0m) continue;

            StatementAccountDto Account(decimal amount) =>
                new() { Code = group.Key, ExternalCode = latest.ExternalCode, Name = latest.Name, Amount = amount };

            switch (type)
            {
                case LedgerCategoryType.Asset: assets.Add(Account(net)); break;
                case LedgerCategoryType.Liability: liabilities.Add(Account(-net)); break;
                case LedgerCategoryType.Fund: equity.Add(Account(-net)); break;
                case LedgerCategoryType.Income:
                case LedgerCategoryType.Expense: break;
                default: unclassified += net; break;
            }
        }

        // Resultado todavia en las cuentas de ingresos y gastos (de cualquier ejercicio sin cerrar), para que el balance cuadre.
        var pending = upTo
            .Where(l => TypeOf(l, plan) is LedgerCategoryType.Income or LedgerCategoryType.Expense)
            .Sum(l => l.Credit - l.Debit);

        // Estado de resultados del ejercicio: sin los asientos de cierre (que lo saldan).
        var inYear = upTo.Where(l => l.Date >= from && !l.IsClosing).ToList();
        var results = ResultAccounts(inYear, plan);
        var income = results.Where(r => r.Type == LedgerCategoryType.Income)
            .Select(r => new StatementAccountDto { Code = r.Code, ExternalCode = r.ExternalCode, Name = r.Name, Amount = -r.Balance }).ToList();
        var expenses = results.Where(r => r.Type == LedgerCategoryType.Expense)
            .Select(r => new StatementAccountDto { Code = r.Code, ExternalCode = r.ExternalCode, Name = r.Name, Amount = r.Balance }).ToList();

        var totalAssets = assets.Sum(a => a.Amount);
        var totalLiabilities = liabilities.Sum(a => a.Amount);
        var totalEquity = equity.Sum(a => a.Amount);
        var difference = totalAssets - (totalLiabilities + totalEquity + pending) + unclassified;
        var closed = upTo.Any(l => l.IsClosing && l.Date >= from) && pending == 0m;

        return new JournalStatementsDto
        {
            From = from,
            AsOf = asOf,
            YearClosed = closed,
            Assets = assets,
            Liabilities = liabilities,
            Equity = equity,
            Income = income,
            Expenses = expenses,
            TotalAssets = totalAssets,
            TotalLiabilities = totalLiabilities,
            TotalEquity = totalEquity,
            TotalIncome = income.Sum(a => a.Amount),
            TotalExpenses = expenses.Sum(a => a.Amount),
            Result = income.Sum(a => a.Amount) - expenses.Sum(a => a.Amount),
            PendingResult = pending,
            // Con renglones de clase desconocida el estado esta incompleto: no se da por cuadrado.
            IsBalanced = difference == 0m && unclassified == 0m,
            Difference = difference,
            UnclassifiedNet = unclassified
        };
    }
}
