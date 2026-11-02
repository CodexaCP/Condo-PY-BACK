using System.Globalization;

namespace Condo.Application.Localization;

/// <summary>
/// Formato central de importes. La moneda sale de la empresa del usuario (la fija el middleware al entrar la peticion) o de un bloque
/// <see cref="Use"/> en los procesos de fondo y en los documentos de un edificio concreto. Sin moneda resuelta se usan guaraníes, que
/// es lo que el sistema escribia antes de ser multipais.
/// </summary>
public static class Money
{
    private static readonly AsyncLocal<CurrencyInfo?> Ambient = new();

    public static CurrencyInfo Current => Ambient.Value ?? Currencies.Default;

    /// <summary>Fija la moneda del contexto actual hasta que se libere el bloque.</summary>
    public static IDisposable Use(CurrencyInfo? currency)
    {
        var previous = Ambient.Value;
        Ambient.Value = currency;
        return new Scope(previous);
    }

    /// <summary>Importe con simbolo: «Gs. 1.234.567» o «$ 1.234,56».</summary>
    public static string Format(decimal amount, CurrencyInfo? currency = null)
    {
        var c = currency ?? Current;
        return $"{c.Symbol} {Number(amount, c)}";
    }

    /// <summary>Importe sin simbolo, con los separadores y decimales de la moneda.</summary>
    public static string Number(decimal amount, CurrencyInfo? currency = null)
    {
        var c = currency ?? Current;
        var info = new NumberFormatInfo
        {
            NumberGroupSeparator = c.GroupSeparator,
            NumberDecimalSeparator = c.DecimalSeparator,
            NumberGroupSizes = [3],
            NumberDecimalDigits = c.Decimals,
            NegativeSign = "-"
        };
        return amount.ToString("N" + c.Decimals, info);
    }

    /// <summary>Redondea al decimal mas chico de la moneda (el guarani no tiene centavos; el peso si).</summary>
    public static decimal Round(decimal amount, CurrencyInfo? currency = null) =>
        decimal.Round(amount, (currency ?? Current).Decimals, MidpointRounding.AwayFromZero);

    private sealed class Scope(CurrencyInfo? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
