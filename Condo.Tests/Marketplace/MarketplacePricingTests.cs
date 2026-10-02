using Condo.Application.Services;

namespace Condo.Tests.Marketplace;

public class MarketplacePricingTests
{
    [Fact]
    public void Ejemplo_de_la_especificacion_3_horas_a_20000_con_10_por_ciento()
    {
        var q = MarketplacePricing.Quote(20_000m, 3, 10m);

        Assert.Equal(3, q.Hours);
        Assert.Equal(60_000m, q.BaseAmount);
        Assert.Equal(6_000m, q.CommissionAmount);
        Assert.Equal(66_000m, q.TotalAmount);
        Assert.Equal(60_000m, q.OwnerNetAmount);
    }

    [Fact]
    public void El_propietario_recibe_exactamente_la_base_y_el_total_es_base_mas_comision()
    {
        var q = MarketplacePricing.Quote(25_555m, 2, 7.5m);

        Assert.Equal(q.BaseAmount, q.OwnerNetAmount);
        Assert.Equal(q.BaseAmount + q.CommissionAmount, q.TotalAmount);
    }

    [Theory]
    [InlineData(25_555, 2, 10, 5_111)]   // base 51.110 -> 5.111 exacto
    [InlineData(10_001, 1, 10, 1_001)]   // 1.000,1 -> sube a 1.001
    [InlineData(3, 1, 10, 1)]            // 0,3 -> sube a 1
    [InlineData(1_000, 1, 0, 0)]         // sin comision
    [InlineData(20_000, 3, 12, 7_200)]   // 60.000 al 12%
    public void La_comision_se_redondea_siempre_hacia_arriba_al_guarani_entero(int price, int hours, int percent, int expectedCommission)
    {
        var q = MarketplacePricing.Quote(price, hours, percent);

        Assert.Equal(expectedCommission, q.CommissionAmount);
    }

    [Fact]
    public void La_comision_se_calcula_una_sola_vez_sobre_el_total_no_por_hora()
    {
        // Por hora: ceil(1.005 * 10%) = 101 -> x2 = 202. Sobre el total: ceil(2.010 * 10%) = 201.
        var q = MarketplacePricing.Quote(1_005m, 2, 10m);

        Assert.Equal(201m, q.CommissionAmount);
    }

    [Fact]
    public void La_comision_con_decimales_en_el_porcentaje_se_calcula_sin_perdida()
    {
        // 45.000 * 7,5% = 3.375 exacto
        Assert.Equal(3_375m, MarketplacePricing.CommissionFor(45_000m, 7.5m));
        // 100 * 0,01% = 0,01 -> sube a 1
        Assert.Equal(1m, MarketplacePricing.CommissionFor(100m, 0.01m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_precio_por_hora_debe_ser_mayor_que_cero(int price)
    {
        Assert.NotNull(MarketplacePricing.ValidateHourlyPrice(price));
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketplacePricing.Quote(price, 1, 10m));
    }

    [Fact]
    public void El_precio_por_hora_debe_ser_entero_y_no_pasar_el_tope()
    {
        Assert.NotNull(MarketplacePricing.ValidateHourlyPrice(20_000.5m));
        Assert.NotNull(MarketplacePricing.ValidateHourlyPrice(MarketplacePricing.MaxHourlyPrice + 1));
        Assert.Null(MarketplacePricing.ValidateHourlyPrice(MarketplacePricing.MaxHourlyPrice));
        Assert.Null(MarketplacePricing.ValidateHourlyPrice(20_000m));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(10.123)]
    public void La_comision_debe_estar_entre_0_y_100_con_dos_decimales_como_maximo(double percent)
    {
        Assert.NotNull(MarketplacePricing.ValidateCommissionPercent((decimal)percent));
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketplacePricing.Quote(20_000m, 1, (decimal)percent));
    }

    [Fact]
    public void Los_limites_de_la_comision_son_validos()
    {
        Assert.Null(MarketplacePricing.ValidateCommissionPercent(0m));
        Assert.Null(MarketplacePricing.ValidateCommissionPercent(100m));
        Assert.Null(MarketplacePricing.ValidateCommissionPercent(12.5m));
    }

    [Fact]
    public void La_reserva_dura_como_minimo_una_hora()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketplacePricing.Quote(20_000m, 0, 10m));
    }

    [Fact]
    public void Cambiar_la_comision_despues_no_altera_una_cotizacion_ya_hecha()
    {
        var antes = MarketplacePricing.Quote(20_000m, 3, 10m);
        var despues = MarketplacePricing.Quote(20_000m, 3, 12m);

        // La cotizacion congelada es un valor: la reserva guarda estos numeros y no los recalcula.
        Assert.Equal(66_000m, antes.TotalAmount);
        Assert.Equal(67_200m, despues.TotalAmount);
    }
}
