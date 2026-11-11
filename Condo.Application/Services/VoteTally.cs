using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>Una unidad del padron de la votacion (unidades activas del edificio) con su coeficiente.</summary>
public sealed record VoteTallyUnit(Guid UnitId, decimal Coefficient);

/// <summary>Voto vigente de una unidad, con el coeficiente congelado al momento de votar.</summary>
public sealed record VoteTallyCast(Guid UnitId, Guid OptionId, decimal CoefficientWeight);

public sealed record VoteTallyOption(
    Guid OptionId,
    int CastCount,
    decimal CastWeight,
    // Sobre quienes votaron (por unidades o por coeficiente segun el peso), redondeado a 1 decimal para mostrar.
    decimal Percentage,
    // Sobre la TOTALIDAD de unidades y de coeficiente del padron, redondeado a 2 decimales para mostrar.
    decimal PercentOfTotalUnits,
    decimal PercentOfTotalCoefficient,
    // Mas del 50 % de todas las unidades Y de todo el coeficiente, comparado sin redondear.
    bool HasAbsoluteMajority);

public sealed record VoteTallyResult(
    int TotalUnits,
    decimal TotalCoefficient,
    int ParticipatingUnits,
    decimal ParticipatingCoefficient,
    bool QuorumReached,
    IReadOnlyList<VoteTallyOption> Options,
    // La opcion con mas peso, si es una sola; con empate en el primer puesto queda null e IsTie = true.
    Guid? WinningOptionId,
    bool IsTie,
    // La opcion que cumple la regla de mayoria: Simple = la ganadora con quorum alcanzado; DoubleAbsolute = la de mayoria absoluta.
    Guid? ApprovedOptionId);

/// <summary>
/// Conteo de una votacion, sin base de datos. Reglas:
/// <list type="bullet">
/// <item>Solo cuentan los votos de unidades del padron (las activas del edificio).</item>
/// <item>Quorum segun el peso: unidades que votaron sobre el total de unidades, o coeficiente que voto sobre el coeficiente total.</item>
/// <item>Simple: gana la opcion con mas peso; si hay empate en el primer puesto no hay ganadora (desempate explicito: no se elige).
/// Se aprueba si ademas se alcanzo el quorum.</item>
/// <item>DoubleAbsolute: se aprueba la opcion con mas del 50 % de todas las unidades y de todo el coeficiente a la vez.</item>
/// </list>
/// Las comparaciones se hacen con los valores exactos; el redondeo es solo para mostrar.
/// </summary>
public static class VoteTally
{
    public static VoteTallyResult Compute(
        VoteWeightType weightType,
        VoteMajorityRule majorityRule,
        decimal quorumPercentage,
        IReadOnlyCollection<Guid> optionIds,
        IReadOnlyCollection<VoteTallyUnit> electorate,
        IReadOnlyCollection<VoteTallyCast> casts)
    {
        var electorateIds = electorate.Select(x => x.UnitId).ToHashSet();
        var validCasts = casts.Where(c => electorateIds.Contains(c.UnitId) && optionIds.Contains(c.OptionId)).ToList();

        var totalUnits = electorate.Count;
        var totalCoefficient = electorate.Sum(x => x.Coefficient);
        var participatingUnits = validCasts.Count;
        var participatingCoefficient = validCasts.Sum(x => x.CoefficientWeight);

        var quorumReached = weightType == VoteWeightType.ByCoefficient
            ? totalCoefficient > 0 && participatingCoefficient * 100m >= quorumPercentage * totalCoefficient
            : totalUnits > 0 && participatingUnits * 100m >= quorumPercentage * totalUnits;

        var options = optionIds.Select(optionId =>
        {
            var optionCasts = validCasts.Where(c => c.OptionId == optionId).ToList();
            var count = optionCasts.Count;
            var weight = optionCasts.Sum(c => c.CoefficientWeight);
            var percentage = weightType == VoteWeightType.ByCoefficient
                ? (participatingCoefficient > 0 ? Math.Round(weight * 100m / participatingCoefficient, 1) : 0m)
                : (participatingUnits > 0 ? Math.Round(count * 100m / participatingUnits, 1) : 0m);
            var pctUnits = totalUnits > 0 ? Math.Round(count * 100m / totalUnits, 2) : 0m;
            var pctCoefficient = totalCoefficient > 0 ? Math.Round(weight * 100m / totalCoefficient, 2) : 0m;
            var absolute = totalUnits > 0 && totalCoefficient > 0 && count * 2 > totalUnits && weight * 2m > totalCoefficient;
            return new VoteTallyOption(optionId, count, weight, percentage, pctUnits, pctCoefficient, absolute);
        }).ToList();

        decimal Weight(VoteTallyOption o) => weightType == VoteWeightType.ByCoefficient ? o.CastWeight : o.CastCount;

        Guid? winning = null;
        var isTie = false;
        if (options.Count > 0)
        {
            var max = options.Max(Weight);
            if (max > 0)
            {
                var leaders = options.Where(o => Weight(o) == max).ToList();
                if (leaders.Count == 1) winning = leaders[0].OptionId;
                else isTie = true;
            }
        }

        var approved = majorityRule == VoteMajorityRule.DoubleAbsolute
            ? options.FirstOrDefault(o => o.HasAbsoluteMajority)?.OptionId
            : (quorumReached ? winning : null);

        return new VoteTallyResult(totalUnits, totalCoefficient, participatingUnits, participatingCoefficient, quorumReached,
            options, winning, isTie, approved);
    }
}
