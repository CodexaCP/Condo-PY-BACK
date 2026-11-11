using Condo.Application.Services;
using Condo.Domain.Enums;
using Xunit;

namespace Condo.Tests.Votes;

// Conteo puro de una votacion: quorum segun el peso, ganadora/empate en la regla Simple y mayoria absoluta sin redondear.
public class VoteTallyTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static VoteTallyUnit U(decimal coefficient) => new(Guid.NewGuid(), coefficient);

    private static VoteTallyResult Run(VoteWeightType weight, VoteMajorityRule rule, decimal quorum,
        IReadOnlyCollection<VoteTallyUnit> units, params (VoteTallyUnit Unit, Guid Option)[] casts) =>
        VoteTally.Compute(weight, rule, quorum, [A, B], units,
            casts.Select(c => new VoteTallyCast(c.Unit.UnitId, c.Option, c.Unit.Coefficient)).ToList());

    [Fact]
    public void El_quorum_por_coeficiente_se_mide_con_el_coeficiente_y_no_con_la_cantidad_de_unidades()
    {
        // 1 unidad grande (60 %) y 4 chicas (10 % cada una). Vota solo la grande: 1 de 5 unidades, pero 60 % del coeficiente.
        var big = U(0.6m);
        var small = Enumerable.Range(0, 4).Select(_ => U(0.1m)).ToList();
        var units = small.Prepend(big).ToList();

        var byUnit = Run(VoteWeightType.ByUnit, VoteMajorityRule.Simple, 50m, units, (big, A));
        var byCoefficient = Run(VoteWeightType.ByCoefficient, VoteMajorityRule.Simple, 50m, units, (big, A));

        Assert.False(byUnit.QuorumReached);
        Assert.True(byCoefficient.QuorumReached);
        Assert.Equal(0.6m, byCoefficient.ParticipatingCoefficient);
        Assert.Null(byUnit.ApprovedOptionId);                 // gana A, pero sin quorum no se aprueba
        Assert.Equal(A, byUnit.WinningOptionId);
        Assert.Equal(A, byCoefficient.ApprovedOptionId);
    }

    [Fact]
    public void El_quorum_justo_se_alcanza()
    {
        var units = Enumerable.Range(0, 4).Select(_ => U(0.25m)).ToList();
        var result = Run(VoteWeightType.ByUnit, VoteMajorityRule.Simple, 50m, units, (units[0], A), (units[1], B));
        Assert.True(result.QuorumReached);
    }

    [Fact]
    public void Con_empate_en_el_primer_puesto_no_hay_ganadora_ni_aprobada()
    {
        var units = Enumerable.Range(0, 4).Select(_ => U(0.25m)).ToList();
        var result = Run(VoteWeightType.ByUnit, VoteMajorityRule.Simple, 50m, units, (units[0], A), (units[1], B));

        Assert.True(result.IsTie);
        Assert.Null(result.WinningOptionId);
        Assert.Null(result.ApprovedOptionId);
    }

    [Fact]
    public void Por_coeficiente_desempata_el_peso_y_no_la_cantidad()
    {
        // A: 2 unidades chicas (0,2); B: 1 unidad grande (0,6). Por coeficiente gana B aunque tenga menos votos.
        var a1 = U(0.1m);
        var a2 = U(0.1m);
        var b1 = U(0.6m);
        var units = new List<VoteTallyUnit> { a1, a2, b1, U(0.2m) };

        var result = Run(VoteWeightType.ByCoefficient, VoteMajorityRule.Simple, 50m, units, (a1, A), (a2, A), (b1, B));

        Assert.False(result.IsTie);
        Assert.Equal(B, result.WinningOptionId);
        Assert.Equal(B, result.ApprovedOptionId);
    }

    [Fact]
    public void La_mayoria_absoluta_se_compara_sin_redondear()
    {
        // A suma 0,500001 del coeficiente: redondeado a 2 decimales seria 50,00 % (no supera 50), pero el valor real si.
        var u1 = U(0.25m);
        var u2 = U(0.250001m);
        var u3 = U(0.499999m);
        var units = new List<VoteTallyUnit> { u1, u2, u3 };

        var result = Run(VoteWeightType.ByCoefficient, VoteMajorityRule.DoubleAbsolute, 50m, units, (u1, A), (u2, A));
        var a = result.Options.Single(o => o.OptionId == A);

        Assert.Equal(50.00m, a.PercentOfTotalCoefficient);
        Assert.True(a.HasAbsoluteMajority);
        Assert.Equal(A, result.ApprovedOptionId);
    }

    [Fact]
    public void Los_votos_de_unidades_fuera_del_padron_no_cuentan()
    {
        var units = Enumerable.Range(0, 2).Select(_ => U(0.5m)).ToList();
        var outsider = U(0.5m);

        var result = Run(VoteWeightType.ByUnit, VoteMajorityRule.Simple, 50m, units, (units[0], A), (outsider, B));

        Assert.Equal(1, result.ParticipatingUnits);
        Assert.Equal(0, result.Options.Single(o => o.OptionId == B).CastCount);
    }
}
