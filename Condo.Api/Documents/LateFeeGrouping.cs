using System.Text.RegularExpressions;

namespace Condo.Api.Documents;

/// <summary>
/// Las moras automaticas ("Mora 0.66% (diario) #94 — Mayo 2026") se muestran en una sola linea por
/// porcentaje: "Mora 0.66% (diario) por un total de 12 días" con la suma. El resto de conceptos va tal cual.
/// </summary>
public static class LateFeeGrouping
{
    private static readonly Regex Pattern =
        new(@"^Mora\s+(?<rate>[\d.,]+%)\s*\((?<freq>[^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<T> Collapse<T>(
        IEnumerable<T> items,
        Func<T, string?> concept,
        Func<T, decimal> amount,
        Func<T, decimal, string, T> makeGroup)
    {
        var result = new List<T>();
        var groups = new Dictionary<string, (int Index, T First, decimal Sum, int Count, string Rate, string Freq)>();

        foreach (var item in items)
        {
            var match = Pattern.Match(concept(item) ?? string.Empty);
            if (!match.Success)
            {
                result.Add(item);
                continue;
            }

            var rate = match.Groups["rate"].Value;
            var freq = match.Groups["freq"].Value.Trim();
            var key = $"{rate}|{freq}".ToLowerInvariant();

            if (groups.TryGetValue(key, out var group))
            {
                groups[key] = (group.Index, group.First, group.Sum + amount(item), group.Count + 1, rate, freq);
            }
            else
            {
                groups[key] = (result.Count, item, amount(item), 1, rate, freq);
                result.Add(item);
            }
        }

        foreach (var group in groups.Values)
        {
            var label = $"Mora {group.Rate} ({group.Freq}) por un total de {group.Count} {IntervalLabel(group.Freq, group.Count)}";
            result[group.Index] = makeGroup(group.First, group.Sum, label);
        }

        return result;
    }

    private static string IntervalLabel(string frequency, int count) => frequency.ToLowerInvariant() switch
    {
        "diario" => count == 1 ? "día" : "días",
        "semanal" => count == 1 ? "semana" : "semanas",
        "quincenal" => count == 1 ? "quincena" : "quincenas",
        _ => count == 1 ? "intervalo" : "intervalos"
    };
}
