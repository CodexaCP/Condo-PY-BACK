using Condo.Application.Models;
using Condo.Domain.Enums;
using Xunit;
using static Condo.Tests.Votes.VoteEnv;

namespace Condo.Tests.Votes;

// Votaciones, Fase 3: canal del propietario (lista, voto, cambio, privacidad y resultados).
public class OwnerVoteTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task El_propietario_ve_la_votacion_abierta_solo_con_su_unidad_y_sin_resultados()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();

        var list = Ok(await env.As(env.Owner).ListForOwnerAsync(Ct));

        var vote = Assert.Single(list);
        Assert.Equal(open.Id, vote.Id);
        var unit = Assert.Single(vote.MyUnits);
        Assert.Equal(env.Units[0].Id, unit.UnitId);
        Assert.True(unit.CanVote);
        Assert.False(vote.ResultsVisible);
        Assert.All(vote.Options, o => Assert.Null(o.CastCount));
    }

    [Fact]
    public async Task El_propietario_vota_y_el_personal_lo_ve_como_voto_del_propietario()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();

        var dto = Ok(await env.As(env.Owner).CastAsOwnerAsync(open.Id,
            new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct));

        Assert.Equal(open.Options[0].Id, dto.MyUnits[0].VotedOptionId);
        Assert.Equal("Owner", dto.MyUnits[0].CastSource);
        var staffView = Ok(await env.As(env.Manager).GetAsync(open.Id, Ct));
        Assert.Equal("Owner", staffView.Units.First(u => u.UnitId == env.Units[0].Id).CastSource);
    }

    [Fact]
    public async Task El_propietario_puede_cambiar_su_voto()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var service = env.As(env.Owner);

        Ok(await service.CastAsOwnerAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct));
        var dto = Ok(await service.CastAsOwnerAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[1].Id }, Ct));

        Assert.Equal(open.Options[1].Id, dto.MyUnits[0].VotedOptionId);
    }

    [Fact]
    public async Task El_propietario_no_vota_por_una_unidad_ajena()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();

        var result = await env.As(env.Owner).CastAsOwnerAsync(open.Id,
            new VoteCastRequest { UnitId = env.Units[1].Id, VoteOptionId = open.Options[0].Id }, Ct);

        Assert.Equal(403, Status(result));
    }

    [Fact]
    public async Task El_propietario_no_pisa_un_voto_cargado_por_el_personal()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        Ok(await env.As(env.Manager).CastAsync(open.Id,
            new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct));

        var result = await env.As(env.Owner).CastAsOwnerAsync(open.Id,
            new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[1].Id }, Ct);

        Assert.Equal(400, Status(result));
        var list = Ok(await env.As(env.Owner).ListForOwnerAsync(Ct));
        Assert.False(list[0].MyUnits[0].CanVote);
    }

    [Fact]
    public async Task El_borrador_no_se_lista_ni_se_abre()
    {
        using var env = new VoteEnv();
        var draft = Ok(await env.As(env.Manager).CreateAsync(env.Request(), Ct));
        var owner = env.As(env.Owner);

        Assert.Empty(Ok(await owner.ListForOwnerAsync(Ct)));
        Assert.Equal(404, Status(await owner.GetForOwnerAsync(draft.Id, Ct)));
        Assert.Equal(404, Status(await owner.CastAsOwnerAsync(draft.Id,
            new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = draft.Options[0].Id }, Ct)));
    }

    [Fact]
    public async Task Al_cerrar_se_ven_los_resultados()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var owner = env.As(env.Owner);
        Ok(await owner.CastAsOwnerAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct));
        Ok(await env.As(env.Manager).CloseAsync(open.Id, Ct));

        var dto = Ok(await owner.GetForOwnerAsync(open.Id, Ct));

        Assert.True(dto.ResultsVisible);
        Assert.Equal(1, dto.Options.First(o => o.Id == open.Options[0].Id).CastCount);
        Assert.False(dto.MyUnits[0].CanVote);
        Assert.Equal(400, Status(await owner.CastAsOwnerAsync(open.Id,
            new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[1].Id }, Ct)));
    }

    [Fact]
    public async Task El_copropietario_no_principal_ve_pero_no_vota()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var co = env.T.AddUser(env.Company, UserRole.Owner, "Copropietario");
        env.T.AddOwner(env.Units[0], co, primary: false);
        var service = env.As(co);

        var list = Ok(await service.ListForOwnerAsync(Ct));

        Assert.False(Assert.Single(list).MyUnits[0].CanVote);
        Assert.Equal(403, Status(await service.CastAsOwnerAsync(open.Id,
            new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct)));
    }

    [Fact]
    public async Task Un_usuario_sin_unidades_no_ve_votaciones()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var res = env.T.AddUser(env.Company, UserRole.Resident, "Residente");
        var service = env.As(res);

        Assert.Empty(Ok(await service.ListForOwnerAsync(Ct)));
        Assert.Equal(404, Status(await service.GetForOwnerAsync(open.Id, Ct)));
    }
}
