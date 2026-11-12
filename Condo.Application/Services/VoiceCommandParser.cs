using System.Globalization;
using System.Text;
using Condo.Application.Models;

namespace Condo.Application.Services;

/// <summary>
/// Analizador de frases por reglas propias (decisión de Tony: sin IA ni servicios externos).
/// Normaliza el texto (minúsculas, sin tildes ni puntuación) y detecta la intención
/// por palabras clave: morosos con antigüedad, período vacío o período igual al anterior.
/// </summary>
public static class VoiceCommandParser
{
    private static readonly Dictionary<string, int> NumberWords = new()
    {
        ["un"] = 1, ["uno"] = 1, ["una"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5,
        ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10, ["once"] = 11, ["doce"] = 12,
        ["quince"] = 15, ["veinte"] = 20, ["treinta"] = 30, ["cuarenta"] = 40, ["cincuenta"] = 50,
        ["sesenta"] = 60, ["noventa"] = 90, ["cien"] = 100
    };

    private static readonly string[] DelinquentPrefixes = ["moros", "deudor", "atrasad", "adeud", "impag"];
    private static readonly string[] ClonePrefixes = ["igual", "identic", "copi", "clon", "mism", "duplic", "anterior", "pasado"];
    private static readonly string[] EmptyWords = ["vacio", "vacia", "blanco", "limpio", "limpia"];
    private static readonly string[] SinObjects = ["gastos", "gasto", "movimientos", "nada", "cargar"];

    public static ParsedVoiceCommand Parse(string? text, DateOnly today)
    {
        var tokens = Tokenize(text);

        if (tokens.Any(t => DelinquentPrefixes.Any(t.StartsWith) || t == "debe" || t == "deben"))
        {
            for (var i = 1; i < tokens.Count; i++)
            {
                var unit = ParseUnit(tokens[i]);
                if (unit is null || !TryParseNumber(tokens[i - 1], out var n) || n < 1 || n > 1000)
                {
                    continue;
                }

                var cutoff = unit.Value switch
                {
                    OverdueAgeUnit.Days => today.AddDays(-n),
                    OverdueAgeUnit.Weeks => today.AddDays(-7 * n),
                    OverdueAgeUnit.Months => today.AddMonths(-n),
                    _ => today.AddYears(-n)
                };
                return new ParsedVoiceCommand
                {
                    Intent = VoiceCommandIntent.ListDelinquents,
                    AgeAmount = n,
                    AgeUnit = unit,
                    DueOnOrBefore = cutoff
                };
            }

            return new ParsedVoiceCommand { Intent = VoiceCommandIntent.ListDelinquents };
        }

        if (tokens.Any(t => t.StartsWith("period") || t.StartsWith("liquidacion") || t == "mes"))
        {
            var isEmpty = tokens.Any(t => EmptyWords.Contains(t));
            if (!isEmpty)
            {
                for (var i = 0; i < tokens.Count - 1; i++)
                {
                    if (tokens[i] == "sin" && SinObjects.Contains(tokens[i + 1]))
                    {
                        isEmpty = true;
                        break;
                    }
                }
            }

            if (isEmpty)
            {
                return new ParsedVoiceCommand { Intent = VoiceCommandIntent.CreateEmptyPeriod };
            }

            if (tokens.Any(t => ClonePrefixes.Any(t.StartsWith)))
            {
                return new ParsedVoiceCommand { Intent = VoiceCommandIntent.ClonePreviousPeriod };
            }

            return new ParsedVoiceCommand { Intent = VoiceCommandIntent.Unknown, MentionsPeriod = true };
        }

        return new ParsedVoiceCommand { Intent = VoiceCommandIntent.Unknown, MentionsPeriod = false };
    }

    private static List<string> Tokenize(string? text)
    {
        var decomposed = (text ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static OverdueAgeUnit? ParseUnit(string token) => token switch
    {
        "dia" or "dias" => OverdueAgeUnit.Days,
        "semana" or "semanas" => OverdueAgeUnit.Weeks,
        "mes" or "meses" => OverdueAgeUnit.Months,
        "ano" or "anos" => OverdueAgeUnit.Years,
        _ => null
    };

    private static bool TryParseNumber(string token, out int n) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || NumberWords.TryGetValue(token, out n);
}
