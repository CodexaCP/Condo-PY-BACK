using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Tests.Marketplace;

public class MarketplaceStateMachineTests
{
    private const MarketplaceReservationStatus P = MarketplaceReservationStatus.PendingPayment;
    private const MarketplaceReservationStatus R = MarketplaceReservationStatus.InReview;
    private const MarketplaceReservationStatus C = MarketplaceReservationStatus.Confirmed;
    private const MarketplaceReservationStatus D = MarketplaceReservationStatus.Completed;
    private const MarketplaceReservationStatus X = MarketplaceReservationStatus.Cancelled;
    private const MarketplaceReservationStatus E = MarketplaceReservationStatus.Expired;
    private const MarketplaceReservationStatus J = MarketplaceReservationStatus.Rejected;

    public static IEnumerable<object[]> ValidReservationTransitions =>
    [
        [P, R], [P, E], [P, X],
        [R, C], [R, J],
        [C, D], [C, X]
    ];

    [Theory]
    [MemberData(nameof(ValidReservationTransitions))]
    public void Reserva_transiciones_permitidas(MarketplaceReservationStatus from, MarketplaceReservationStatus to)
    {
        Assert.True(MarketplaceStateMachine.CanTransition(from, to));
        MarketplaceStateMachine.EnsureTransition(from, to);
    }

    [Fact]
    public void Reserva_toda_otra_transicion_se_rechaza()
    {
        var allowed = ValidReservationTransitions.Select(x => ((MarketplaceReservationStatus)x[0], (MarketplaceReservationStatus)x[1])).ToHashSet();
        var all = Enum.GetValues<MarketplaceReservationStatus>();

        foreach (var from in all)
        {
            foreach (var to in all)
            {
                if (allowed.Contains((from, to)))
                {
                    continue;
                }

                Assert.False(MarketplaceStateMachine.CanTransition(from, to), $"{from} -> {to} no debería permitirse");
                Assert.Throws<MarketplaceTransitionException>(() => MarketplaceStateMachine.EnsureTransition(from, to));
            }
        }
    }

    [Fact]
    public void Reserva_no_se_puede_confirmar_sin_pasar_por_la_revision()
    {
        Assert.False(MarketplaceStateMachine.CanTransition(P, C));
    }

    [Fact]
    public void Reserva_un_pago_en_revision_no_se_cancela_se_confirma_o_se_rechaza()
    {
        Assert.False(MarketplaceStateMachine.CanTransition(R, X));
    }

    [Fact]
    public void Reserva_rechazar_solo_se_hace_desde_la_revision()
    {
        Assert.False(MarketplaceStateMachine.CanTransition(P, J));
        Assert.False(MarketplaceStateMachine.CanTransition(C, J));
    }

    [Theory]
    [InlineData(MarketplaceReservationStatus.Completed)]
    [InlineData(MarketplaceReservationStatus.Cancelled)]
    [InlineData(MarketplaceReservationStatus.Expired)]
    [InlineData(MarketplaceReservationStatus.Rejected)]
    public void Reserva_los_estados_finales_no_tienen_salida(MarketplaceReservationStatus final)
    {
        Assert.True(MarketplaceStateMachine.IsFinal(final));
        foreach (var to in Enum.GetValues<MarketplaceReservationStatus>())
        {
            Assert.False(MarketplaceStateMachine.CanTransition(final, to));
        }
    }

    [Fact]
    public void Reserva_un_estado_no_pasa_a_si_mismo()
    {
        foreach (var status in Enum.GetValues<MarketplaceReservationStatus>())
        {
            Assert.False(MarketplaceStateMachine.CanTransition(status, status));
        }
    }

    [Fact]
    public void El_mensaje_del_error_dice_de_que_estado_a_cual()
    {
        var ex = Assert.Throws<MarketplaceTransitionException>(() => MarketplaceStateMachine.EnsureTransition(E, C));

        Assert.Contains("Expired", ex.Message);
        Assert.Contains("Confirmed", ex.Message);
    }

    // ── Publicacion ──────────────────────────────────────────────────────────

    [Fact]
    public void Publicacion_se_suspende_se_reanuda_y_se_cierra()
    {
        Assert.True(MarketplaceStateMachine.CanTransition(MarketplaceListingStatus.Active, MarketplaceListingStatus.Suspended));
        Assert.True(MarketplaceStateMachine.CanTransition(MarketplaceListingStatus.Suspended, MarketplaceListingStatus.Active));
        Assert.True(MarketplaceStateMachine.CanTransition(MarketplaceListingStatus.Active, MarketplaceListingStatus.Closed));
        Assert.True(MarketplaceStateMachine.CanTransition(MarketplaceListingStatus.Suspended, MarketplaceListingStatus.Closed));
    }

    [Fact]
    public void Publicacion_cerrada_no_se_reabre()
    {
        foreach (var to in Enum.GetValues<MarketplaceListingStatus>())
        {
            Assert.False(MarketplaceStateMachine.CanTransition(MarketplaceListingStatus.Closed, to));
        }
    }

    // ── Pago ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Pago_se_aprueba_o_se_rechaza_una_sola_vez()
    {
        Assert.True(MarketplaceStateMachine.CanTransition(MarketplacePaymentStatus.Submitted, MarketplacePaymentStatus.Approved));
        Assert.True(MarketplaceStateMachine.CanTransition(MarketplacePaymentStatus.Submitted, MarketplacePaymentStatus.Rejected));
        Assert.False(MarketplaceStateMachine.CanTransition(MarketplacePaymentStatus.Approved, MarketplacePaymentStatus.Rejected));
        Assert.False(MarketplaceStateMachine.CanTransition(MarketplacePaymentStatus.Rejected, MarketplacePaymentStatus.Approved));
        Assert.False(MarketplaceStateMachine.CanTransition(MarketplacePaymentStatus.Approved, MarketplacePaymentStatus.Approved));
    }

    // ── Acreditacion del saldo ───────────────────────────────────────────────

    [Theory]
    [InlineData(MarketplaceCreditStatus.None, MarketplaceCreditStatus.Pending)]
    [InlineData(MarketplaceCreditStatus.Pending, MarketplaceCreditStatus.Credited)]
    [InlineData(MarketplaceCreditStatus.Pending, MarketplaceCreditStatus.Held)]
    [InlineData(MarketplaceCreditStatus.Pending, MarketplaceCreditStatus.None)]
    [InlineData(MarketplaceCreditStatus.Held, MarketplaceCreditStatus.Pending)]
    [InlineData(MarketplaceCreditStatus.Held, MarketplaceCreditStatus.None)]
    [InlineData(MarketplaceCreditStatus.Credited, MarketplaceCreditStatus.Reversed)]
    public void Acreditacion_transiciones_permitidas(MarketplaceCreditStatus from, MarketplaceCreditStatus to)
    {
        Assert.True(MarketplaceStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void Acreditacion_no_se_puede_acreditar_dos_veces_ni_acreditar_algo_retenido()
    {
        Assert.False(MarketplaceStateMachine.CanTransition(MarketplaceCreditStatus.Credited, MarketplaceCreditStatus.Credited));
        Assert.False(MarketplaceStateMachine.CanTransition(MarketplaceCreditStatus.Held, MarketplaceCreditStatus.Credited));
        Assert.False(MarketplaceStateMachine.CanTransition(MarketplaceCreditStatus.None, MarketplaceCreditStatus.Credited));
    }

    [Fact]
    public void Acreditacion_revertida_es_final_y_lo_acreditado_solo_se_revierte()
    {
        foreach (var to in Enum.GetValues<MarketplaceCreditStatus>())
        {
            Assert.False(MarketplaceStateMachine.CanTransition(MarketplaceCreditStatus.Reversed, to));
            if (to != MarketplaceCreditStatus.Reversed)
            {
                Assert.False(MarketplaceStateMachine.CanTransition(MarketplaceCreditStatus.Credited, to));
            }
        }
    }
}
