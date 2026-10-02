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

public class MarketplacePaymentServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly FakeAccessScope _access;
    private readonly StubOverdueService _overdue = new();

    private readonly Company _company;
    private readonly Building _building;
    private readonly Building _otherBuilding;
    private readonly Unit _unit;            // 302: Juan publica
    private readonly Unit _otherUnit;       // 101: Pedro y Maria
    private readonly ApplicationUser _juan;
    private readonly ApplicationUser _pedro;
    private readonly ApplicationUser _maria;
    private readonly ApplicationUser _manager;
    private readonly ApplicationUser _operator;
    private readonly ApplicationUser _companyAdmin;
    private readonly ApplicationUser _otherManager;
    private readonly ApplicationUser _superAdmin;
    private readonly MarketplaceListing _listing;

    private DateTime Start(int hour) => _listing.WindowStartUtc.Date.AddHours(hour);

    public MarketplacePaymentServiceTests()
    {
        _access = new FakeAccessScope(_tenant);
        _company = _t.AddCompany();
        _companyAdmin = _t.AddUser(_company, UserRole.CompanyAdmin);
        _building = _t.AddBuilding(_company, "Edificio A");
        _otherBuilding = _t.AddBuilding(_company, "Edificio B");
        _unit = _t.AddUnit(_building, "302");
        _otherUnit = _t.AddUnit(_building, "101");
        _t.AssignPlan(_building, _companyAdmin, includesMarketplace: true);
        _t.EnableMarketplace(_building);
        _t.SetTransferInfo(_building, "Banco Familiar · Cuenta 123456 · Titular: Administración · Alias: edificio.a");

        _juan = _t.AddUser(_company, name: "Juan");
        _t.AddOwner(_unit, _juan, primary: true);
        _pedro = _t.AddUser(_company, UserRole.Resident, "Pedro");
        _t.AddResident(_otherUnit, _pedro);
        _maria = _t.AddUser(_company, UserRole.Resident, "Maria");
        _t.AddResident(_otherUnit, _maria);

        _manager = _t.AddUser(_company, UserRole.BuildingManager, "Encargado");
        _t.AddBuildingAccess(_manager, _building);
        _operator = _t.AddUser(_company, UserRole.CompanyOperator, "Operador");
        _t.AddBuildingAccess(_operator, _building);
        _otherManager = _t.AddUser(_company, UserRole.BuildingManager, "OtroEncargado");
        _t.AddBuildingAccess(_otherManager, _otherBuilding);
        _superAdmin = _t.AddUser(null, UserRole.SuperAdmin, "Super");

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

    private void LoginAsStaff(ApplicationUser staff, string role)
    {
        LoginAs(staff, role);
        _access.Buildings.Clear();
        foreach (var access in _t.NewContext().UserBuildingAccesses.Where(x => x.ApplicationUserId == staff.Id && !x.IsDeleted))
        {
            _access.Buildings.Add(access.BuildingId);
        }
    }

    private (MarketplaceReservationService Reservations, MarketplacePaymentService Payments) Build()
    {
        var db = _t.NewContext();
        var gate = new MarketplaceModuleGate(db);
        var scope = new MarketplaceScope(db, _tenant, _access, gate);
        var audit = new MarketplaceAudit(db, _tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var listings = new MarketplaceListingService((CondoDbContext)db, _tenant, scope, audit);
        var push = new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance);
        var reservations = new MarketplaceReservationService((CondoDbContext)db, _tenant, scope, audit, listings, push,
            Options.Create(new MarketplaceOptions()));
        var payments = new MarketplacePaymentService((CondoDbContext)db, _tenant, scope, audit, push, _overdue);
        return (reservations, payments);
    }

    private MarketplaceReservationService Reservations() => Build().Reservations;
    private MarketplacePaymentService Payments() => Build().Payments;

    private async Task<MarketplaceReservationDto> Reserve(int fromHour, int toHour)
    {
        var result = await Reservations().ReserveAsync(new MarketplaceQuoteRequest
        {
            ListingId = _listing.Id, StartsAtUtc = Start(fromHour), EndsAtUtc = Start(toHour)
        }, CancellationToken.None);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    // Pedro reserva y sube el comprobante; devuelve el id del pago para que el personal lo revise.
    private async Task<(MarketplaceReservationDto Reservation, Guid PaymentId)> ReserveAndPay(int fromHour = 19, int toHour = 22)
    {
        var reservation = await Reserve(fromHour, toHour);
        var paid = await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/abc.png" }, CancellationToken.None);
        Assert.True(paid.Ok, paid.Error?.Message);
        var paymentId = _t.NewContext().MarketplacePayments.Single(x => x.ReservationId == reservation.Id).Id;
        return (paid.Value!, paymentId);
    }

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    private static MarketplaceApproveRequest Amount(decimal value) => new() { ReviewedAmount = value };

    // ── Datos para pagar ─────────────────────────────────────────────────────

    [Fact]
    public async Task El_comprador_ve_el_total_la_referencia_y_los_datos_para_transferir_mientras_espera_el_pago()
    {
        var reservation = await Reserve(19, 22);

        var info = (await Payments().GetPaymentInfoAsync(reservation.Id, CancellationToken.None)).Value!;

        Assert.Equal(66_000m, info.TotalAmount);
        Assert.Equal("MP-00000001", info.Reference);
        Assert.Equal("Cochera 12", info.Title);
        Assert.Contains("Banco Familiar", info.TransferInfo);
        Assert.NotNull(info.ExpiresAtUtc);
    }

    [Fact]
    public async Task Sin_datos_cargados_en_el_edificio_el_texto_viene_vacio()
    {
        _t.SetTransferInfo(_building, null);
        var reservation = await Reserve(19, 22);

        Assert.Equal(string.Empty, (await Payments().GetPaymentInfoAsync(reservation.Id, CancellationToken.None)).Value!.TransferInfo);
    }

    [Fact]
    public async Task Los_datos_para_transferir_no_se_muestran_cuando_la_reserva_ya_no_espera_el_pago()
    {
        var (reservation, _) = await ReserveAndPay();

        AssertError(await Payments().GetPaymentInfoAsync(reservation.Id, CancellationToken.None), 409);
    }

    [Fact]
    public async Task No_se_muestran_los_datos_de_una_reserva_vencida()
    {
        var reservation = await Reserve(19, 22);
        var db = _t.NewContext();
        db.MarketplaceReservations.Single().ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        db.SaveChanges();

        AssertError(await Payments().GetPaymentInfoAsync(reservation.Id, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Otro_vecino_no_puede_ver_los_datos_de_pago_de_mi_reserva()
    {
        var reservation = await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        AssertError(await Payments().GetPaymentInfoAsync(reservation.Id, CancellationToken.None), 404);
    }

    // ── Subir el comprobante ─────────────────────────────────────────────────

    [Fact]
    public async Task Subir_el_comprobante_deja_la_reserva_en_revision_y_detiene_el_plazo()
    {
        var (reservation, _) = await ReserveAndPay();

        Assert.Equal("InReview", reservation.Status);
        Assert.Null(reservation.ExpiresAtUtc);
        var payment = _t.NewContext().MarketplacePayments.Single();
        Assert.Equal(MarketplacePaymentStatus.Submitted, payment.Status);
        Assert.Equal(66_000m, payment.ExpectedAmount);
        Assert.Equal("/uploads/abc.png", payment.ComprobanteUrl);
        Assert.Equal(_pedro.Id, payment.BuyerUserId);
        Assert.Equal(1, payment.AlertCount);
    }

    [Fact]
    public async Task Subir_el_comprobante_queda_auditado_y_el_horario_sigue_bloqueado()
    {
        var (reservation, paymentId) = await ReserveAndPay();

        var events = _t.NewContext().MarketplaceEvents.OrderBy(x => x.TimestampUtc).ToList();
        Assert.Contains(events, e => e.Action == "payment.submitted" && e.EntityId == paymentId && e.UserId == _pedro.Id);
        Assert.Contains(events, e => e.Action == "reservation.in_review" && e.EntityId == reservation.Id
                                     && e.FromStatus == "PendingPayment" && e.ToStatus == "InReview");
        Assert.Equal(6, _t.NewContext().MarketplaceReservationSlots.Count(x => !x.IsDeleted));
    }

    [Fact]
    public async Task Al_subir_el_comprobante_se_avisa_a_quienes_revisan_y_solo_a_ellos()
    {
        await ReserveAndPay();

        var notices = _t.NewContext().Notifications.Where(x => x.Type == NotificationType.MarketplacePaymentPending).ToList();
        var recipients = notices.Select(x => x.RecipientId).ToHashSet();

        Assert.Contains(_manager.Id, recipients);
        Assert.Contains(_operator.Id, recipients);
        Assert.Contains(_companyAdmin.Id, recipients);
        Assert.DoesNotContain(_otherManager.Id, recipients);   // encargado de otro edificio
        Assert.DoesNotContain(_superAdmin.Id, recipients);
        Assert.DoesNotContain(_pedro.Id, recipients);
        Assert.DoesNotContain(_juan.Id, recipients);
        Assert.All(notices, n => Assert.Equal("MarketplacePayment", n.EntityType));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://sitio-externo.com/comprobante.png")]
    [InlineData("/otra-carpeta/comprobante.png")]
    [InlineData("/uploads/../secretos.png")]
    [InlineData("/uploads//comprobante.png")]
    [InlineData("/uploads\\comprobante.png")]
    public async Task Solo_se_aceptan_comprobantes_subidos_a_la_plataforma(string url)
    {
        var reservation = await Reserve(19, 22);

        var result = await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = url }, CancellationToken.None);

        AssertError(result, 400);
        Assert.Empty(_t.NewContext().MarketplacePayments);
        Assert.Equal(MarketplaceReservationStatus.PendingPayment, _t.NewContext().MarketplaceReservations.Single().Status);
    }

    [Fact]
    public async Task Un_comprobante_con_una_ruta_demasiado_larga_se_rechaza()
    {
        var reservation = await Reserve(19, 22);

        var result = await Payments().SubmitPaymentAsync(reservation.Id,
            new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/" + new string('a', 600) + ".png" }, CancellationToken.None);

        AssertError(result, 400);
    }

    [Fact]
    public async Task No_se_puede_subir_el_comprobante_de_la_reserva_de_otro()
    {
        var reservation = await Reserve(19, 22);
        LoginAs(_maria, "Resident");

        AssertError(await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/x.png" }, CancellationToken.None), 404);
    }

    [Fact]
    public async Task No_se_sube_dos_veces_el_comprobante_ni_se_duplica_el_pago()
    {
        var (reservation, _) = await ReserveAndPay();

        var second = await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/otro.png" }, CancellationToken.None);

        AssertError(second, 409);
        Assert.Single(_t.NewContext().MarketplacePayments);
    }

    [Fact]
    public async Task Una_reserva_vencida_no_acepta_el_pago_aunque_el_proceso_de_fondo_no_haya_corrido()
    {
        var reservation = await Reserve(19, 22);
        var db = _t.NewContext();
        db.MarketplaceReservations.Single().ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        db.SaveChanges();

        var result = await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/x.png" }, CancellationToken.None);

        AssertError(result, 409);
        Assert.Contains("venció", result.Error!.Message);
        Assert.Empty(_t.NewContext().MarketplacePayments);
    }

    [Fact]
    public async Task Una_reserva_cancelada_no_acepta_el_pago()
    {
        var reservation = await Reserve(19, 22);
        await Reservations().CancelPendingAsync(reservation.Id, CancellationToken.None);

        AssertError(await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/x.png" }, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_no_se_puede_pagar()
    {
        var reservation = await Reserve(19, 22);
        _t.EnableMarketplace(_building, enabled: false);

        var result = await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/x.png" }, CancellationToken.None);

        AssertError(result, 403);
        Assert.Equal(MarketplaceModuleGate.DisabledCode, result.Error!.Code);
    }

    [Fact]
    public async Task Una_reserva_en_revision_no_vence_aunque_pase_el_plazo_original()
    {
        var (reservation, _) = await ReserveAndPay();

        Assert.Equal(0, await Reservations().ExpireStaleAsync(CancellationToken.None));
        Assert.Equal(MarketplaceReservationStatus.InReview, _t.NewContext().MarketplaceReservations.Single(x => x.Id == reservation.Id).Status);
    }

    // ── Lista de pagos por revisar ───────────────────────────────────────────

    [Fact]
    public async Task El_personal_ve_los_pagos_por_revisar_con_comprador_unidades_y_montos()
    {
        await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");

        var items = (await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None)).Value!;

        var item = Assert.Single(items);
        Assert.Equal("MP-00000001", item.Reference);
        Assert.Equal("Cochera 12", item.Title);
        Assert.Equal("Pedro Prueba", item.BuyerName);
        Assert.Equal("101", item.BuyerUnits);
        Assert.Equal("Juan Prueba", item.OwnerName);
        Assert.Equal(60_000m, item.BaseAmount);
        Assert.Equal(6_000m, item.CommissionAmount);
        Assert.Equal(66_000m, item.ExpectedAmount);
        Assert.Equal("/uploads/abc.png", item.ComprobanteUrl);
        Assert.Equal("Submitted", item.Status);
        Assert.False(item.BuyerUnitOverdue);
        Assert.False(item.ReservationEnded);
    }

    [Fact]
    public async Task El_aviso_de_mora_lo_ve_solo_quien_revisa_el_pago()
    {
        await ReserveAndPay();
        _overdue.OverdueUnitIds.Add(_otherUnit.Id);
        LoginAsStaff(_manager, "BuildingManager");

        var item = Assert.Single((await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None)).Value!);

        Assert.True(item.BuyerUnitOverdue);
    }

    [Fact]
    public async Task Un_vecino_no_puede_ver_la_lista_de_pagos_por_revisar()
    {
        await ReserveAndPay();

        // Ni el comprador ni el publicador: la lista es solo del personal.
        AssertError(await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None), 403);
        LoginAs(_juan, "Owner");
        AssertError(await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None), 403);
    }

    [Fact]
    public async Task El_personal_de_otro_edificio_no_ve_los_pagos()
    {
        await ReserveAndPay();
        LoginAsStaff(_otherManager, "BuildingManager");

        AssertError(await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None), 404);
    }

    [Fact]
    public async Task El_personal_de_otra_empresa_no_ve_los_pagos()
    {
        await ReserveAndPay();
        var otherCompany = _t.AddCompany();
        var intruder = _t.AddUser(otherCompany, UserRole.BuildingManager);
        LoginAs(intruder, "BuildingManager");
        _access.Buildings.Add(_building.Id);

        AssertError(await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None), 404);
    }

    [Fact]
    public async Task Los_pagos_ya_resueltos_salen_de_la_lista()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        Assert.Empty((await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None)).Value!);
    }

    // ── Confirmar ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirmar_con_el_monto_exacto_confirma_la_reserva_y_asienta_el_ingreso()
    {
        var (reservation, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");

        var result = await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("Approved", result.Value!.Status);

        var db = _t.NewContext();
        var payment = db.MarketplacePayments.Single();
        Assert.Equal(MarketplacePaymentStatus.Approved, payment.Status);
        Assert.Equal(66_000m, payment.ReviewedAmount);
        Assert.Equal(_manager.Id, payment.ReviewedByUserId);
        Assert.NotNull(payment.ReviewedAtUtc);

        var saved = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        Assert.Equal(MarketplaceReservationStatus.Confirmed, saved.Status);
        Assert.Equal(MarketplaceCreditStatus.Pending, saved.CreditStatus);

        var movement = Assert.Single(db.MarketplaceAccountMovements.ToList());
        Assert.Equal(MarketplaceAccountMovementKind.PaymentIn, movement.Kind);
        Assert.Equal(66_000m, movement.Amount);
        Assert.Equal(reservation.Id, movement.ReservationId);
        Assert.Equal(_building.Id, movement.BuildingId);
        Assert.Null(movement.CreatedByUserId);
        Assert.Contains("MP-00000001", movement.Concept);
    }

    [Fact]
    public async Task Confirmar_deja_auditados_el_pago_la_reserva_y_el_asiento()
    {
        var (reservation, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        var events = _t.NewContext().MarketplaceEvents.ToList();
        var approved = Assert.Single(events, e => e.Action == "payment.approved");
        Assert.Equal(_manager.Id, approved.UserId);
        Assert.Equal("Submitted", approved.FromStatus);
        Assert.Equal("Approved", approved.ToStatus);
        var confirmed = Assert.Single(events, e => e.Action == "reservation.confirmed");
        Assert.Equal(reservation.Id, confirmed.EntityId);
        Assert.Contains("66000", confirmed.DataJson);
        Assert.Contains("60000", confirmed.DataJson);
        var posted = Assert.Single(events, e => e.Action == "account.movement");
        Assert.Null(posted.UserId);
    }

    [Fact]
    public async Task Al_confirmar_se_avisa_al_comprador_y_al_propietario()
    {
        var (reservation, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        var db = _t.NewContext();
        var buyer = db.Notifications.Single(x => x.Type == NotificationType.MarketplaceReservationConfirmed);
        Assert.Equal(_pedro.Id, buyer.RecipientId);
        Assert.Equal(reservation.Id, buyer.EntityId);
        var owner = db.Notifications.Single(x => x.Type == NotificationType.MarketplaceNewReservation);
        Assert.Equal(_juan.Id, owner.RecipientId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60_000)]    // solo la base: falta la comision
    [InlineData(65_999)]
    [InlineData(66_001)]
    [InlineData(70_000)]    // excedente
    public async Task Solo_se_confirma_si_el_monto_coincide_exacto_con_el_total(int amount)
    {
        var (reservation, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");

        var result = await Payments().ApproveAsync(paymentId, Amount(amount), CancellationToken.None);

        AssertError(result, 400);
        var db = _t.NewContext();
        Assert.Equal(MarketplacePaymentStatus.Submitted, db.MarketplacePayments.Single().Status);
        Assert.Equal(MarketplaceReservationStatus.InReview, db.MarketplaceReservations.Single(x => x.Id == reservation.Id).Status);
        Assert.Empty(db.MarketplaceAccountMovements);
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_duplica_el_ingreso()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");

        Assert.True((await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None)).Ok);
        var again = await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        AssertError(again, 409);
        var db = _t.NewContext();
        Assert.Single(db.MarketplaceAccountMovements.ToList());
        Assert.Single(db.Notifications.Where(x => x.Type == NotificationType.MarketplaceReservationConfirmed).ToList());
    }

    [Fact]
    public async Task Confirmar_con_otro_usuario_del_personal_tampoco_repite_el_efecto()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);
        LoginAsStaff(_operator, "CompanyOperator");

        AssertError(await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None), 409);
        Assert.Single(_t.NewContext().MarketplaceAccountMovements.ToList());
    }

    [Fact]
    public async Task Un_vecino_no_puede_confirmar_ni_el_comprador_ni_el_publicador()
    {
        var (_, paymentId) = await ReserveAndPay();

        AssertError(await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None), 404);
        LoginAs(_juan, "Owner");
        AssertError(await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None), 404);
        Assert.Equal(MarketplacePaymentStatus.Submitted, _t.NewContext().MarketplacePayments.Single().Status);
    }

    [Fact]
    public async Task El_personal_de_otro_edificio_no_puede_confirmar()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_otherManager, "BuildingManager");

        AssertError(await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None), 404);
        Assert.Empty(_t.NewContext().MarketplaceAccountMovements);
    }

    [Fact]
    public async Task El_Operador_y_el_Administrador_de_empresa_tambien_pueden_confirmar()
    {
        var (_, first) = await ReserveAndPay(19, 21);
        LoginAsStaff(_operator, "CompanyOperator");
        Assert.True((await Payments().ApproveAsync(first, Amount(44_000m), CancellationToken.None)).Ok);

        LoginAs(_maria, "Resident");
        var reservation = await Reserve(22, 24);
        await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/m.png" }, CancellationToken.None);
        var second = _t.NewContext().MarketplacePayments.Single(x => x.ReservationId == reservation.Id).Id;
        _access.Buildings.Add(_building.Id);
        LoginAs(_companyAdmin, "CompanyAdmin");
        _access.Buildings.Add(_building.Id);
        Assert.True((await Payments().ApproveAsync(second, Amount(44_000m), CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Pasado_el_fin_de_la_reserva_ya_no_se_puede_confirmar()
    {
        var (reservation, paymentId) = await ReserveAndPay();
        var db = _t.NewContext();
        var saved = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        saved.StartsAtUtc = DateTime.UtcNow.AddHours(-4);
        saved.EndsAtUtc = DateTime.UtcNow.AddHours(-1);
        db.SaveChanges();
        LoginAsStaff(_manager, "BuildingManager");

        var result = await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        AssertError(result, 409);
        Assert.Empty(_t.NewContext().MarketplaceAccountMovements);
        Assert.True((await Payments().GetPendingForReviewAsync(_building.Id, CancellationToken.None)).Value!.Single().ReservationEnded);
    }

    [Fact]
    public async Task Un_pago_ya_rechazado_no_se_puede_confirmar()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "No llegó la transferencia" }, CancellationToken.None);

        AssertError(await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None), 409);
        Assert.Empty(_t.NewContext().MarketplaceAccountMovements);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_el_personal_no_confirma()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        _t.EnableMarketplace(_building, enabled: false);

        var result = await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        AssertError(result, 403);
    }

    // ── Rechazar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rechazar_cierra_la_reserva_libera_el_horario_y_guarda_el_motivo()
    {
        var (reservation, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");

        var result = await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "  No llegó la transferencia  " }, CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("Rejected", result.Value!.Status);
        Assert.Equal("No llegó la transferencia", result.Value.RejectionReason);

        var db = _t.NewContext();
        var payment = db.MarketplacePayments.Single();
        Assert.Equal(MarketplacePaymentStatus.Rejected, payment.Status);
        Assert.Equal(_manager.Id, payment.ReviewedByUserId);
        var saved = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        Assert.Equal(MarketplaceReservationStatus.Rejected, saved.Status);
        Assert.Equal("No llegó la transferencia", saved.CancelReason);
        Assert.Equal(MarketplaceCancellationActor.Staff, saved.CancelledBy);
        Assert.Equal(0, db.MarketplaceReservationSlots.Count(x => !x.IsDeleted));
        Assert.Empty(db.MarketplaceAccountMovements);
    }

    [Fact]
    public async Task Despues_de_un_rechazo_otro_vecino_puede_tomar_el_horario_y_el_comprador_reservar_de_nuevo()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "Monto incorrecto" }, CancellationToken.None);

        LoginAs(_maria, "Resident");
        await Reserve(19, 22);
        LoginAs(_pedro, "Resident");
        await Reserve(22, 24);
    }

    [Fact]
    public async Task Al_rechazar_se_avisa_al_comprador_con_el_motivo_y_queda_auditado()
    {
        var (reservation, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "Monto incorrecto" }, CancellationToken.None);

        var db = _t.NewContext();
        var notice = db.Notifications.Single(x => x.Type == NotificationType.MarketplaceReservationRejected);
        Assert.Equal(_pedro.Id, notice.RecipientId);
        Assert.Contains("Monto incorrecto", notice.Body);
        var rejected = Assert.Single(db.MarketplaceEvents.Where(x => x.Action == "payment.rejected").ToList());
        Assert.Equal(_manager.Id, rejected.UserId);
        Assert.Contains("Monto incorrecto", rejected.DataJson);
        Assert.Single(db.MarketplaceEvents.Where(x => x.Action == "reservation.rejected" && x.EntityId == reservation.Id).ToList());
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Rechazar_exige_el_motivo(string reason)
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");

        AssertError(await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = reason }, CancellationToken.None), 400);
        Assert.Equal(MarketplacePaymentStatus.Submitted, _t.NewContext().MarketplacePayments.Single().Status);
    }

    [Fact]
    public async Task El_motivo_no_puede_superar_500_caracteres()
    {
        var (_, paymentId) = await ReserveAndPay();
        LoginAsStaff(_manager, "BuildingManager");

        AssertError(await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = new string('x', 501) }, CancellationToken.None), 400);
    }

    [Fact]
    public async Task No_se_rechaza_dos_veces_ni_un_pago_ya_confirmado()
    {
        var (_, first) = await ReserveAndPay(19, 21);
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().RejectAsync(first, new MarketplaceRejectRequest { Reason = "x" }, CancellationToken.None);
        AssertError(await Payments().RejectAsync(first, new MarketplaceRejectRequest { Reason = "otra vez" }, CancellationToken.None), 409);

        LoginAs(_maria, "Resident");
        var reservation = await Reserve(22, 24);
        await Payments().SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/m.png" }, CancellationToken.None);
        var second = _t.NewContext().MarketplacePayments.Single(x => x.ReservationId == reservation.Id).Id;
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().ApproveAsync(second, Amount(44_000m), CancellationToken.None);

        AssertError(await Payments().RejectAsync(second, new MarketplaceRejectRequest { Reason = "tarde" }, CancellationToken.None), 409);
    }

    [Fact]
    public async Task Rechazar_si_se_puede_aunque_la_reserva_ya_haya_terminado()
    {
        var (reservation, paymentId) = await ReserveAndPay();
        var db = _t.NewContext();
        var saved = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        saved.StartsAtUtc = DateTime.UtcNow.AddHours(-4);
        saved.EndsAtUtc = DateTime.UtcNow.AddHours(-1);
        db.SaveChanges();
        LoginAsStaff(_manager, "BuildingManager");

        Assert.True((await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "Llegó tarde" }, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Un_vecino_o_personal_de_otro_edificio_no_pueden_rechazar()
    {
        var (_, paymentId) = await ReserveAndPay();

        AssertError(await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "x" }, CancellationToken.None), 404);
        LoginAsStaff(_otherManager, "BuildingManager");
        AssertError(await Payments().RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "x" }, CancellationToken.None), 404);
        Assert.Equal(MarketplacePaymentStatus.Submitted, _t.NewContext().MarketplacePayments.Single().Status);
    }

    // ── Alertas al revisor ───────────────────────────────────────────────────

    private int PendingAlertNotices() =>
        _t.NewContext().Notifications.Count(x => x.Type == NotificationType.MarketplacePaymentPending && x.RecipientId == _manager.Id);

    private void SetPaymentTimes(DateTime? submittedAt = null, DateTime? lastAlertAt = null)
    {
        var db = _t.NewContext();
        var payment = db.MarketplacePayments.Single();
        if (submittedAt.HasValue) payment.SubmittedAtUtc = submittedAt.Value;
        if (lastAlertAt.HasValue) payment.LastAlertAtUtc = lastAlertAt.Value;
        db.SaveChanges();
    }

    [Fact]
    public async Task La_primera_alerta_sale_al_subir_el_comprobante_y_no_se_repite_enseguida()
    {
        await ReserveAndPay();

        Assert.Equal(1, PendingAlertNotices());
        Assert.Equal(0, await Payments().SendReviewAlertsAsync(CancellationToken.None));
        Assert.Equal(1, PendingAlertNotices());
    }

    [Fact]
    public async Task A_los_15_minutos_sale_el_recordatorio_y_despues_cada_hora()
    {
        await ReserveAndPay();

        SetPaymentTimes(submittedAt: DateTime.UtcNow.AddMinutes(-14));
        Assert.Equal(0, await Payments().SendReviewAlertsAsync(CancellationToken.None));

        SetPaymentTimes(submittedAt: DateTime.UtcNow.AddMinutes(-16));
        Assert.Equal(1, await Payments().SendReviewAlertsAsync(CancellationToken.None));
        Assert.Equal(2, _t.NewContext().MarketplacePayments.Single().AlertCount);
        Assert.Equal(2, PendingAlertNotices());
        var titles = _t.NewContext().Notifications
            .Where(x => x.Type == NotificationType.MarketplacePaymentPending && x.RecipientId == _manager.Id)
            .Select(x => x.Title).ToList();
        Assert.Contains(titles, t => t.StartsWith("Recordatorio"));
        Assert.Contains(titles, t => !t.StartsWith("Recordatorio"));

        // Recien enviada: no vuelve a salir. A la hora sale la tercera, y asi.
        Assert.Equal(0, await Payments().SendReviewAlertsAsync(CancellationToken.None));
        SetPaymentTimes(lastAlertAt: DateTime.UtcNow.AddMinutes(-59));
        Assert.Equal(0, await Payments().SendReviewAlertsAsync(CancellationToken.None));
        SetPaymentTimes(lastAlertAt: DateTime.UtcNow.AddMinutes(-61));
        Assert.Equal(1, await Payments().SendReviewAlertsAsync(CancellationToken.None));
        Assert.Equal(3, _t.NewContext().MarketplacePayments.Single().AlertCount);
    }

    [Fact]
    public async Task Resuelto_el_pago_dejan_de_llegar_alertas()
    {
        var (_, paymentId) = await ReserveAndPay();
        SetPaymentTimes(submittedAt: DateTime.UtcNow.AddMinutes(-30));
        LoginAsStaff(_manager, "BuildingManager");
        await Payments().ApproveAsync(paymentId, Amount(66_000m), CancellationToken.None);

        Assert.Equal(0, await Payments().SendReviewAlertsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Un_dia_despues_del_fin_de_la_reserva_ya_no_se_insiste()
    {
        var (reservation, _) = await ReserveAndPay();
        var db = _t.NewContext();
        var saved = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        saved.StartsAtUtc = DateTime.UtcNow.AddHours(-30);
        saved.EndsAtUtc = DateTime.UtcNow.AddHours(-26);
        db.SaveChanges();
        SetPaymentTimes(submittedAt: DateTime.UtcNow.AddHours(-30), lastAlertAt: DateTime.UtcNow.AddHours(-5));

        Assert.Equal(0, await Payments().SendReviewAlertsAsync(CancellationToken.None));
    }
}
