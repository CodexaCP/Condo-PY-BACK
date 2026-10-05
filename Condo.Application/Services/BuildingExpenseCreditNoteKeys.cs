using System.Globalization;
using System.Text;

namespace Condo.Application.Services;

// Llaves para reconocer una misma nota de credito de proveedor escrita de distintas formas: "001-001-0000123", "001 001 0000123" y
// "0010010000123" son la misma; "Ferretería López S.A." y "FERRETERIA LOPEZ SA" tambien.
public static class BuildingExpenseCreditNoteKeys
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }
}
