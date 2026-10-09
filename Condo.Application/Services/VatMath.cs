using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>
/// IVA de las compras. El monto del comprobante ya incluye el IVA: con 10 % el IVA es monto / 11 y con 5 % es monto / 21 (base gravada =
/// monto - IVA). Los importes se redondean a 2 decimales.
/// </summary>
public static class VatMath
{
    /// <summary>Tasas aceptadas en un gasto: 10, 5 o 0 (exento).</summary>
    public static bool IsValidRate(decimal rate) => rate is 10m or 5m or 0m;

    /// <summary>Tasa que corresponde a un tratamiento de IVA de la cuenta; null cuando no corresponde o no esta definido.</summary>
    public static decimal? RateOf(VatTreatment? treatment) => treatment switch
    {
        VatTreatment.Vat10 => 10m,
        VatTreatment.Vat5 => 5m,
        VatTreatment.Exempt => 0m,
        _ => null
    };

    /// <summary>IVA incluido en un monto con esa tasa; 0 si es exento y null si el gasto no tiene tasa.</summary>
    public static decimal? VatOf(decimal amount, decimal? rate)
    {
        if (rate is null)
        {
            return null;
        }

        return rate.Value == 0m ? 0m : decimal.Round(amount * rate.Value / (100m + rate.Value), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Base gravada (monto sin IVA) de un monto con esa tasa; el monto completo si es exento; null si no hay tasa.</summary>
    public static decimal? BaseOf(decimal amount, decimal? rate)
    {
        var vat = VatOf(amount, rate);
        return vat is null ? null : amount - vat.Value;
    }
}
