using Condo.Application.Models;
using Condo.Application.Services;
using Xunit;

namespace Condo.Tests.VoiceCommands;

public class VoiceCommandParserTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Theory]
    [InlineData("Listame los morosos que llevan 3 meses de atraso", 3, OverdueAgeUnit.Months, "2026-08-12")]
    [InlineData("morosos con tres meses", 3, OverdueAgeUnit.Months, "2026-08-12")]
    [InlineData("deudores de 15 días", 15, OverdueAgeUnit.Days, "2026-10-28")]
    [InlineData("morosos con 2 semanas de retraso", 2, OverdueAgeUnit.Weeks, "2026-10-29")]
    [InlineData("morosos de más de 1 año", 1, OverdueAgeUnit.Years, "2025-11-12")]
    [InlineData("morosos con un año", 1, OverdueAgeUnit.Years, "2025-11-12")]
    [InlineData("MOROSOS CON 6 MESES", 6, OverdueAgeUnit.Months, "2026-05-12")]
    [InlineData("morosos con 6 meses.", 6, OverdueAgeUnit.Months, "2026-05-12")]
    public void Morosos_con_antiguedad(string text, int amount, OverdueAgeUnit unit, string cutoff)
    {
        var r = VoiceCommandParser.Parse(text, Today);

        Assert.Equal(VoiceCommandIntent.ListDelinquents, r.Intent);
        Assert.Equal(amount, r.AgeAmount);
        Assert.Equal(unit, r.AgeUnit);
        Assert.Equal(DateOnly.Parse(cutoff), r.DueOnOrBefore);
    }

    [Theory]
    [InlineData("morosos de este mes")]
    [InlineData("morosos con 0 meses")]
    [InlineData("morosos con 5000 días")]
    public void Morosos_sin_antiguedad(string text)
    {
        var r = VoiceCommandParser.Parse(text, Today);

        Assert.Equal(VoiceCommandIntent.ListDelinquents, r.Intent);
        Assert.Null(r.AgeAmount);
        Assert.Null(r.AgeUnit);
        Assert.Null(r.DueOnOrBefore);
    }

    [Theory]
    [InlineData("crear período vacío")]
    [InlineData("crea un periodo nuevo sin gastos")]
    [InlineData("abrí el período en blanco")]
    [InlineData("período nuevo sin gastos del anterior")]
    public void Periodo_vacio(string text) =>
        Assert.Equal(VoiceCommandIntent.CreateEmptyPeriod, VoiceCommandParser.Parse(text, Today).Intent);

    [Theory]
    [InlineData("crear período igual al anterior")]
    [InlineData("copiá el período pasado")]
    [InlineData("clonar la liquidación")]
    [InlineData("período idéntico al anterior")]
    public void Periodo_igual_al_anterior(string text) =>
        Assert.Equal(VoiceCommandIntent.ClonePreviousPeriod, VoiceCommandParser.Parse(text, Today).Intent);

    [Fact]
    public void Periodo_sin_decir_como_es_desconocido_pero_menciona_periodo()
    {
        var r = VoiceCommandParser.Parse("crear período nuevo", Today);

        Assert.Equal(VoiceCommandIntent.Unknown, r.Intent);
        Assert.True(r.MentionsPeriod);
    }

    [Theory]
    [InlineData("hola")]
    [InlineData("")]
    [InlineData(null)]
    public void Frase_no_entendida(string? text)
    {
        var r = VoiceCommandParser.Parse(text, Today);

        Assert.Equal(VoiceCommandIntent.Unknown, r.Intent);
        Assert.False(r.MentionsPeriod);
    }
}
