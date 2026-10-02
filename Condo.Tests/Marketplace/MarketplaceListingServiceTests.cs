using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Marketplace;

public class MarketplaceListingServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly FakeAccessScope _access;

    private readonly Company _company;
    private readonly Building _building;
    private readonly Unit _unit;          // 302: Juan es el principal
    private readonly Unit _otherUnit;     // 101
    private readonly ApplicationUser _juan;
    private readonly ApplicationUser _pedro;   // residente, no propietario

    public MarketplaceListingServiceTests()
    {
        _access = new FakeAccessScope(_tenant);
        _company = _t.AddCompany();
        var admin = _t.AddUser(_company, UserRole.CompanyAdmin);
        _building = _t.AddBuilding(_company);
        _unit = _t.AddUnit(_building, "302");
        _otherUnit = _t.AddUnit(_building, "101");
        _t.AssignPlan(_building, admin, includesMarketplace: true);
        _t.EnableMarketplace(_building);

        _juan = _t.AddUser(_company, name: "Juan");
        _t.AddOwner(_unit, _juan, primary: true);
        _pedro = _t.AddUser(_company, UserRole.Resident, "Pedro");
        _t.AddResident(_unit, _pedro);

        LoginAs(_juan, "Owner");
    }

    public void Dispose() => _t.Dispose();

    private void LoginAs(ApplicationUser user, string role)
    {
        _tenant.UserId = user.Id;
        _tenant.Role = role;
        _tenant.CompanyId = user.CompanyId;
    }

    private MarketplaceListingService Svc()
    {
        var db = _t.NewContext();
        var gate = new MarketplaceModuleGate(db);
        var scope = new MarketplaceScope(db, _tenant, _access, gate);
        var audit = new MarketplaceAudit(db, _tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        return new MarketplaceListingService((Condo.Infrastructure.Persistence.CondoDbContext)db, _tenant, scope, audit);
    }

    // Dia futuro a una hora en punto (UTC).
    private static DateTime Day(int daysAhead, int hour, int minute = 0) =>
        DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(hour).AddMinutes(minute);

    private MarketplaceListingCreateRequest Request(Unit? unit = null, int dayAhead = 3, int fromHour = 19, int toHour = 22, decimal price = 20_000m) => new()
    {
        BuildingId = _building.Id,
        UnitId = (unit ?? _unit).Id,
        Title = "Cochera 12",
        WindowStartUtc = Day(dayAhead, fromHour),
        WindowEndUtc = Day(dayAhead, toHour),
        HourlyPrice = price
    };

    private async Task<MarketplaceListingDto> Publish(MarketplaceListingCreateRequest? request = null)
    {
        var result = await Svc().CreateAsync(request ?? Request(), CancellationToken.None);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    // ── Publicar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_propietario_principal_publica_su_unidad()
    {
        var dto = await Publish();

        Assert.Equal("Cochera 12", dto.Title);
        Assert.Equal("302", dto.UnitCode);
        Assert.Equal("Active", dto.Status);
        Assert.Equal(3, dto.WindowHours);
        Assert.Equal(20_000m, dto.HourlyPrice);
        Assert.Equal(_juan.Id, dto.OwnerId);
        Assert.Equal(0, dto.ActiveReservations);
        Assert.False(dto.WindowEnded);

        var saved = _t.NewContext().MarketplaceListings.Single();
        Assert.Equal(_company.Id, saved.CompanyId);
        Assert.Equal(_building.Id, saved.BuildingId);
    }

    [Fact]
    public async Task Publicar_deja_un_evento_de_auditoria_con_quien_lo_hizo()
    {
        var dto = await Publish();

        var evt = _t.NewContext().MarketplaceEvents.Single(x => x.EntityId == dto.Id);
        Assert.Equal("listing.created", evt.Action);
        Assert.Equal(_juan.Id, evt.UserId);
        Assert.Equal("Active", evt.ToStatus);
        Assert.Contains("20000", evt.DataJson);
    }

    [Fact]
    public async Task Un_residente_que_no_es_propietario_no_puede_publicar_la_unidad()
    {
        LoginAs(_pedro, "Resident");

        var result = await Svc().CreateAsync(Request(), CancellationToken.None);

        AssertError(result, 403);
        Assert.Empty(_t.NewContext().MarketplaceListings);
    }

    [Fact]
    public async Task Un_copropietario_que_no_es_el_principal_no_puede_publicar()
    {
        var copropietario = _t.AddUser(_company);
        _t.AddOwner(_unit, copropietario, primary: false);
        LoginAs(copropietario, "Owner");

        AssertError(await Svc().CreateAsync(Request(), CancellationToken.None), 403);
    }

    [Fact]
    public async Task Un_propietario_no_puede_publicar_una_unidad_que_no_es_suya()
    {
        // Juan es el principal de 302, pero no de 101.
        AssertError(await Svc().CreateAsync(Request(_otherUnit), CancellationToken.None), 403);
    }

    [Fact]
    public async Task No_se_publica_una_unidad_de_otro_edificio_mandando_su_id()
    {
        var otherBuilding = _t.AddBuilding(_company);
        var foreignUnit = _t.AddUnit(otherBuilding);
        _t.AddOwner(foreignUnit, _juan, primary: true);   // es suyo, pero de otro edificio

        // Se pide con el edificio A y la unidad del edificio B.
        AssertError(await Svc().CreateAsync(Request(foreignUnit), CancellationToken.None), 404);
    }

    [Fact]
    public async Task Un_usuario_de_otra_empresa_no_puede_publicar_en_este_edificio()
    {
        var otherCompany = _t.AddCompany();
        var intruder = _t.AddUser(otherCompany);
        LoginAs(intruder, "Owner");

        AssertError(await Svc().CreateAsync(Request(), CancellationToken.None), 404);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_no_se_puede_publicar()
    {
        _t.EnableMarketplace(_building, enabled: false);

        var result = await Svc().CreateAsync(Request(), CancellationToken.None);

        AssertError(result, 403);
        Assert.Equal(MarketplaceModuleGate.DisabledCode, result.Error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task El_titulo_es_obligatorio(string title)
    {
        var request = Request();
        request.Title = title;

        AssertError(await Svc().CreateAsync(request, CancellationToken.None), 400);
    }

    [Fact]
    public async Task El_titulo_no_puede_superar_200_caracteres()
    {
        var request = Request();
        request.Title = new string('a', 201);

        AssertError(await Svc().CreateAsync(request, CancellationToken.None), 400);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(20_000.5)]
    public async Task El_precio_por_hora_debe_ser_un_entero_mayor_que_cero(double price)
    {
        AssertError(await Svc().CreateAsync(Request(price: (decimal)price), CancellationToken.None), 400);
    }

    [Fact]
    public async Task El_titulo_se_guarda_sin_espacios_sobrantes()
    {
        var request = Request();
        request.Title = "  Cochera 12  ";

        Assert.Equal("Cochera 12", (await Publish(request)).Title);
    }

    // ── Ventana ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_ventana_puede_cruzar_la_medianoche()
    {
        var request = Request();
        request.WindowStartUtc = Day(3, 20);
        request.WindowEndUtc = Day(4, 3);

        var dto = await Publish(request);

        Assert.Equal(7, dto.WindowHours);
    }

    [Fact]
    public async Task Una_ventana_de_dos_horas_y_media_se_rechaza()
    {
        var request = Request();
        request.WindowStartUtc = Day(3, 10);
        request.WindowEndUtc = Day(3, 12, 30);

        AssertError(await Svc().CreateAsync(request, CancellationToken.None), 400);
    }

    [Fact]
    public async Task Una_ventana_en_el_pasado_se_rechaza()
    {
        var request = Request();
        request.WindowStartUtc = Day(-1, 10);
        request.WindowEndUtc = Day(-1, 12);

        AssertError(await Svc().CreateAsync(request, CancellationToken.None), 400);
    }

    // ── Una unidad, una publicacion por horario ──────────────────────────────

    [Fact]
    public async Task La_misma_unidad_no_puede_tener_dos_publicaciones_solapadas()
    {
        await Publish(Request(fromHour: 19, toHour: 22));

        var overlapping = Request(fromHour: 20, toHour: 23);
        AssertError(await Svc().CreateAsync(overlapping, CancellationToken.None), 409);
        Assert.Single(_t.NewContext().MarketplaceListings);
    }

    [Fact]
    public async Task Publicaciones_consecutivas_de_la_misma_unidad_si_se_pueden()
    {
        await Publish(Request(fromHour: 19, toHour: 22));

        await Publish(Request(fromHour: 22, toHour: 23));

        Assert.Equal(2, _t.NewContext().MarketplaceListings.Count());
    }

    [Fact]
    public async Task Otro_dia_de_la_misma_unidad_si_se_puede()
    {
        await Publish(Request(dayAhead: 3));
        await Publish(Request(dayAhead: 4));
    }

    [Fact]
    public async Task Una_publicacion_cerrada_no_bloquea_el_horario()
    {
        var first = await Publish();
        Assert.True((await Svc().CloseAsync(first.Id, "Ya no la presto", CancellationToken.None)).Ok);

        await Publish();
    }

    [Fact]
    public async Task Una_publicacion_suspendida_sigue_ocupando_el_horario()
    {
        var first = await Publish();
        Assert.True((await Svc().SuspendAsync(first.Id, null, CancellationToken.None)).Ok);

        AssertError(await Svc().CreateAsync(Request(), CancellationToken.None), 409);
    }

    // ── Editar ───────────────────────────────────────────────────────────────

    private static MarketplaceListingUpdateRequest UpdateOf(MarketplaceListingDto dto) => new()
    {
        Title = dto.Title,
        WindowStartUtc = dto.WindowStartUtc,
        WindowEndUtc = dto.WindowEndUtc,
        HourlyPrice = dto.HourlyPrice
    };

    [Fact]
    public async Task El_propietario_cambia_el_precio_y_queda_auditado_el_antes_y_el_despues()
    {
        var dto = await Publish();
        var update = UpdateOf(dto);
        update.HourlyPrice = 25_000m;

        var result = await Svc().UpdateAsync(dto.Id, update, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(25_000m, result.Value!.HourlyPrice);
        var evt = _t.NewContext().MarketplaceEvents.Single(x => x.Action == "listing.updated");
        Assert.Contains("20000", evt.DataJson);
        Assert.Contains("25000", evt.DataJson);
    }

    [Fact]
    public async Task Cambiar_el_precio_no_toca_los_importes_de_una_reserva_ya_hecha()
    {
        var dto = await Publish();
        var listing = _t.NewContext().MarketplaceListings.Single();
        var reservation = _t.NewReservation(listing, _pedro, status: MarketplaceReservationStatus.Confirmed);
        reservation.StartsAtUtc = dto.WindowStartUtc;
        reservation.EndsAtUtc = dto.WindowStartUtc.AddHours(3);
        _t.Db.MarketplaceReservations.Add(reservation);
        _t.Db.SaveChanges();

        var update = UpdateOf(dto);
        update.HourlyPrice = 99_000m;
        Assert.True((await Svc().UpdateAsync(dto.Id, update, CancellationToken.None)).Ok);

        var saved = _t.NewContext().MarketplaceReservations.Single();
        Assert.Equal(20_000m, saved.HourlyPrice);
        Assert.Equal(66_000m, saved.TotalAmount);
        Assert.Equal(60_000m, saved.OwnerNetAmount);
    }

    [Fact]
    public async Task Otro_usuario_no_puede_editar_una_publicacion_ajena()
    {
        var dto = await Publish();
        var other = _t.AddUser(_company);
        _t.AddOwner(_otherUnit, other, primary: true);
        LoginAs(other, "Owner");

        var update = UpdateOf(dto);
        update.Title = "Mía ahora";

        AssertError(await Svc().UpdateAsync(dto.Id, update, CancellationToken.None), 404);
        Assert.Equal("Cochera 12", _t.NewContext().MarketplaceListings.Single().Title);
    }

    [Fact]
    public async Task El_personal_no_edita_la_publicacion_de_un_propietario()
    {
        var dto = await Publish();
        var manager = _t.AddUser(_company, UserRole.BuildingManager);
        LoginAs(manager, "BuildingManager");
        _access.Buildings.Add(_building.Id);

        AssertError(await Svc().UpdateAsync(dto.Id, UpdateOf(dto), CancellationToken.None), 404);
    }

    [Fact]
    public async Task Cambiar_la_ventana_con_una_reserva_que_quedaria_afuera_se_rechaza()
    {
        var dto = await Publish(Request(fromHour: 19, toHour: 22));
        var listing = _t.NewContext().MarketplaceListings.Single();
        var reservation = _t.NewReservation(listing, _pedro, status: MarketplaceReservationStatus.Confirmed);
        reservation.StartsAtUtc = Day(3, 19);
        reservation.EndsAtUtc = Day(3, 22);
        _t.Db.MarketplaceReservations.Add(reservation);
        _t.Db.SaveChanges();

        var update = UpdateOf(dto);
        update.WindowStartUtc = Day(3, 20);   // la reserva empieza a las 19:00
        update.WindowEndUtc = Day(3, 22);

        AssertError(await Svc().UpdateAsync(dto.Id, update, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Ampliar_la_ventana_con_una_reserva_adentro_se_permite()
    {
        var dto = await Publish(Request(fromHour: 19, toHour: 22));
        var listing = _t.NewContext().MarketplaceListings.Single();
        var reservation = _t.NewReservation(listing, _pedro, status: MarketplaceReservationStatus.Confirmed);
        reservation.StartsAtUtc = Day(3, 19);
        reservation.EndsAtUtc = Day(3, 22);
        _t.Db.MarketplaceReservations.Add(reservation);
        _t.Db.SaveChanges();

        var update = UpdateOf(dto);
        update.WindowStartUtc = Day(3, 18);
        update.WindowEndUtc = Day(3, 23);

        Assert.True((await Svc().UpdateAsync(dto.Id, update, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task No_se_puede_pisar_otra_publicacion_de_la_unidad_al_cambiar_la_ventana()
    {
        await Publish(Request(fromHour: 22, toHour: 23));
        var first = await Publish(Request(fromHour: 19, toHour: 21));

        var update = UpdateOf(first);
        update.WindowEndUtc = Day(3, 23);   // ahora pisaria la de 22 a 23

        AssertError(await Svc().UpdateAsync(first.Id, update, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Una_publicacion_cerrada_no_se_edita()
    {
        var dto = await Publish();
        await Svc().CloseAsync(dto.Id, null, CancellationToken.None);

        AssertError(await Svc().UpdateAsync(dto.Id, UpdateOf(dto), CancellationToken.None), 409);
    }

    [Fact]
    public async Task Si_deja_de_ser_el_principal_ya_no_puede_editar()
    {
        var dto = await Publish();
        var link = _t.Db.UnitOwners.Single(x => x.OwnerId == _juan.Id);
        link.IsPrimary = false;
        _t.Db.SaveChanges();

        AssertError(await Svc().UpdateAsync(dto.Id, UpdateOf(dto), CancellationToken.None), 403);
    }

    // ── Suspender, reanudar, cerrar ──────────────────────────────────────────

    [Fact]
    public async Task El_propietario_suspende_y_reanuda_su_publicacion()
    {
        var dto = await Publish();

        var suspended = await Svc().SuspendAsync(dto.Id, "Voy a usar la cochera", CancellationToken.None);
        Assert.Equal("Suspended", suspended.Value!.Status);
        Assert.Equal("Voy a usar la cochera", suspended.Value.StatusReason);

        var resumed = await Svc().ResumeAsync(dto.Id, CancellationToken.None);
        Assert.Equal("Active", resumed.Value!.Status);
        Assert.Null(resumed.Value.StatusReason);

        var actions = _t.NewContext().MarketplaceEvents.OrderBy(x => x.TimestampUtc).Select(x => x.Action).ToList();
        Assert.Contains("listing.suspended", actions);
        Assert.Contains("listing.resumed", actions);
    }

    [Fact]
    public async Task No_se_suspende_dos_veces_ni_se_reanuda_una_activa()
    {
        var dto = await Publish();

        AssertError(await Svc().ResumeAsync(dto.Id, CancellationToken.None), 409);
        await Svc().SuspendAsync(dto.Id, null, CancellationToken.None);
        AssertError(await Svc().SuspendAsync(dto.Id, null, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Una_publicacion_cerrada_no_se_reabre()
    {
        var dto = await Publish();
        await Svc().CloseAsync(dto.Id, null, CancellationToken.None);

        AssertError(await Svc().ResumeAsync(dto.Id, CancellationToken.None), 409);
        AssertError(await Svc().SuspendAsync(dto.Id, null, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Con_reservas_vigentes_no_se_puede_cerrar()
    {
        var dto = await Publish();
        var listing = _t.NewContext().MarketplaceListings.Single();
        _t.Db.MarketplaceReservations.Add(_t.NewReservation(listing, _pedro, status: MarketplaceReservationStatus.Confirmed));
        _t.Db.SaveChanges();

        AssertError(await Svc().CloseAsync(dto.Id, null, CancellationToken.None), 409);
        Assert.Equal("Active", _t.NewContext().MarketplaceListings.Single().Status.ToString());
    }

    [Fact]
    public async Task Las_reservas_ya_terminadas_o_vencidas_no_impiden_cerrar()
    {
        var dto = await Publish();
        var listing = _t.NewContext().MarketplaceListings.Single();
        _t.Db.MarketplaceReservations.Add(_t.NewReservation(listing, _pedro, status: MarketplaceReservationStatus.Expired));
        _t.Db.SaveChanges();

        Assert.True((await Svc().CloseAsync(dto.Id, null, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Otro_usuario_no_puede_suspender_ni_cerrar_una_publicacion_ajena()
    {
        var dto = await Publish();
        var other = _t.AddUser(_company);
        _t.AddOwner(_otherUnit, other, primary: true);
        LoginAs(other, "Owner");

        AssertError(await Svc().SuspendAsync(dto.Id, null, CancellationToken.None), 404);
        AssertError(await Svc().CloseAsync(dto.Id, null, CancellationToken.None), 404);
    }

    [Fact]
    public async Task El_personal_del_edificio_puede_suspender_pero_debe_indicar_el_motivo()
    {
        var dto = await Publish();
        var manager = _t.AddUser(_company, UserRole.BuildingManager);
        LoginAs(manager, "BuildingManager");
        _access.Buildings.Add(_building.Id);

        AssertError(await Svc().SuspendAsync(dto.Id, null, CancellationToken.None), 400);

        var result = await Svc().SuspendAsync(dto.Id, "Uso indebido", CancellationToken.None);
        Assert.Equal("Suspended", result.Value!.Status);
        var evt = _t.NewContext().MarketplaceEvents.Single(x => x.Action == "listing.suspended");
        Assert.Equal(manager.Id, evt.UserId);
    }

    [Fact]
    public async Task El_personal_de_otro_edificio_no_puede_suspender()
    {
        var dto = await Publish();
        var otherBuilding = _t.AddBuilding(_company);
        var manager = _t.AddUser(_company, UserRole.BuildingManager);
        LoginAs(manager, "BuildingManager");
        _access.Buildings.Add(otherBuilding.Id);

        AssertError(await Svc().SuspendAsync(dto.Id, "x", CancellationToken.None), 404);
    }

    [Fact]
    public async Task No_se_reanuda_si_el_propietario_ya_no_es_el_principal()
    {
        var dto = await Publish();
        await Svc().SuspendAsync(dto.Id, null, CancellationToken.None);
        var link = _t.Db.UnitOwners.Single(x => x.OwnerId == _juan.Id);
        link.IsPrimary = false;
        _t.Db.SaveChanges();

        AssertError(await Svc().ResumeAsync(dto.Id, CancellationToken.None), 409);
    }

    // ── Titularidad: suspension automatica ───────────────────────────────────

    [Fact]
    public async Task Si_el_propietario_deja_de_ser_el_principal_su_publicacion_se_suspende_sola()
    {
        var dto = await Publish();
        var link = _t.Db.UnitOwners.Single(x => x.OwnerId == _juan.Id);
        link.IsPrimary = false;
        _t.Db.SaveChanges();

        var mine = (await Svc().GetMineAsync(_building.Id, CancellationToken.None)).Value!;

        var listing = Assert.Single(mine);
        Assert.Equal("Suspended", listing.Status);
        Assert.Contains("principal", listing.StatusReason);
        var evt = _t.NewContext().MarketplaceEvents.Single(x => x.Action == "listing.suspended");
        Assert.Null(evt.UserId);   // lo hizo el sistema, no una persona
        Assert.Equal(dto.Id, evt.EntityId);
    }

    [Fact]
    public async Task Si_el_principal_cambia_a_otra_persona_la_publicacion_del_anterior_se_suspende()
    {
        await Publish();
        var link = _t.Db.UnitOwners.Single(x => x.OwnerId == _juan.Id);
        link.IsPrimary = false;
        _t.AddOwner(_unit, _pedro, primary: true);

        var suspended = await Svc().SyncOwnershipAsync(_building.Id, CancellationToken.None);

        Assert.Equal(1, suspended);
    }

    [Fact]
    public async Task Si_sigue_siendo_el_principal_la_publicacion_no_se_toca()
    {
        await Publish();

        Assert.Equal(0, await Svc().SyncOwnershipAsync(_building.Id, CancellationToken.None));
        Assert.Equal("Active", _t.NewContext().MarketplaceListings.Single().Status.ToString());
    }

    // ── Consultas ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mis_publicaciones_solo_muestra_las_mias()
    {
        await Publish();
        var other = _t.AddUser(_company);
        _t.AddOwner(_otherUnit, other, primary: true);
        LoginAs(other, "Owner");
        var theirs = Request(_otherUnit);
        Assert.True((await Svc().CreateAsync(theirs, CancellationToken.None)).Ok);

        LoginAs(_juan, "Owner");
        var mine = (await Svc().GetMineAsync(_building.Id, CancellationToken.None)).Value!;

        Assert.Single(mine);
        Assert.Equal("302", mine[0].UnitCode);
    }

    [Fact]
    public async Task Las_unidades_publicables_son_solo_las_del_propietario_principal()
    {
        var units = (await Svc().GetPublishableUnitsAsync(_building.Id, CancellationToken.None)).Value!;

        Assert.Equal(["302"], units.Select(x => x.Code));

        LoginAs(_pedro, "Resident");
        Assert.Empty((await Svc().GetPublishableUnitsAsync(_building.Id, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task Solo_el_personal_ve_todas_las_publicaciones_del_edificio()
    {
        await Publish();

        AssertError(await Svc().GetAllForStaffAsync(_building.Id, CancellationToken.None), 403);

        var manager = _t.AddUser(_company, UserRole.BuildingManager);
        LoginAs(manager, "BuildingManager");
        _access.Buildings.Add(_building.Id);
        var all = (await Svc().GetAllForStaffAsync(_building.Id, CancellationToken.None)).Value!;

        Assert.Single(all);
        Assert.Equal("Juan Prueba", all[0].OwnerName);
    }

    [Fact]
    public async Task Las_consultas_de_un_edificio_ajeno_no_revelan_que_existe()
    {
        var otherCompany = _t.AddCompany();
        var otherBuilding = _t.AddBuilding(otherCompany);
        LoginAs(_juan, "Owner");

        AssertError(await Svc().GetMineAsync(otherBuilding.Id, CancellationToken.None), 404);
        AssertError(await Svc().GetPublishableUnitsAsync(otherBuilding.Id, CancellationToken.None), 404);
        AssertError(await Svc().GetAllForStaffAsync(otherBuilding.Id, CancellationToken.None), 404);
    }

    [Fact]
    public async Task Una_publicacion_con_la_ventana_terminada_se_marca_como_terminada()
    {
        var dto = await Publish();
        var listing = _t.Db.MarketplaceListings.Single(x => x.Id == dto.Id);
        listing.WindowStartUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        listing.WindowEndUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(12);
        _t.Db.SaveChanges();

        var mine = (await Svc().GetMineAsync(_building.Id, CancellationToken.None)).Value!;

        Assert.True(mine.Single().WindowEnded);
    }
}
