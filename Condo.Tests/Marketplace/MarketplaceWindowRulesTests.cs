using Condo.Application.Services;

namespace Condo.Tests.Marketplace;

public class MarketplaceWindowRulesTests
{
    private static DateTime U(int day, int hour, int minute = 0, int second = 0) =>
        new(2026, 10, day, hour, minute, second, DateTimeKind.Utc);

    // "Ahora" fijo: 2 de octubre 12:00 UTC.
    private static readonly DateTime Now = U(2, 12);

    [Theory]
    [InlineData(19, 0, true)]
    [InlineData(19, 30, true)]
    [InlineData(19, 15, false)]
    [InlineData(19, 45, false)]
    public void Las_horas_solo_valen_en_punto_o_y_media(int hour, int minute, bool aligned)
    {
        Assert.Equal(aligned, MarketplaceWindowRules.IsAligned(U(3, hour, minute)));
    }

    [Fact]
    public void Una_hora_con_segundos_o_fracciones_no_esta_alineada()
    {
        Assert.False(MarketplaceWindowRules.IsAligned(U(3, 19, 0, 30)));
        Assert.False(MarketplaceWindowRules.IsAligned(U(3, 19).AddTicks(1)));
    }

    // ── Solapamientos (intervalos semiabiertos) ──────────────────────────────

    [Theory]
    [InlineData(18, 20)]  // pisa el inicio
    [InlineData(19, 22)]  // identico
    [InlineData(20, 21)]  // contenido
    [InlineData(18, 23)]  // contiene
    [InlineData(21, 23)]  // pisa el final
    public void Los_horarios_que_se_pisan_chocan_con_19_a_22(int start, int end)
    {
        Assert.True(MarketplaceWindowRules.Overlaps(U(3, 19), U(3, 22), U(3, start), U(3, end)));
    }

    [Fact]
    public void Un_solapamiento_de_media_hora_tambien_choca()
    {
        // 19:30 -> 21:00 contra 19:00 -> 22:00
        Assert.True(MarketplaceWindowRules.Overlaps(U(3, 19), U(3, 22), U(3, 19, 30), U(3, 21)));
    }

    [Theory]
    [InlineData(22, 23)]  // empieza justo cuando termina la otra
    [InlineData(17, 19)]  // termina justo cuando empieza la otra
    [InlineData(23, 23)]  // (23 -> 23:30) totalmente despues
    public void Las_reservas_consecutivas_no_chocan(int start, int end)
    {
        var otherEnd = end == start ? U(3, start, 30) : U(3, end);

        Assert.False(MarketplaceWindowRules.Overlaps(U(3, 19), U(3, 22), U(3, start), otherEnd));
    }

    // ── Ventana de una publicacion ───────────────────────────────────────────

    [Fact]
    public void Ventana_valida_que_cruza_la_medianoche()
    {
        Assert.Null(MarketplaceWindowRules.ValidateListingWindow(U(3, 20), U(4, 3), Now));
    }

    [Fact]
    public void Ventana_valida_que_empieza_y_termina_y_media()
    {
        Assert.Null(MarketplaceWindowRules.ValidateListingWindow(U(3, 20, 30), U(4, 3, 30), Now));
        Assert.Equal(7, MarketplaceWindowRules.WholeHours(U(3, 20, 30), U(4, 3, 30)));
    }

    [Fact]
    public void Ventana_de_dos_horas_y_media_es_invalida_porque_los_minutos_difieren()
    {
        Assert.NotNull(MarketplaceWindowRules.ValidateListingWindow(U(3, 10), U(3, 12, 30), Now));
        Assert.NotNull(MarketplaceWindowRules.ValidateListingWindow(U(3, 10, 30), U(3, 13), Now));
    }

    [Fact]
    public void Ventana_con_minutos_sueltos_es_invalida()
    {
        Assert.NotNull(MarketplaceWindowRules.ValidateListingWindow(U(3, 10, 15), U(3, 12, 15), Now));
    }

    [Fact]
    public void Ventana_con_fin_igual_o_anterior_al_inicio_es_invalida()
    {
        Assert.NotNull(MarketplaceWindowRules.ValidateListingWindow(U(3, 10), U(3, 10), Now));
        Assert.NotNull(MarketplaceWindowRules.ValidateListingWindow(U(3, 12), U(3, 10), Now));
    }

    [Fact]
    public void Ventana_que_ya_empezo_o_empieza_ahora_es_invalida()
    {
        Assert.NotNull(MarketplaceWindowRules.ValidateListingWindow(U(2, 11), U(2, 15), Now));
        Assert.NotNull(MarketplaceWindowRules.ValidateListingWindow(U(2, 12), U(2, 15), Now));
        Assert.Null(MarketplaceWindowRules.ValidateListingWindow(U(2, 12, 30), U(2, 15, 30), Now));
    }

    // ── Reserva dentro de la ventana ─────────────────────────────────────────

    [Fact]
    public void Reservar_solo_parte_de_la_ventana_es_valido()
    {
        // Ventana 10 horas, se reservan 2.
        Assert.Null(MarketplaceWindowRules.ValidateReservationInterval(U(3, 10), U(3, 20), U(3, 12), U(3, 14), Now));
    }

    [Fact]
    public void La_reserva_puede_empezar_y_media_dentro_de_una_ventana_en_punto()
    {
        Assert.Null(MarketplaceWindowRules.ValidateReservationInterval(U(3, 10), U(3, 20), U(3, 10, 30), U(3, 12, 30), Now));
    }

    [Fact]
    public void Reservar_toda_la_ventana_es_valido()
    {
        Assert.Null(MarketplaceWindowRules.ValidateReservationInterval(U(3, 10), U(3, 14), U(3, 10), U(3, 14), Now));
    }

    [Fact]
    public void Una_reserva_que_empieza_antes_o_termina_despues_de_la_ventana_es_invalida()
    {
        Assert.NotNull(MarketplaceWindowRules.ValidateReservationInterval(U(3, 10), U(3, 14), U(3, 9), U(3, 11), Now));
        Assert.NotNull(MarketplaceWindowRules.ValidateReservationInterval(U(3, 10), U(3, 14), U(3, 13), U(3, 15), Now));
    }

    [Fact]
    public void Una_reserva_de_una_hora_y_media_es_invalida()
    {
        Assert.NotNull(MarketplaceWindowRules.ValidateReservationInterval(U(3, 10), U(3, 20), U(3, 12), U(3, 13, 30), Now));
    }

    [Fact]
    public void Una_reserva_en_el_pasado_es_invalida()
    {
        Assert.NotNull(MarketplaceWindowRules.ValidateReservationInterval(U(2, 8), U(2, 20), U(2, 10), U(2, 12), Now));
    }

    // ── Bloques de 30 minutos ────────────────────────────────────────────────

    [Fact]
    public void Tres_horas_son_seis_bloques_y_el_fin_no_se_incluye()
    {
        var slots = MarketplaceWindowRules.Slots(U(3, 19), U(3, 22));

        Assert.Equal(6, slots.Count);
        Assert.Equal(U(3, 19), slots[0]);
        Assert.Equal(U(3, 21, 30), slots[^1]);
        Assert.DoesNotContain(U(3, 22), slots);
    }

    [Fact]
    public void Dos_reservas_consecutivas_no_comparten_ningun_bloque()
    {
        var a = MarketplaceWindowRules.Slots(U(3, 19), U(3, 22));
        var b = MarketplaceWindowRules.Slots(U(3, 22), U(3, 23));

        Assert.Empty(a.Intersect(b));
    }

    [Fact]
    public void Dos_reservas_que_se_pisan_comparten_al_menos_un_bloque()
    {
        var a = MarketplaceWindowRules.Slots(U(3, 19), U(3, 22));
        var b = MarketplaceWindowRules.Slots(U(3, 21, 30), U(3, 23, 30));

        Assert.Equal([U(3, 21, 30)], a.Intersect(b).ToList());
    }

    [Fact]
    public void Una_fecha_sin_tipo_se_toma_como_utc()
    {
        var unspecified = new DateTime(2026, 10, 3, 19, 0, 0, DateTimeKind.Unspecified);

        Assert.True(MarketplaceWindowRules.IsAligned(unspecified));
        Assert.Equal(6, MarketplaceWindowRules.Slots(unspecified, unspecified.AddHours(3)).Count);
    }
}
