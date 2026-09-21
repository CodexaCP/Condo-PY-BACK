namespace Condo.Api.Documents;

/// <summary>Importe en letras (espanol) para el campo "SON:" de la factura.</summary>
public static class NumberToWordsEs
{
    private static readonly string[] Units =
    [
        "CERO", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ",
        "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE",
        "VEINTE", "VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS",
        "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"
    ];

    private static readonly string[] Tens =
        ["", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"];

    private static readonly string[] Hundreds =
        ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"];

    public static string Guaranies(decimal amount)
    {
        var value = (long)decimal.Round(decimal.Abs(amount), 0, MidpointRounding.AwayFromZero);
        return $"GUARANÍES {Convert(value)}.";
    }

    private static string Convert(long n)
    {
        if (n == 0) return "CERO";

        var millions = n / 1_000_000;
        var rest = n % 1_000_000;
        var parts = new List<string>();

        if (millions > 0)
            parts.Add(millions == 1 ? "UN MILLÓN" : $"{BelowMillion(millions, true)} MILLONES");
        if (rest > 0)
            parts.Add(BelowMillion(rest, false));

        return string.Join(" ", parts);
    }

    private static string BelowMillion(long n, bool apocope)
    {
        var thousands = n / 1000;
        var rest = n % 1000;
        var parts = new List<string>();

        if (thousands > 0)
            parts.Add(thousands == 1 ? "MIL" : $"{BelowThousand((int)thousands, true)} MIL");
        if (rest > 0)
            parts.Add(BelowThousand((int)rest, apocope));

        return string.Join(" ", parts);
    }

    private static string BelowThousand(int n, bool apocope)
    {
        if (n == 100) return "CIEN";

        var words = new List<string>();
        var hundreds = n / 100;
        var rest = n % 100;

        if (hundreds > 0) words.Add(Hundreds[hundreds]);

        if (rest > 0)
        {
            string tail;
            if (rest < 30)
            {
                tail = Units[rest];
                if (apocope && rest == 1) tail = "UN";
                if (apocope && rest == 21) tail = "VEINTIÚN";
            }
            else
            {
                var ten = rest / 10;
                var unit = rest % 10;
                tail = unit == 0 ? Tens[ten] : $"{Tens[ten]} Y {(apocope && unit == 1 ? "UN" : Units[unit])}";
            }
            words.Add(tail);
        }

        return string.Join(" ", words);
    }
}
