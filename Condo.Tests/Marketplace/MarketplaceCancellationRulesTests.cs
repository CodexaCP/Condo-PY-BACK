using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Tests.Marketplace;

/// <summary>Reglas puras de la fase 7: quien puede cancelar y cuando, cuanto se reembolsa, la ventana de reclamo y el aviso de inicio.</summary>
public class MarketplaceCancellationRulesTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── Cancelar una reserva pagada ──────────────────────────────────────────

    [Fact]
    public void Una_reserva_confirmada_se_cancela_solo_antes_de_que_empiece()
    {
        Assert.True(MarketplaceCancellationRules.CanCancelPaid(MarketplaceReservationStatus.Confirmed, Now.AddMinutes(1), Now));
        Assert.False(MarketplaceCancellationRules.CanCancelPaid(MarketplaceReservationStatus.Confirmed, Now, Now));
        Assert.False(MarketplaceCancellationRules.CanCancelPaid(MarketplaceReservationStatus.Confirmed, Now.AddHours(-1), Now));
    }

    [Theory]
    [InlineData(MarketplaceReservationStatus.PendingPayment)]
    [InlineData(MarketplaceReservationStatus.InReview)]
    [InlineData(MarketplaceReservationStatus.Completed)]
    [InlineData(MarketplaceReservationStatus.Cancelled)]
    [InlineData(MarketplaceReservationStatus.Expired)]
    [InlineData(MarketplaceReservationStatus.Rejected)]
    public void Solo_la_reserva_confirmada_se_cancela_como_pagada(MarketplaceReservationStatus status) =>
        Assert.False(MarketplaceCancellationRules.CanCancelPaid(status, Now.AddHours(5), Now));

    // ── Cuanto se reembolsa y quien asume la comision ────────────────────────

    [Fact]
    public void Si_cancela_el_comprador_se_devuelve_solo_la_base_y_el_propietario_no_asume_nada()
    {
        Assert.Equal(60_000m, MarketplaceCancellationRules.RefundAmount(MarketplaceRefundOrigin.BuyerCancellation, 60_000m, 66_000m));
        Assert.False(MarketplaceCancellationRules.OwnerAssumesCommission(MarketplaceRefundOrigin.BuyerCancellation));
    }

    [Theory]
    [InlineData(MarketplaceRefundOrigin.OwnerCancellation)]
    [InlineData(MarketplaceRefundOrigin.ClaimResolution)]
    public void Si_cancela_el_propietario_o_pierde_un_reclamo_se_devuelve_todo_y_el_propietario_asume_la_comision(MarketplaceRefundOrigin origin)
    {
        Assert.Equal(66_000m, MarketplaceCancellationRules.RefundAmount(origin, 60_000m, 66_000m));
        Assert.True(MarketplaceCancellationRules.OwnerAssumesCommission(origin));
    }

    // ── Reclamo: desde que empieza hasta 24 horas despues del fin ────────────

    [Fact]
    public void El_reclamo_se_abre_desde_que_empieza_la_reserva_hasta_24_horas_despues_de_su_fin()
    {
        var start = Now.AddHours(-2);
        var end = Now.AddHours(1);

        // En curso.
        Assert.True(MarketplaceCancellationRules.CanOpenClaim(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, start, end, false, Now));
        // Antes de empezar no hay nada que reclamar.
        Assert.False(MarketplaceCancellationRules.CanOpenClaim(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, Now.AddHours(1), Now.AddHours(4), false, Now));
        // Terminada: dentro de las 24 horas si, pasadas no.
        Assert.True(MarketplaceCancellationRules.CanOpenClaim(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, start, end, false, end.AddHours(23)));
        Assert.True(MarketplaceCancellationRules.CanOpenClaim(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, start, end, false, end.AddHours(24)));
        Assert.False(MarketplaceCancellationRules.CanOpenClaim(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, start, end, false, end.AddHours(24).AddSeconds(1)));
    }

    [Theory]
    [InlineData(MarketplaceCreditStatus.None)]
    [InlineData(MarketplaceCreditStatus.Credited)]
    [InlineData(MarketplaceCreditStatus.Reversed)]
    public void No_se_reclama_si_la_ganancia_ya_se_acredito_o_no_corresponde(MarketplaceCreditStatus credit) =>
        Assert.False(MarketplaceCancellationRules.CanOpenClaim(
            MarketplaceReservationStatus.Completed, credit, Now.AddHours(-5), Now.AddHours(-2), false, Now));

    [Fact]
    public void No_se_abre_un_segundo_reclamo_si_ya_hay_uno_abierto() =>
        Assert.False(MarketplaceCancellationRules.CanOpenClaim(
            MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Held, Now.AddHours(-5), Now.AddHours(-2), true, Now));

    // ── Aviso de inicio ──────────────────────────────────────────────────────

    [Fact]
    public void El_comprador_debe_responder_solo_si_llego_el_aviso_no_respondio_y_la_reserva_sigue_en_curso()
    {
        var end = Now.AddHours(1);
        Assert.True(MarketplaceCancellationRules.NeedsStartResponse(MarketplaceReservationStatus.Confirmed, Now.AddMinutes(-5), null, end, Now));
        Assert.False(MarketplaceCancellationRules.NeedsStartResponse(MarketplaceReservationStatus.Confirmed, null, null, end, Now));
        Assert.False(MarketplaceCancellationRules.NeedsStartResponse(MarketplaceReservationStatus.Confirmed, Now.AddMinutes(-5), MarketplaceStartResponse.Attending, end, Now));
        Assert.False(MarketplaceCancellationRules.NeedsStartResponse(MarketplaceReservationStatus.Confirmed, Now.AddMinutes(-5), null, Now.AddMinutes(-1), Now));
        Assert.False(MarketplaceCancellationRules.NeedsStartResponse(MarketplaceReservationStatus.Completed, Now.AddMinutes(-5), null, end, Now));
    }

    [Fact]
    public void La_alerta_de_reembolso_vence_a_las_72_horas() =>
        Assert.Equal(TimeSpan.FromHours(72), MarketplaceCancellationRules.RefundMaxTime);
}
