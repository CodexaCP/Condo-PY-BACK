using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>
/// IVA de las compras. El monto del comprobante ya incluye el IVA: con 10 % el IVA es monto / 11 y con 5 % es monto / 21 (base gravada =
/// monto - IVA). El IVA se redondea al guarani entero.
/// </summary>
public static class VatMath
{
    /// <summary>Tasas aceptadas en un gasto: 10, 5 o 0 (exento).</summary>
    public static bool IsValidRate(decimal rate) => rate is 10m or 5m or 0m;

    /// <summary>Tasas aceptadas en un gasto de una empresa: su tasa general, su tasa reducida (si tiene) o 0 (exento).</summary>
    public static bool IsValidRate(decimal rate, decimal? general, decimal? reduced) =>
        rate == 0m || (general.HasValue && rate == general.Value) || (reduced.HasValue && rate == reduced.Value);

    /// <summary>Lista de tasas aceptadas para mostrar en un mensaje: «10, 5» (general y reducida de la empresa).</summary>
    public static string RatesText(decimal? general, decimal? reduced) =>
        string.Join(", ", new[] { general, reduced }.Where(r => r.HasValue).Select(r => r!.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// Tasa de un tratamiento para una empresa: «IVA 10 %» es su tasa general y «IVA 5 %» su tasa reducida (en Paraguay 10 y 5). Null si el
    /// tratamiento no tiene tasa o la empresa no tiene esa tasa definida.
    /// </summary>
    public static decimal? RateOf(VatTreatment? treatment, decimal? general, decimal? reduced) => treatment switch
    {
        VatTreatment.Vat10 => general,
        VatTreatment.Vat5 => reduced,
        VatTreatment.Exempt => 0m,
        _ => null
    };

    /// <summary>Tasa que corresponde a un tratamiento de IVA de la cuenta; null cuando no corresponde o no esta definido.</summary>
    public static decimal? RateOf(VatTreatment? treatment) => treatment switch
    {
        VatTreatment.Vat10 => 10m,
        VatTreatment.Vat5 => 5m,
        VatTreatment.Exempt => 0m,
        _ => null
    };

    /// <summary>
    /// IVA incluido en un monto con esa tasa; 0 si es exento y null si el gasto no tiene tasa. El guarani no tiene centavos: el IVA se redondea
    /// al guarani entero (como el comprobante y Marangatu), no a 2 decimales.
    /// </summary>
    public static decimal? VatOf(decimal amount, decimal? rate)
    {
        if (rate is null)
        {
            return null;
        }

        return rate.Value == 0m ? 0m : decimal.Round(amount * rate.Value / (100m + rate.Value), 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Base gravada (monto sin IVA) de un monto con esa tasa; el monto completo si es exento; null si no hay tasa.</summary>
    public static decimal? BaseOf(decimal amount, decimal? rate)
    {
        var vat = VatOf(amount, rate);
        return vat is null ? null : amount - vat.Value;
    }
}
