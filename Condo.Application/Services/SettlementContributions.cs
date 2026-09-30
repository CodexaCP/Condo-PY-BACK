namespace Condo.Application.Services;

/// <summary>
/// Aportes de la liquidacion calculados a partir de los gastos comunes: el de fondo de reserva es un % de la
/// base (gastos comunes, menos los ingresos si el edificio los acredita a los propietarios) y el
/// extraordinario un % del sub total (base + aporte de reserva). Redondeo al guarani.
/// </summary>
public static class SettlementContributions
{
    public sealed record Result(
        decimal Base,
        decimal ReserveContribution,
        decimal SubTotal,
        decimal ExtraordinaryContribution,
        decimal GrandTotal);

    public static Result Compute(decimal commonExpenses, decimal creditedIncomes, decimal? reservePercentage, decimal? extraordinaryPercentage)
    {
        var baseAmount = commonExpenses - creditedIncomes;
        var reserve = RoundGuarani(Math.Max(0m, baseAmount) * (reservePercentage ?? 0m) / 100m);
        var subTotal = baseAmount + reserve;
        var extraordinary = RoundGuarani(Math.Max(0m, subTotal) * (extraordinaryPercentage ?? 0m) / 100m);
        return new Result(baseAmount, reserve, subTotal, extraordinary, subTotal + extraordinary);
    }

    private static decimal RoundGuarani(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
