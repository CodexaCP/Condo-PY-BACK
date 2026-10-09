using Condo.Application.Models;

namespace Condo.Application.Services;

/// <summary>Una cuenta del plan de cuentas del edificio, tal como la necesita un asiento.</summary>
public sealed record AccountingPlanAccount(Guid Id, string Code, string? ExternalCode, string Name);

public sealed class AccountingBuildInput
{
    // Cuenta del plan de cada cuenta financiera (banco, caja, fondo).
    public required IReadOnlyDictionary<Guid, AccountingPlanAccount> FinancialPlan { get; init; }
    public required IReadOnlyDictionary<Guid, string> FinancialNames { get; init; }
    // Cuenta del plan para el IVA credito de las compras.
    public AccountingPlanAccount? VatCredit { get; init; }
    // Cuenta del plan de un rubro del libro (null si el plan ya no lo tiene) y su nombre para avisar.
    public required Func<string, AccountingPlanAccount?> RubroPlan { get; init; }
    public Func<string, string>? RubroLabel { get; init; }
    // Tasa de IVA de los gastos (y de las notas de credito, la del gasto que corrigen) por origen del renglon.
    public required IReadOnlyDictionary<(LedgerSourceType Type, Guid Id), decimal> VatRates { get; init; }
}

public sealed record AccountingBuildResult(
    IReadOnlyList<AccountingEntryDto> Entries, IReadOnlyList<AccountingTotalDto> Totals, IReadOnlyList<string> Pending);

/// <summary>
/// Asientos sugeridos de la partida doble a partir de los renglones del libro (criterio percibido: un asiento por movimiento de caja, sin
/// cuentas por cobrar ni por pagar). Un cobro o gasto que el libro parte en varios renglones de la misma cuenta es un solo asiento. Son
/// sugeridos, no asentados: no cambian ningun saldo. Cada asiento cuadra por construccion (la contrapartida de cada renglon es lo que lo
/// cierra), y si falta la cuenta del plan de algun renglon el asiento se marca incompleto en vez de inventar una cuenta.
/// </summary>
public static class AccountingEntryBuilder
{
    private const int MaxDescriptionLength = 300;

    private sealed class LineAcc
    {
        public required string Kind { get; init; }
        public AccountingPlanAccount? Plan { get; init; }
        public required string Label { get; init; }
        public decimal Net { get; set; }
    }

    public static AccountingBuildResult Build(IEnumerable<LedgerRow> rows, AccountingBuildInput input)
    {
        var groups = rows
            .GroupBy(r => (r.SourceType, r.SourceId, r.Date, r.AccountId))
            .OrderBy(g => g.Key.Date)
            .ThenBy(g => g.Key.SourceType)
            .ThenBy(g => g.First().Description, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.SourceId)
            .ToList();

        var entries = new List<AccountingEntryDto>();
        var totals = new Dictionary<string, AccountingTotalDto>();
        var pending = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in groups)
        {
            var lines = new Dictionary<string, LineAcc>();
            foreach (var row in group)
            {
                AddRow(lines, row, input);
            }

            var dtoLines = lines.Values
                .Where(l => l.Net != 0m)
                .Select(l => new AccountingLineDto
                {
                    CategoryId = l.Plan?.Id,
                    Code = l.Plan?.Code ?? string.Empty,
                    ExternalCode = l.Plan?.ExternalCode,
                    Name = l.Plan?.Name ?? l.Label,
                    Debit = l.Net > 0 ? l.Net : 0m,
                    Credit = l.Net < 0 ? -l.Net : 0m,
                    Kind = l.Kind,
                    Missing = l.Plan is null
                })
                .OrderByDescending(l => l.Debit > 0)
                .ThenBy(l => l.Code, StringComparer.Ordinal)
                .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (dtoLines.Count == 0)
            {
                continue;
            }

            var issues = dtoLines.Where(l => l.Missing).Select(l => $"Falta la cuenta del plan: {l.Name}.").Distinct().ToList();
            foreach (var line in dtoLines.Where(l => l.Missing)) pending.Add(line.Name);

            var first = group.First();
            var descriptions = group.Select(r => r.Description).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToList();
            var description = string.Join(" · ", descriptions);
            entries.Add(new AccountingEntryDto
            {
                Number = entries.Count + 1,
                Date = group.Key.Date,
                Description = description.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
                ThirdParty = group.Select(r => r.ThirdParty).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty,
                Reference = group.Select(r => r.Reference).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty,
                SourceType = first.SourceType,
                SourceId = first.SourceId,
                Lines = dtoLines,
                Debit = dtoLines.Sum(l => l.Debit),
                Credit = dtoLines.Sum(l => l.Credit),
                IsComplete = issues.Count == 0,
                Issues = issues
            });

            foreach (var line in dtoLines)
            {
                var key = line.CategoryId.HasValue ? $"P:{line.CategoryId}" : $"M:{line.Name}";
                if (!totals.TryGetValue(key, out var total))
                {
                    totals[key] = total = new AccountingTotalDto
                    {
                        CategoryId = line.CategoryId, Code = line.Code, ExternalCode = line.ExternalCode, Name = line.Name, Missing = line.Missing
                    };
                }

                total.Debit += line.Debit;
                total.Credit += line.Credit;
            }
        }

        return new AccountingBuildResult(
            entries,
            totals.Values.OrderBy(t => t.Missing).ThenBy(t => t.Code, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            pending.ToList());
    }

    // Un renglon del libro: lo que entra o sale de la cuenta financiera, el IVA credito de la compra (si tiene tasa) y, como contrapartida,
    // el rubro por el resto. Los importes van con signo "debe menos haber": la suma de los tres renglones es siempre cero.
    private static void AddRow(Dictionary<string, LineAcc> lines, LedgerRow row, AccountingBuildInput input)
    {
        var cash = row.Signed;

        AccountingPlanAccount? financial = null;
        string financialLabel;
        if (row.AccountId is { } accountId)
        {
            input.FinancialPlan.TryGetValue(accountId, out financial);
            var name = input.FinancialNames.TryGetValue(accountId, out var n) ? n : "cuenta financiera";
            financialLabel = $"Cuenta financiera: {name}";
        }
        else
        {
            financialLabel = "Movimiento sin cuenta financiera";
        }

        Add(lines, "Financial", financial, financialLabel, cash);

        var vat = 0m;
        if (row.Direction == LedgerDirection.Out && input.VatRates.TryGetValue((row.SourceType, row.SourceId), out var rate) && rate > 0m)
        {
            vat = VatMath.VatOf(row.Amount, rate) ?? 0m;
            if (vat != 0m)
            {
                Add(lines, "VatCredit", input.VatCredit, "IVA crédito fiscal", vat);
            }
        }

        var rubro = input.RubroPlan(row.RubroKey);
        var rubroLabel = $"Rubro: {input.RubroLabel?.Invoke(row.RubroKey) ?? row.RubroKey}";
        Add(lines, "Rubro", rubro, rubroLabel, -cash - vat);
    }

    private static void Add(Dictionary<string, LineAcc> lines, string kind, AccountingPlanAccount? plan, string label, decimal net)
    {
        var key = plan is null ? $"M:{label}" : $"P:{plan.Id}";
        if (!lines.TryGetValue(key, out var line))
        {
            lines[key] = line = new LineAcc { Kind = kind, Plan = plan, Label = label };
        }

        line.Net += net;
    }
}
