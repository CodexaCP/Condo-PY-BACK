using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Condo.Tests.Marketplace;

public class MarketplaceReservationServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly FakeAccessScope _access;

    private readonly Company _company;
    private readonly Building _building;
    private readonly Unit _unit;            // 302: Juan publica
    private readonly Unit _otherUnit;       // 101: vecinos que reservan
    private readonly ApplicationUser _juan;
    private readonly ApplicationUser _pedro;
    private readonly ApplicationUser _maria;
    private readonly MarketplaceListing _listing;   // 2030-01-01 19:00 -> 2030-01-02 05:00 (10 h), Gs. 20.000 por hora

    private int _timeoutMinutes = 10;
    private DateTime Start(int hour, int minute = 0) => _listing.WindowStartUtc.Date.AddHours(hour).AddMinutes(minute);

    public MarketplaceReservationServiceTests()
    {
        _access = new FakeAccessScope(_tenant);
        _company = _t.AddCompany();
        var admin = _t.AddUser(_company, UserRole.CompanyAdmin);
        _building = _t.AddBuilding(_company, "Edificio A");
        _unit = _t.AddUnit(_building, "302");
        _otherUnit = _t.AddUnit(_building, "101");
        _t.AssignPlan(_building, admin, includesMarketplace: true);
        _t.EnableMarketplace(_building);

        _juan = _t.AddUser(_company, name: "Juan");
        _t.AddOwner(_unit, _juan, primary: true);
        _pedro = _t.AddUser(_company, UserRole.Resident, "Pedro");
        _t.AddResident(_otherUnit, _pedro);
        _maria = _t.AddUser(_company, UserRole.Resident, "Maria");
        _t.AddResident(_otherUnit, _maria);

        _listing = _t.AddListing(_building, _unit, _juan, 20_000m);
        LoginAs(_pedro, "Resident");
    }

    public void Dispose() => _t.Dispose();

    private void LoginAs(ApplicationUser user, string role)
    {
        _tenant.UserId = user.Id;
        _tenant.Role = role;
        _tenant.CompanyId = user.CompanyId;
    }


    private MarketplaceReservationService Svc()
    {
        var db = _t.NewContext();
        var gate = new MarketplaceModuleGate(db);
        var scope = new MarketplaceScope(db, _tenant, _access, gate);
        var audit = new MarketplaceAudit(db, _tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var listings = new MarketplaceListingService((CondoDbContext)db, _tenant, scope, audit);
        var push = new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance);
        var options = Options.Create(new MarketplaceOptions { PaymentTimeoutMinutes = _timeoutMinutes });
        return new MarketplaceReservationService((CondoDbContext)db, _tenant, scope, audit, listings, push, options);
    }

    private MarketplaceQuoteRequest Req(int fromHour, int toHour, MarketplaceListing? listing = null) => new()
    {
        ListingId = (listing ?? _listing).Id,
        StartsAtUtc = Start(fromHour),
        EndsAtUtc = Start(toHour)
    };

    private async Task<MarketplaceReservationDto> Reserve(int fromHour, int toHour)
    {
        var result = await Svc().ReserveAsync(Req(fromHour, toHour), CancellationToken.None);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    // Deja vencido el plazo de una reserva SIN que el proceso de fondo haya corrido todavia.
    private void MakeStale(Guid reservationId)
    {
        var db = _t.NewContext();
        var reservation = db.MarketplaceReservations.Single(x => x.Id == reservationId);
        reservation.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        db.SaveChanges();
    }

    // ── Explorar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_vecino_ve_las_publicaciones_de_otros_con_precio_ventana_y_comision()
    {
        var items = (await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!;

        var item = Assert.Single(items);
        Assert.Equal("Cochera 12", item.Title);
        Assert.Equal("302", item.UnitCode);
        Assert.Equal(20_000m, item.HourlyPrice);
        Assert.Equal(10m, item.CommissionPercent);
        Assert.Empty(item.Occupied);
        Assert.Equal(_listing.WindowStartUtc, item.WindowStartUtc);
    }

    [Fact]
    public async Task El_propietario_no_ve_su_propia_publicacion_en_explorar()
    {
        LoginAs(_juan, "Owner");

        Assert.Empty((await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task Un_usuario_de_otro_edificio_no_puede_explorar()
    {
        var otherBuilding = _t.AddBuilding(_company);
        var stranger = _t.AddUser(_company, UserRole.Resident);
        _t.AddResident(_t.AddUnit(otherBuilding), stranger);
        LoginAs(stranger, "Resident");

        AssertError(await Svc().ExploreAsync(_building.Id, CancellationToken.None), 404);
    }

    [Fact]
    public async Task Un_usuario_de_otra_empresa_no_puede_explorar_ni_conoce_que_existe()
    {
        var otherCompany = _t.AddCompany();
        var intruder = _t.AddUser(otherCompany, UserRole.Resident);
        LoginAs(intruder, "Resident");

        AssertError(await Svc().ExploreAsync(_building.Id, CancellationToken.None), 404);
    }

    [Fact]
    public async Task Cada_edificio_ve_solo_sus_publicaciones()
    {
        var otherBuilding = _t.AddBuilding(_company, "Edificio B");
        var otherOwnerUnit = _t.AddUnit(otherBuilding);
        _t.AssignPlan(otherBuilding, _t.AddUser(_company, UserRole.CompanyAdmin), includesMarketplace: true);
        _t.EnableMarketplace(otherBuilding);
        var otherOwner = _t.AddUser(_company);
        _t.AddOwner(otherOwnerUnit, otherOwner, primary: true);
        _t.AddListing(otherBuilding, otherOwnerUnit, otherOwner);
        // Pedro vive tambien en el edificio B.
        _t.AddResident(_t.AddUnit(otherBuilding), _pedro);

        var a = (await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!;
        var b = (await Svc().ExploreAsync(otherBuilding.Id, CancellationToken.None)).Value!;

        Assert.Equal(_listing.Id, Assert.Single(a).ListingId);
        Assert.NotEqual(_listing.Id, Assert.Single(b).ListingId);
    }

    [Fact]
    public async Task El_personal_que_no_es_vecino_no_explora()
    {
        var manager = _t.AddUser(_company, UserRole.BuildingManager);
        LoginAs(manager, "BuildingManager");
        _access.Buildings.Add(_building.Id);

        AssertError(await Svc().ExploreAsync(_building.Id, CancellationToken.None), 403);
    }

    [Fact]
    public async Task Las_publicaciones_pausadas_cerradas_o_terminadas_no_se_listan()
    {
        var db = _t.NewContext();
        var listing = db.MarketplaceListings.Single();
        listing.Status = MarketplaceListingStatus.Suspended;
        db.SaveChanges();
        Assert.Empty((await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!);

        listing.Status = MarketplaceListingStatus.Closed;
        db.SaveChanges();
        Assert.Empty((await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!);

        listing.Status = MarketplaceListingStatus.Active;
        listing.WindowStartUtc = DateTime.UtcNow.Date.AddDays(-2).AddHours(10);
        listing.WindowEndUtc = DateTime.UtcNow.Date.AddDays(-2).AddHours(12);
        db.SaveChanges();
        Assert.Empty((await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task Una_publicacion_cuyo_dueno_dejo_de_ser_el_principal_ya_no_se_ofrece()
    {
        var link = _t.Db.UnitOwners.Single(x => x.OwnerId == _juan.Id);
        link.IsPrimary = false;
        _t.Db.SaveChanges();

        Assert.Empty((await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!);
        Assert.Equal(MarketplaceListingStatus.Suspended, _t.NewContext().MarketplaceListings.Single().Status);
    }

    [Fact]
    public async Task Lo_ya_reservado_aparece_como_ocupado_y_una_publicacion_llena_desaparece()
    {
        await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        var partial = (await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!;
        var occupied = Assert.Single(Assert.Single(partial).Occupied);
        Assert.Equal(Start(19), occupied.StartUtc);
        Assert.Equal(Start(22), occupied.EndUtc);

        // Maria toma el resto de la ventana (22:00 a 05:00 del dia siguiente): la publicacion queda llena.
        var rest = new MarketplaceQuoteRequest { ListingId = _listing.Id, StartsAtUtc = Start(22), EndsAtUtc = Start(29) };
        Assert.True((await Svc().ReserveAsync(rest, CancellationToken.None)).Ok);
        LoginAs(_pedro, "Resident");
        Assert.Empty((await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task Una_reserva_vencida_no_cuenta_como_ocupada_aunque_el_proceso_no_haya_corrido()
    {
        var reservation = await Reserve(19, 22);
        MakeStale(reservation.Id);
        LoginAs(_maria, "Resident");

        var item = Assert.Single((await Svc().ExploreAsync(_building.Id, CancellationToken.None)).Value!);

        Assert.Empty(item.Occupied);
    }

    // ── Cotizar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_desglose_de_3_horas_a_20000_con_10_por_ciento_es_66000()
    {
        var quote = (await Svc().QuoteAsync(Req(19, 22), CancellationToken.None)).Value!;

        Assert.Equal(3, quote.Hours);
        Assert.Equal(20_000m, quote.HourlyPrice);
        Assert.Equal(60_000m, quote.BaseAmount);
        Assert.Equal(10m, quote.CommissionPercent);
        Assert.Equal(6_000m, quote.CommissionAmount);
        Assert.Equal(66_000m, quote.TotalAmount);
    }

    [Fact]
    public async Task La_comision_del_edificio_es_la_que_manda_en_la_cotizacion()
    {
        var db = _t.NewContext();
        db.Buildings.Single(x => x.Id == _building.Id).MarketplaceCommissionPercent = 12m;
        db.SaveChanges();

        var quote = (await Svc().QuoteAsync(Req(19, 22), CancellationToken.None)).Value!;

        Assert.Equal(7_200m, quote.CommissionAmount);
        Assert.Equal(67_200m, quote.TotalAmount);
    }

    [Fact]
    public async Task El_precio_siempre_sale_de_la_publicacion_no_del_cliente()
    {
        var db = _t.NewContext();
        db.MarketplaceListings.Single().HourlyPrice = 30_000m;
        db.SaveChanges();

        // La solicitud no tiene ningun campo de precio: lo unico que se puede mandar es publicacion y horario.
        var quote = (await Svc().QuoteAsync(Req(19, 22), CancellationToken.None)).Value!;

        Assert.Equal(30_000m, quote.HourlyPrice);
        Assert.Equal(99_000m, quote.TotalAmount);
    }

    [Theory]
    [InlineData(18, 21)]    // empieza antes de la ventana
    [InlineData(23, 31)]    // termina despues de la ventana
    public async Task Un_horario_fuera_de_la_ventana_se_rechaza(int from, int to)
    {
        AssertError(await Svc().QuoteAsync(Req(from, to), CancellationToken.None), 400);
    }

    [Fact]
    public async Task Una_reserva_de_hora_y_media_o_con_minutos_sueltos_se_rechaza()
    {
        var oneAndHalf = new MarketplaceQuoteRequest { ListingId = _listing.Id, StartsAtUtc = Start(19), EndsAtUtc = Start(20, 30) };
        var loose = new MarketplaceQuoteRequest { ListingId = _listing.Id, StartsAtUtc = Start(19, 15), EndsAtUtc = Start(21, 15) };

        AssertError(await Svc().QuoteAsync(oneAndHalf, CancellationToken.None), 400);
        AssertError(await Svc().QuoteAsync(loose, CancellationToken.None), 400);
    }

    [Fact]
    public async Task Se_puede_reservar_desde_y_media_con_horas_enteras()
    {
        var request = new MarketplaceQuoteRequest { ListingId = _listing.Id, StartsAtUtc = Start(19, 30), EndsAtUtc = Start(21, 30) };

        var quote = (await Svc().QuoteAsync(request, CancellationToken.None)).Value!;

        Assert.Equal(2, quote.Hours);
        Assert.Equal(44_000m, quote.TotalAmount);
    }

    [Fact]
    public async Task Cotizar_un_horario_ya_tomado_responde_conflicto()
    {
        await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        AssertError(await Svc().QuoteAsync(Req(20, 23), CancellationToken.None), 409);
    }

    [Fact]
    public async Task No_se_puede_cotizar_ni_reservar_la_propia_publicacion()
    {
        LoginAs(_juan, "Owner");

        AssertError(await Svc().QuoteAsync(Req(19, 22), CancellationToken.None), 403);
        AssertError(await Svc().ReserveAsync(Req(19, 22), CancellationToken.None), 403);
    }

    [Fact]
    public async Task No_se_reserva_una_publicacion_pausada()
    {
        var db = _t.NewContext();
        db.MarketplaceListings.Single().Status = MarketplaceListingStatus.Suspended;
        db.SaveChanges();

        AssertError(await Svc().ReserveAsync(Req(19, 22), CancellationToken.None), 409);
    }

    // ── Reservar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reservar_crea_la_operacion_con_importes_congelados_y_plazo_de_pago()
    {
        var before = DateTime.UtcNow;
        var dto = await Reserve(19, 22);

        Assert.Equal("MP-00000001", dto.Reference);
        Assert.Equal("PendingPayment", dto.Status);
        Assert.Equal(3, dto.Hours);
        Assert.Equal(60_000m, dto.BaseAmount);
        Assert.Equal(6_000m, dto.CommissionAmount);
        Assert.Equal(66_000m, dto.TotalAmount);
        Assert.Equal("Cochera 12", dto.Title);
        Assert.Equal("302", dto.UnitCode);
        Assert.InRange(dto.ExpiresAtUtc!.Value, before.AddMinutes(10).AddSeconds(-5), DateTime.UtcNow.AddMinutes(10).AddSeconds(5));

        var saved = _t.NewContext().MarketplaceReservations.Single();
        Assert.Equal(_pedro.Id, saved.BuyerUserId);
        Assert.Equal(_juan.Id, saved.OwnerId);                 // el propietario queda congelado
        Assert.Equal(60_000m, saved.OwnerNetAmount);
        Assert.Equal(MarketplaceCreditStatus.None, saved.CreditStatus);
        Assert.Equal(6, _t.NewContext().MarketplaceReservationSlots.Count(x => x.ReservationId == saved.Id && !x.IsDeleted));
    }

    [Fact]
    public async Task El_plazo_para_pagar_sale_de_la_configuracion_no_esta_fijo_en_el_codigo()
    {
        _timeoutMinutes = 25;

        var dto = await Reserve(19, 22);

        Assert.InRange((dto.ExpiresAtUtc!.Value - DateTime.UtcNow).TotalMinutes, 24, 25.1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(500)]
    public void Un_plazo_invalido_vuelve_al_valor_por_defecto(int configured)
    {
        Assert.Equal(10, new MarketplaceOptions { PaymentTimeoutMinutes = configured }.EffectivePaymentTimeoutMinutes);
        Assert.Equal(45, new MarketplaceOptions { PaymentTimeoutMinutes = 45 }.EffectivePaymentTimeoutMinutes);
    }

    [Fact]
    public async Task Reservar_deja_un_evento_de_auditoria_con_los_importes()
    {
        var dto = await Reserve(19, 22);

        var evt = _t.NewContext().MarketplaceEvents.Single(x => x.EntityId == dto.Id);
        Assert.Equal("reservation.created", evt.Action);
        Assert.Equal(_pedro.Id, evt.UserId);
        Assert.Equal("PendingPayment", evt.ToStatus);
        Assert.Contains("66000", evt.DataJson);
        Assert.Contains("60000", evt.DataJson);
    }

    [Fact]
    public async Task El_numero_de_operacion_es_correlativo_por_empresa()
    {
        var first = await Reserve(19, 22);
        LoginAs(_maria, "Resident");
        var second = await Reserve(22, 24);

        Assert.Equal("MP-00000001", first.Reference);
        Assert.Equal("MP-00000002", second.Reference);
    }

    // ── Doble reserva ────────────────────────────────────────────────────────

    [Fact]
    public async Task Dos_usuarios_no_pueden_reservar_el_mismo_horario()
    {
        await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        AssertError(await Svc().ReserveAsync(Req(19, 22), CancellationToken.None), 409);
        Assert.Single(_t.NewContext().MarketplaceReservations);
    }

    [Theory]
    [InlineData(18, 20)]    // ya fuera de ventana: 400, no 409
    [InlineData(20, 21)]    // contenido
    [InlineData(21, 24)]    // pisa el final
    public async Task Un_solapamiento_parcial_no_deja_dos_reservas_para_el_mismo_horario(int from, int to)
    {
        await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        var result = await Svc().ReserveAsync(Req(from, to), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Single(_t.NewContext().MarketplaceReservations);
        Assert.Equal(6, _t.NewContext().MarketplaceReservationSlots.Count());
    }

    [Fact]
    public async Task Una_reserva_justo_a_continuacion_no_choca()
    {
        await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        var next = await Reserve(22, 23);

        Assert.Equal("PendingPayment", next.Status);
        Assert.Equal(8, _t.NewContext().MarketplaceReservationSlots.Count());
    }

    [Fact]
    public async Task Un_comprador_no_puede_tener_dos_reservas_esperando_pago()
    {
        await Reserve(19, 22);

        var result = await Svc().ReserveAsync(Req(23, 25), CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("esperando pago", result.Error!.Message);
        Assert.Single(_t.NewContext().MarketplaceReservations);
    }

    [Fact]
    public async Task Un_usuario_que_no_vive_en_el_edificio_no_puede_reservar()
    {
        var otherBuilding = _t.AddBuilding(_company);
        var stranger = _t.AddUser(_company, UserRole.Resident);
        _t.AddResident(_t.AddUnit(otherBuilding), stranger);
        LoginAs(stranger, "Resident");

        AssertError(await Svc().ReserveAsync(Req(19, 22), CancellationToken.None), 404);
        Assert.Empty(_t.NewContext().MarketplaceReservations);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_no_se_puede_reservar()
    {
        _t.EnableMarketplace(_building, enabled: false);

        var result = await Svc().ReserveAsync(Req(19, 22), CancellationToken.None);

        AssertError(result, 403);
        Assert.Equal(MarketplaceModuleGate.DisabledCode, result.Error!.Code);
    }

    [Fact]
    public async Task Cambiar_precio_y_comision_despues_no_toca_una_reserva_ya_hecha()
    {
        var dto = await Reserve(19, 22);
        var db = _t.NewContext();
        db.MarketplaceListings.Single().HourlyPrice = 99_000m;
        db.Buildings.Single(x => x.Id == _building.Id).MarketplaceCommissionPercent = 50m;
        db.SaveChanges();

        var saved = _t.NewContext().MarketplaceReservations.Single(x => x.Id == dto.Id);
        Assert.Equal(20_000m, saved.HourlyPrice);
        Assert.Equal(66_000m, saved.TotalAmount);
        Assert.Equal(10m, saved.CommissionPercent);
    }

    // ── Vencimiento ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_reserva_vencida_libera_el_horario_y_otro_vecino_puede_reservarlo()
    {
        var first = await Reserve(19, 22);
        MakeStale(first.Id);
        LoginAs(_maria, "Resident");

        var second = await Reserve(19, 22);

        Assert.Equal("PendingPayment", second.Status);
        var oldOne = _t.NewContext().MarketplaceReservations.Single(x => x.Id == first.Id);
        Assert.Equal(MarketplaceReservationStatus.Expired, oldOne.Status);
        Assert.Equal(6, _t.NewContext().MarketplaceReservationSlots.Count(x => !x.IsDeleted));
    }

    [Fact]
    public async Task Al_vencer_se_avisa_al_comprador_y_queda_en_la_auditoria_como_hecho_por_el_sistema()
    {
        var first = await Reserve(19, 22);
        MakeStale(first.Id);
        LoginAs(_maria, "Resident");
        await Reserve(19, 22);

        var notification = _t.NewContext().Notifications.Single(x => x.RecipientId == _pedro.Id);
        Assert.Equal(NotificationType.MarketplaceReservationExpired, notification.Type);
        Assert.Equal(first.Id, notification.EntityId);
        var evt = _t.NewContext().MarketplaceEvents.Single(x => x.Action == "reservation.expired");
        Assert.Null(evt.UserId);
        Assert.Equal("Expired", evt.ToStatus);
    }

    [Fact]
    public async Task Un_comprador_con_su_reserva_vencida_puede_hacer_otra_sin_esperar_al_proceso_de_fondo()
    {
        var first = await Reserve(19, 22);
        MakeStale(first.Id);

        var second = await Reserve(23, 25);

        Assert.Equal("PendingPayment", second.Status);
    }

    [Fact]
    public async Task El_proceso_de_fondo_vence_solo_lo_vencido_y_es_repetible()
    {
        var stale = await Reserve(19, 22);
        LoginAs(_maria, "Resident");
        var fresh = await Reserve(22, 24);
        MakeStale(stale.Id);

        Assert.Equal(1, await Svc().ExpireStaleAsync(CancellationToken.None));
        Assert.Equal(0, await Svc().ExpireStaleAsync(CancellationToken.None));

        var db = _t.NewContext();
        Assert.Equal(MarketplaceReservationStatus.Expired, db.MarketplaceReservations.Single(x => x.Id == stale.Id).Status);
        Assert.Equal(MarketplaceReservationStatus.PendingPayment, db.MarketplaceReservations.Single(x => x.Id == fresh.Id).Status);
        Assert.Single(db.Notifications.Where(x => x.Type == NotificationType.MarketplaceReservationExpired).ToList());
    }

    [Fact]
    public async Task El_proceso_de_fondo_no_toca_reservas_ya_en_revision_aunque_pase_el_plazo()
    {
        var dto = await Reserve(19, 22);
        var db = _t.NewContext();
        var reservation = db.MarketplaceReservations.Single(x => x.Id == dto.Id);
        reservation.Status = MarketplaceReservationStatus.InReview;
        reservation.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-30);
        db.SaveChanges();

        Assert.Equal(0, await Svc().ExpireStaleAsync(CancellationToken.None));
        Assert.Equal(MarketplaceReservationStatus.InReview, _t.NewContext().MarketplaceReservations.Single().Status);
    }

    [Fact]
    public async Task Los_bloques_liberados_al_vencer_quedan_libres_en_la_base()
    {
        var dto = await Reserve(19, 22);
        MakeStale(dto.Id);

        await Svc().ExpireStaleAsync(CancellationToken.None);

        Assert.Equal(0, _t.NewContext().MarketplaceReservationSlots.Count(x => !x.IsDeleted));
    }

    // ── Mis reservas y cancelar ──────────────────────────────────────────────

    [Fact]
    public async Task Mis_reservas_solo_muestra_las_mias_y_las_vencidas_se_ven_como_vencidas()
    {
        var mine = await Reserve(19, 22);
        LoginAs(_maria, "Resident");
        await Reserve(22, 24);
        MakeStale(mine.Id);

        LoginAs(_pedro, "Resident");
        var list = (await Svc().GetMineAsync(_building.Id, CancellationToken.None)).Value!;

        var only = Assert.Single(list);
        Assert.Equal(mine.Id, only.Id);
        Assert.Equal("Expired", only.Status);
    }

    [Fact]
    public async Task El_comprador_cancela_una_reserva_sin_pagar_y_el_horario_queda_libre()
    {
        var dto = await Reserve(19, 22);

        var cancelled = (await Svc().CancelPendingAsync(dto.Id, CancellationToken.None)).Value!;

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Null(cancelled.ExpiresAtUtc);
        Assert.Equal(0, _t.NewContext().MarketplaceReservationSlots.Count(x => !x.IsDeleted));
        var evt = _t.NewContext().MarketplaceEvents.Single(x => x.Action == "reservation.cancelled");
        Assert.Equal(_pedro.Id, evt.UserId);

        // Maria ya puede tomar ese horario y Pedro puede reservar de nuevo.
        LoginAs(_maria, "Resident");
        await Reserve(19, 22);
        LoginAs(_pedro, "Resident");
        await Reserve(22, 24);
    }

    [Fact]
    public async Task Otro_vecino_no_puede_cancelar_mi_reserva()
    {
        var dto = await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        AssertError(await Svc().CancelPendingAsync(dto.Id, CancellationToken.None), 404);
        Assert.Equal(MarketplaceReservationStatus.PendingPayment, _t.NewContext().MarketplaceReservations.Single().Status);
    }

    [Fact]
    public async Task Una_reserva_ya_en_revision_o_confirmada_no_se_cancela_desde_aca()
    {
        var dto = await Reserve(19, 22);
        var db = _t.NewContext();
        db.MarketplaceReservations.Single(x => x.Id == dto.Id).Status = MarketplaceReservationStatus.InReview;
        db.SaveChanges();

        AssertError(await Svc().CancelPendingAsync(dto.Id, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Cancelar_una_reserva_vencida_responde_que_ya_vencio()
    {
        var dto = await Reserve(19, 22);
        var db = _t.NewContext();
        db.MarketplaceReservations.Single(x => x.Id == dto.Id).Status = MarketplaceReservationStatus.Expired;
        db.SaveChanges();

        var result = await Svc().CancelPendingAsync(dto.Id, CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("venció", result.Error!.Message);
    }
}
