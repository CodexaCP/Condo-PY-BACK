using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Xunit;
using static Condo.Tests.Votes.VoteEnv;

namespace Condo.Tests.Votes;

/// <summary>
/// Concurrencia de votaciones contra SQL Server REAL (RowVersion e indice unico): se omite sola sin CONDO_TEST_SQLSERVER. Cada pedido
/// usa su propio contexto, como en produccion. Ninguno puede terminar en una excepcion sin controlar y el estado final tiene que cerrar.
/// </summary>
public class VoteConcurrencySqlServerTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static async Task<(VoteResult<VoteDto>? Value, Exception? Error)[]> Race(params Func<Task<VoteResult<VoteDto>>>[] work)
    {
        using var gate = new ManualResetEventSlim(false);
        var tasks = work.Select(w => Task.Run(async () =>
        {
            gate.Wait();
            try { return (await w(), (Exception?)null); }
            catch (Exception ex) { return ((VoteResult<VoteDto>?)null, ex); }
        })).ToArray();
        gate.Set();
        return await Task.WhenAll(tasks);
    }

    private static void AssertNoCrashes((VoteResult<VoteDto>? Value, Exception? Error)[] results) =>
        Assert.True(results.All(r => r.Error is null),
            "Excepciones sin controlar: " + string.Join(" | ", results.Where(r => r.Error is not null).Select(r => r.Error!.GetType().Name + ": " + r.Error.Message)));

    [SqlServerFact]
    public async Task Votos_y_cierre_al_mismo_tiempo_no_dejan_un_resultado_que_no_coincida_con_los_votos()
    {
        using var env = new VoteEnv(useSqlServer: true);
        var open = await env.OpenVoteAsync();
        var option = open.Options[0].Id;

        var work = env.Units.Select(u => (Func<Task<VoteResult<VoteDto>>>)(() =>
                env.As(env.Operator, env.T.NewContext()).CastAsync(open.Id, new VoteCastRequest { UnitId = u.Id, VoteOptionId = option }, Ct)))
            .Append(() => env.As(env.Manager, env.T.NewContext()).CloseAsync(open.Id, Ct))
            .ToArray();

        var results = await Race(work);
        AssertNoCrashes(results);
        Assert.All(results.Where(r => !r.Value!.Ok), r => Assert.Contains(r.Value!.Error!.StatusCode, new[] { 400, 409 }));

        using var db = env.T.NewContext();
        var vote = db.Votes.Single(x => x.Id == open.Id);
        var casts = db.VoteCasts.Count(x => x.VoteId == open.Id);
        Assert.Equal(casts, db.VoteCastEvents.Count(x => x.VoteId == open.Id));
        if (vote.Status == VoteStatus.Closed)
            Assert.Equal(casts, vote.ResultParticipatingUnits);
    }

    [SqlServerFact]
    public async Task Dos_primeros_votos_simultaneos_de_la_misma_unidad_dejan_un_solo_voto_vigente()
    {
        using var env = new VoteEnv(useSqlServer: true);
        var open = await env.OpenVoteAsync();
        var unit = env.Units[0].Id;

        var results = await Race(
            () => env.As(env.Manager, env.T.NewContext()).CastAsync(open.Id, new VoteCastRequest { UnitId = unit, VoteOptionId = open.Options[0].Id }, Ct),
            () => env.As(env.Operator, env.T.NewContext()).CastAsync(open.Id, new VoteCastRequest { UnitId = unit, VoteOptionId = open.Options[1].Id }, Ct));

        AssertNoCrashes(results);
        Assert.All(results.Where(r => !r.Value!.Ok), r => Assert.Equal(409, r.Value!.Error!.StatusCode));

        using var db = env.T.NewContext();
        Assert.Equal(1, db.VoteCasts.Count(x => x.VoteId == open.Id && x.UnitId == unit));
        // Un evento por cada pedido que salio bien (si el segundo llego despues del primero, es un cambio).
        Assert.Equal(results.Count(r => r.Value!.Ok), db.VoteCastEvents.Count(x => x.VoteId == open.Id));
    }
}
