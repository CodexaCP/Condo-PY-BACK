namespace Condo.Application.Localization;

/// <summary>
/// Moneda de una empresa: como se escribe un importe (simbolo, decimales, separadores) y como se nombra en letras. El formato es
/// explicito y no depende de la cultura del servidor, para que un mismo importe salga igual en la API, los PDF y los correos.
/// </summary>
public sealed record CurrencyInfo(
    string Code,
    string Symbol,
    int Decimals,
    string GroupSeparator,
    string DecimalSeparator,
    string SingularName,
    string PluralName,
    string CentsName);

public static class Currencies
{
    public static readonly CurrencyInfo Pyg = new("PYG", "Gs.", 0, ".", ",", "guaraní", "guaraníes", "céntimos");
    public static readonly CurrencyInfo Ars = new("ARS", "$", 2, ".", ",", "peso", "pesos", "centavos");
    public static readonly CurrencyInfo Ves = new("VES", "Bs.", 2, ".", ",", "bolívar", "bolívares", "céntimos");
    public static readonly CurrencyInfo Usd = new("USD", "US$", 2, ",", ".", "dólar", "dólares", "centavos");

    public static IReadOnlyList<CurrencyInfo> All { get; } = [Pyg, Ars, Ves, Usd];

    /// <summary>Moneda por defecto: la de siempre (guaraníes). Todo lo que no tenga empresa resuelta sigue saliendo como hasta ahora.</summary>
    public static CurrencyInfo Default => Pyg;

    public static CurrencyInfo? Find(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : All.FirstOrDefault(x => string.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    public static CurrencyInfo FindOrDefault(string? code) => Find(code) ?? Default;
}
