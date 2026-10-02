namespace Condo.Application.Services;

/// <summary>
/// Importes de una reserva, congelados al crearla. El propietario fija el precio por hora (lo que quiere recibir); la
/// comision de gestion se calcula UNA vez sobre el total de la base y se redondea siempre hacia arriba al guarani entero.
/// Total = base + comision; al propietario le corresponde exactamente la base.
/// </summary>
public sealed record MarketplaceQuote(
    int Hours,
    decimal HourlyPrice,
    decimal BaseAmount,
    decimal CommissionPercent,
    decimal CommissionAmount,
    decimal TotalAmount,
    decimal OwnerNetAmount);

public static class MarketplacePricing
{
    // Tope tecnico del precio por hora (evita errores de tipeo y desbordes de decimal(18,2)); no es una regla de negocio.
    public const decimal MaxHourlyPrice = 100_000_000m;

    /// <summary>Mensaje de error si el precio por hora no sirve; null si es valido. El guarani no tiene decimales.</summary>
    public static string? ValidateHourlyPrice(decimal hourlyPrice)
    {
        if (hourlyPrice <= 0m)
        {
            return "El precio por hora debe ser mayor que cero.";
        }

        if (decimal.Truncate(hourlyPrice) != hourlyPrice)
        {
            return "El precio por hora debe ser un monto entero en guaraníes.";
        }

        if (hourlyPrice > MaxHourlyPrice)
        {
            return "El precio por hora es demasiado alto.";
        }

        return null;
    }

    /// <summary>Mensaje de error si la comision no sirve (0 a 100, hasta 2 decimales); null si es valida.</summary>
    public static string? ValidateCommissionPercent(decimal percent)
    {
        if (percent < 0m || percent > 100m)
        {
            return "La comisión debe estar entre 0 y 100.";
        }

        if (decimal.Round(percent, 2) != percent)
        {
            return "La comisión admite como máximo 2 decimales.";
        }

        return null;
    }

    /// <summary>Comision de gestion: porcentaje del total de la base, redondeado hacia arriba al guarani entero.</summary>
    public static decimal CommissionFor(decimal baseAmount, decimal commissionPercent) =>
        decimal.Ceiling(baseAmount * commissionPercent / 100m);

    public static MarketplaceQuote Quote(decimal hourlyPrice, int hours, decimal commissionPercent)
    {
        var priceError = ValidateHourlyPrice(hourlyPrice);
        if (priceError is not null)
        {
            throw new ArgumentOutOfRangeException(nameof(hourlyPrice), priceError);
        }

        if (hours < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(hours), "La reserva dura como mínimo una hora.");
        }

        var percentError = ValidateCommissionPercent(commissionPercent);
        if (percentError is not null)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionPercent), percentError);
        }

        var baseAmount = hourlyPrice * hours;
        var commission = CommissionFor(baseAmount, commissionPercent);

        return new MarketplaceQuote(
            Hours: hours,
            HourlyPrice: hourlyPrice,
            BaseAmount: baseAmount,
            CommissionPercent: commissionPercent,
            CommissionAmount: commission,
            TotalAmount: baseAmount + commission,
            OwnerNetAmount: baseAmount);
    }
}
