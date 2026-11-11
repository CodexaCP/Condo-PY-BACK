using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Condo.Tests.Votes.VoteEnv;

namespace Condo.Tests.Votes;

// Votaciones, Fase 1: permisos por accion, aislamiento entre edificios, historial de votos, resultado congelado, anulacion,
// validaciones e informativa por defecto.
public class VoteServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // ── Permisos por accion ─────────────────────────────────────────────────

    [Fact]
    public async Task El_operador_carga_votos_pero_no_crea_abre_cierra_ni_anula()
    {
        using var env = new VoteEnv();
        var op = env.As(env.Operator);

        Assert.Equal(403, Status(await op.CreateAsync(env.Request(), Ct)));

        var draft = Ok(await env.As(env.Manager).CreateAsync(env.Request(), Ct));
        Assert.Equal(403, Status(await op.UpdateAsync(draft.Id, env.Request(), Ct)));
        Assert.Equal(403, Status(await op.OpenAsync(draft.Id, Ct)));
        Assert.Equal(403, Status(await op.DeleteAsync(draft.Id, Ct)));

        var open = Ok(await env.As(env.Manager).OpenAsync(draft.Id, Ct));
        Assert.False(Ok(await op.GetAsync(open.Id, Ct)).CanManage);
        Assert.True(Ok(await op.GetAsync(open.Id, Ct)).CanCast);

        var cast = Ok(await op.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct));
        Assert.Equal(1, cast.ParticipatingUnits);

        Assert.Equal(403, Status(await op.CloseAsync(open.Id, Ct)));
        Assert.Equal(403, Status(await op.CancelAsync(open.Id, new VoteCancelRequest { Reason = "x" }, Ct)));
    }

    [Fact]
    public async Task El_administrador_de_la_empresa_gestiona_votaciones()
    {
        using var env = new VoteEnv();
        var admin = env.As(env.Admin);
        var created = Ok(await admin.CreateAsync(env.Request(), Ct));
        var open = Ok(await admin.OpenAsync(created.Id, Ct));
        var closed = Ok(await admin.CloseAsync(open.Id, Ct));
        Assert.Equal("Closed", closed.Status);
        Assert.True(closed.CanManage);
    }

    [Fact]
    public async Task Propietarios_y_porteros_no_acceden_a_las_votaciones_del_personal()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var porter = env.T.AddUser(env.Company, UserRole.Porter, "Portero");
        env.T.AddBuildingAccess(porter, env.Building);

        foreach (var user in new[] { env.Owner, porter })
        {
            var service = env.As(user);
            Assert.Equal(403, Status(await service.ListAsync(null, Ct)));
            Assert.Equal(404, Status(await service.GetAsync(open.Id, Ct)));
            Assert.Equal(404, Status(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct)));
            Assert.Equal(404, Status(await service.GetHistoryAsync(open.Id, null, Ct)));
        }
    }

    [Fact]
    public async Task El_encargado_de_otro_edificio_no_ve_ni_toca_la_votacion()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var other = env.As(env.OtherManager);

        // 404 (no 403): no se revela que la votacion existe ni en que estado esta.
        Assert.Equal(404, Status(await other.GetAsync(open.Id, Ct)));
        Assert.Equal(404, Status(await other.CloseAsync(open.Id, Ct)));
        Assert.Equal(404, Status(await other.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct)));
        Assert.Empty(Ok(await other.ListAsync(null, Ct)));
        Assert.Equal(403, Status(await other.ListAsync(env.Building.Id, Ct)));
        Assert.Equal(403, Status(await other.CreateAsync(env.Request(), Ct)));
    }

    // ── Historial ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Votar_cambiar_y_quitar_deja_cada_paso_en_el_historial()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var service = env.As(env.Manager);
        var unit = env.Units[0];
        var yes = open.Options[0].Id;
        var no = open.Options[1].Id;

        Ok(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = unit.Id, VoteOptionId = yes }, Ct));
        // Repetir la misma opcion no cambia nada ni deja evento.
        Ok(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = unit.Id, VoteOptionId = yes }, Ct));
        var changed = Ok(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = unit.Id, VoteOptionId = no }, Ct));
        var castId = changed.Units.Single(u => u.UnitId == unit.Id).CastId!.Value;
        var removed = Ok(await service.RemoveCastAsync(open.Id, castId, Ct));

        Assert.Equal(0, removed.ParticipatingUnits);
        Assert.Equal(3, removed.Units.Single(u => u.UnitId == unit.Id).HistoryCount);

        var history = Ok(await service.GetHistoryAsync(open.Id, unit.Id, Ct)).OrderBy(x => x.OccurredAtUtc).ToList();
        Assert.Equal(["Cast", "Change", "Remove"], history.Select(x => x.Action));
        Assert.Equal([null, "A favor", "En contra"], history.Select(x => x.PreviousOptionLabel));
        Assert.Equal(["A favor", "En contra", null], history.Select(x => x.NewOptionLabel));
        Assert.All(history, x => Assert.Equal("Staff", x.Source));
        Assert.All(history, x => Assert.Equal(env.Manager.FullName, x.ActingUserName));
        // Titular de la unidad al momento del voto.
        Assert.All(history, x => Assert.Equal(env.Owner.FullName, x.OwnerUserName));

        // El voto vigente sigue el ultimo estado; el evento queda aunque el voto se haya quitado.
        Assert.Empty(env.T.NewContext().VoteCasts.Where(x => x.VoteId == open.Id));
        Assert.Equal(3, env.T.NewContext().VoteCastEvents.Count(x => x.VoteId == open.Id));
    }

    [Fact]
    public async Task El_historial_de_votos_no_se_puede_modificar_ni_borrar()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        Ok(await env.As(env.Manager).CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct));

        using var db = env.T.NewContext();
        var ev = db.VoteCastEvents.Single();
        ev.CoefficientWeight = 99m;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        using var db2 = env.T.NewContext();
        db2.VoteCastEvents.Remove(db2.VoteCastEvents.Single());
        Assert.Throws<InvalidOperationException>(() => db2.SaveChanges());
    }

    [Fact]
    public async Task No_se_vota_con_una_unidad_inactiva_ni_de_otro_edificio()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var service = env.As(env.Manager);
        env.Units[3].IsActive = false;
        env.T.Db.SaveChanges();
        var foreign = env.T.AddUnit(env.OtherBuilding, "X1");

        Assert.Equal(400, Status(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[3].Id, VoteOptionId = open.Options[0].Id }, Ct)));
        Assert.Equal(400, Status(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = foreign.Id, VoteOptionId = open.Options[0].Id }, Ct)));
        Assert.Equal(400, Status(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = Guid.NewGuid() }, Ct)));
    }

    // ── Resultado congelado ─────────────────────────────────────────────────

    [Fact]
    public async Task El_resultado_cerrado_no_cambia_aunque_cambien_las_unidades()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync(weight: "ByCoefficient");
        var service = env.As(env.Manager);
        var yes = open.Options[0].Id;
        foreach (var u in env.Units.Take(3))
            Ok(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = u.Id, VoteOptionId = yes }, Ct));

        var closed = Ok(await service.CloseAsync(open.Id, Ct));
        Assert.True(closed.ResultFrozen);
        Assert.Equal(yes, closed.ApprovedOptionId);
        Assert.Equal(4, closed.TotalUnits);
        Assert.Equal(1m, closed.TotalCoefficient);
        Assert.Equal(env.Manager.FullName, closed.ClosedByName);

        // Despues del cierre: cambia un coeficiente, se agrega una unidad y otra se desactiva.
        env.Units[0].Coefficient = 0.10m;
        env.Units[3].IsActive = false;
        env.T.AddUnit(env.Building, "U5");
        env.T.Db.SaveChanges();

        var later = Ok(await env.As(env.Manager).GetAsync(open.Id, Ct));
        Assert.Equal(4, later.TotalUnits);
        Assert.Equal(1m, later.TotalCoefficient);
        Assert.Equal(3, later.ParticipatingUnits);
        Assert.Equal(0.25m, later.Units.Single(u => u.UnitCode == "U1").Coefficient);
        Assert.DoesNotContain(later.Units, u => u.UnitCode == "U5");
        Assert.Equal(closed.Options.Select(o => o.Percentage), later.Options.Select(o => o.Percentage));
        Assert.Equal(yes, later.ApprovedOptionId);

        // El hash corresponde al JSON guardado.
        var vote = env.T.NewContext().Votes.Single(x => x.Id == open.Id);
        Assert.Equal(VoteService.ComputeHash(vote.ResultJson!), vote.ResultHash);
        Assert.Equal(later.ResultHash, vote.ResultHash);

        // En la lista tambien se usan los totales congelados.
        var listed = Ok(await env.As(env.Manager).ListAsync(env.Building.Id, Ct)).Single(x => x.Id == open.Id);
        Assert.Equal(4, listed.TotalUnits);
        Assert.Equal(3, listed.ParticipatingUnits);
        Assert.True(listed.ResultFrozen);
    }

    [Fact]
    public async Task Una_votacion_cerrada_ya_no_acepta_votos_ni_cambios()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var service = env.As(env.Manager);
        var cast = Ok(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[0].Id, VoteOptionId = open.Options[0].Id }, Ct));
        Ok(await service.CloseAsync(open.Id, Ct));

        Assert.Equal(400, Status(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[1].Id, VoteOptionId = open.Options[0].Id }, Ct)));
        Assert.Equal(400, Status(await service.RemoveCastAsync(open.Id, cast.Units[0].CastId!.Value, Ct)));
        Assert.Equal(400, Status(await service.CloseAsync(open.Id, Ct)));
        Assert.Equal(400, Status(await service.CancelAsync(open.Id, new VoteCancelRequest { Reason = "x" }, Ct)));
        Assert.Equal(400, Status(await service.UpdateAsync(open.Id, env.Request(), Ct)));
    }

    [Fact]
    public async Task Las_votaciones_cerradas_antes_del_congelamiento_se_siguen_mostrando_calculadas()
    {
        using var env = new VoteEnv();
        var vote = new Vote { BuildingId = env.Building.Id, Title = "Vieja", Status = VoteStatus.Closed, ClosedAtUtc = DateTime.UtcNow };
        vote.Options.Add(new VoteOption { Label = "Si", DisplayOrder = 0 });
        vote.Options.Add(new VoteOption { Label = "No", DisplayOrder = 1 });
        env.T.Db.Votes.Add(vote);
        env.T.Db.SaveChanges();

        var dto = Ok(await env.As(env.Manager).GetAsync(vote.Id, Ct));
        Assert.False(dto.ResultFrozen);
        Assert.Null(dto.ResultHash);
        Assert.Equal(4, dto.TotalUnits);
    }

    // ── Anulacion ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Anular_pide_motivo_conserva_los_votos_y_no_aprueba_nada()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync();
        var service = env.As(env.Manager);
        foreach (var u in env.Units.Take(3))
            Ok(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = u.Id, VoteOptionId = open.Options[0].Id }, Ct));

        Assert.Equal(400, Status(await service.CancelAsync(open.Id, new VoteCancelRequest { Reason = "  " }, Ct)));

        var cancelled = Ok(await service.CancelAsync(open.Id, new VoteCancelRequest { Reason = "Se convocó mal la asamblea" }, Ct));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Se convocó mal la asamblea", cancelled.CancelReason);
        Assert.Null(cancelled.ApprovedOptionId);
        Assert.Null(cancelled.WinningOptionId);
        Assert.Equal(3, cancelled.ParticipatingUnits);
        Assert.True(cancelled.ResultFrozen);

        Assert.Equal(400, Status(await service.CastAsync(open.Id, new VoteCastRequest { UnitId = env.Units[3].Id, VoteOptionId = open.Options[0].Id }, Ct)));
        Assert.Equal(3, env.T.NewContext().VoteCastEvents.Count(x => x.VoteId == open.Id));
    }

    [Fact]
    public async Task Un_borrador_no_se_anula_y_una_abierta_no_se_elimina()
    {
        using var env = new VoteEnv();
        var service = env.As(env.Manager);
        var draft = Ok(await service.CreateAsync(env.Request(), Ct));
        Assert.Equal(400, Status(await service.CancelAsync(draft.Id, new VoteCancelRequest { Reason = "x" }, Ct)));

        var open = Ok(await service.OpenAsync(draft.Id, Ct));
        Assert.Equal(400, Status(await service.DeleteAsync(open.Id, Ct)));
        Assert.Equal(400, Status(await service.OpenAsync(open.Id, Ct)));
    }

    // ── Validaciones e informativa ──────────────────────────────────────────

    [Fact]
    public async Task Valida_opciones_titulo_descripcion_y_tipos()
    {
        using var env = new VoteEnv();
        var service = env.As(env.Manager);

        Assert.Equal(400, Status(await service.CreateAsync(env.Request(options: ["Si", " si "]), Ct)));
        Assert.Equal(400, Status(await service.CreateAsync(env.Request(options: Enumerable.Range(1, 11).Select(i => $"Opción {i}").ToArray()), Ct)));
        Assert.Equal(400, Status(await service.CreateAsync(env.Request(options: ["Si", new string('x', 201)]), Ct)));
        Assert.Equal(400, Status(await service.CreateAsync(env.Request(options: ["Si", "   "]), Ct)));
        Assert.Equal(400, Status(await service.CreateAsync(env.Request(weight: "1"), Ct)));
        Assert.Equal(400, Status(await service.CreateAsync(env.Request(weight: "byunit"), Ct)));
        Assert.Equal(400, Status(await service.CreateAsync(env.Request(rule: "Absoluta"), Ct)));

        var longTitle = env.Request();
        longTitle.Title = new string('t', 201);
        Assert.Equal(400, Status(await service.CreateAsync(longTitle, Ct)));

        var longDescription = env.Request();
        longDescription.Description = new string('d', 1001);
        Assert.Equal(400, Status(await service.CreateAsync(longDescription, Ct)));

        // Las opciones vacias se descartan y el resto se guarda recortado.
        var ok = Ok(await service.CreateAsync(env.Request(options: [" Sí ", "", "No"]), Ct));
        Assert.Equal(["Sí", "No"], ok.Options.Select(o => o.Label));
        Assert.True(ok.IsInformative);
        Assert.Equal(10, VoteService.MaxOptions);
    }

    [Fact]
    public async Task No_se_abre_una_votacion_en_un_edificio_sin_unidades_activas()
    {
        using var env = new VoteEnv();
        foreach (var u in env.Units) u.IsActive = false;
        env.T.Db.SaveChanges();
        var service = env.As(env.Manager);
        var draft = Ok(await service.CreateAsync(env.Request(), Ct));
        Assert.Equal(400, Status(await service.OpenAsync(draft.Id, Ct)));
    }

    [Fact]
    public async Task Los_estados_y_tipos_se_guardan_como_el_texto_de_siempre()
    {
        using var env = new VoteEnv();
        var open = await env.OpenVoteAsync(weight: "ByCoefficient", rule: "DoubleAbsolute");
        var conn = env.T.Db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT Status || '|' || WeightType || '|' || MajorityRule FROM Votes WHERE Id = '{open.Id.ToString().ToUpperInvariant()}'";
        Assert.Equal("Open|ByCoefficient|DoubleAbsolute", cmd.ExecuteScalar());
    }
}
