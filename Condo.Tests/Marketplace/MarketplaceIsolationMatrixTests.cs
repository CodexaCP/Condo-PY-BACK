using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Auditoria de aislamiento (fase 9): TODAS las operaciones del marketplace que reciben un id o un edificio se intentan desde cada
/// tipo de intruso (otra empresa, otro edificio, vecino sin relacion con la operacion, usuario final en pantallas del personal,
/// personal en lo que es solo del SuperAdmin, token que dice otra empresa). Se exige 403 o 404 (nunca un 409 que revele estados ni un
/// exito) y que la base quede exactamente igual: nada cambia, nada se audita, nada se avisa.
/// </summary>
public class MarketplaceIsolationMatrixTests : IDisposable
{
    private delegate Task<(bool Ok, int? Status)> Operation(Phase7Harness.Services s, Phase7Harness.DocServices d);

    private readonly Phase7Harness _h = new();

    // Empresa Y: otro edificio con el modulo habilitado, su personal y sus vecinos.
    private readonly Company _companyY;
    private readonly Building _buildingY;
    private readonly ApplicationUser _ownerY;
    private readonly ApplicationUser _managerY;
    private readonly ApplicationUser _adminY;

    // Datos de la empresa X (el objetivo de los ataques).
    private readonly MarketplaceReservation _confirmed;     // confirmada, empieza en 5 h
    private readonly MarketplaceReservation _started;       // confirmada y en curso
    private readonly MarketplaceReservation _unpaid;        // esperando pago
    private readonly MarketplaceReservation _inReview;      // pago en revision
    private readonly MarketplacePayment _submittedPayment;
    private readonly MarketplaceRefund _refund;
    private readonly MarketplaceClaim _claim;
    private readonly MarketplaceHandoverNote _note;

    public MarketplaceIsolationMatrixTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        _companyY = _h.T.AddCompany("Empresa Y");
        _buildingY = _h.T.AddBuilding(_companyY, "Edificio Y");
        _adminY = _h.T.AddUser(_companyY, UserRole.CompanyAdmin, "AdminY");
        _h.T.AssignPlan(_buildingY, _adminY, includesMarketplace: true);
        _h.T.EnableMarketplace(_buildingY);
        var unitY = _h.T.AddUnit(_buildingY, "Y1");
        _ownerY = _h.T.AddUser(_companyY, UserRole.Owner, "OwnerY");
        _h.T.AddOwner(unitY, _ownerY, primary: true);
        _managerY = _h.T.AddUser(_companyY, UserRole.BuildingManager, "ManagerY");
        _h.T.AddBuildingAccess(_managerY, _buildingY);

        _confirmed = _h.SeedConfirmed(startsInHours: 5);
        _started = _h.SeedConfirmed(startsInHours: -1);
        _unpaid = Seed(MarketplaceReservationStatus.PendingPayment, 40);
        _inReview = Seed(MarketplaceReservationStatus.InReview, 60);
        _submittedPayment = new MarketplacePayment
        {
            CompanyId = _h.Company.Id, ReservationId = _inReview.Id, BuildingId = _h.Building.Id, BuyerUserId = _h.Pedro.Id,
            ComprobanteUrl = "/uploads/x.png", ExpectedAmount = _inReview.TotalAmount, Status = MarketplacePaymentStatus.Submitted
        };
        _h.T.Db.MarketplacePayments.Add(_submittedPayment);

        var cancelledForRefund = _h.SeedConfirmed(startsInHours: 80);
        _h.T.Db.SaveChanges();
        _h.LoginAs(_h.Pedro, "Resident");
        Assert.True(_h.Build().Cancellations.CancelByBuyerAsync(cancelledForRefund.Id, null, CancellationToken.None).Result.Ok);
        _refund = _h.T.NewContext().MarketplaceRefunds.Single();

        var finished = _h.SeedConfirmed(startsInHours: -10, status: MarketplaceReservationStatus.Completed);
        Assert.True(_h.Build().Claims.OpenAsync(finished.Id, "Problema", CancellationToken.None).Result.Ok);
        _claim = _h.T.NewContext().MarketplaceClaims.Single();

        _note = new MarketplaceHandoverNote
        {
            CompanyId = _h.Company.Id, BuildingId = _h.Building.Id, UnitId = _h.Unit.Id, PreviousOwnerId = _h.Juan.Id,
            Trigger = MarketplaceHandoverTrigger.PrimaryRemoved, Content = "x", ReservationIds = _confirmed.Id.ToString(), ReservationCount = 1
        };
        _h.T.Db.MarketplaceHandoverNotes.Add(_note);
        _h.T.Db.SaveChanges();
    }

    public void Dispose() => _h.Dispose();

    private MarketplaceReservation Seed(MarketplaceReservationStatus status, int hours)
    {
        var r = _h.SeedConfirmed(startsInHours: hours, status: status, credit: MarketplaceCreditStatus.None);
        return r;
    }

    private static async Task<(bool Ok, int? Status)> R<T>(Task<MarketplaceResult<T>> call)
    {
        var result = await call;
        return (result.Ok, result.Error?.StatusCode);
    }

    // ── Intrusos ─────────────────────────────────────────────────────────────

    private sealed record Intruder(string Name, Func<Phase7Harness.Session> Session);

    private Intruder Maria() => new("vecina sin relacion con la operacion", () => _h.NewSession(_h.Maria, "Resident"));
    private Intruder Pedro() => new("comprador (usuario final) en pantallas del personal", () => _h.NewSession(_h.Pedro, "Resident"));
    private Intruder Juan() => new("propietario (usuario final) en pantallas del personal", () => _h.NewSession(_h.Juan, "Owner"));
    private Intruder OwnerY() => new("vecino de otra empresa", () => _h.NewSession(_ownerY, "Owner"));
    private Intruder ManagerY() => new("Encargado de otra empresa", () => _h.NewSession(_managerY, "BuildingManager", null, _buildingY));
    private Intruder AdminY() => new("Administrador de otra empresa", () => _h.NewSession(_adminY, "CompanyAdmin", null, _buildingY));
    private Intruder OtherBuildingManager() => new("Encargado de otro edificio de la misma empresa", () => _h.NewSession(_h.OtherManager, "BuildingManager", null, _h.OtherBuilding));
    private Intruder TokenOfOtherCompany() => new("token que dice otra empresa", () => _h.NewSession(_h.Maria, "Resident", _companyY.Id));
    private Intruder StaffTokenOfOtherCompany() => new("personal con el token de otra empresa", () => _h.NewSession(_h.Manager, "BuildingManager", _companyY.Id, _h.Building));
    private Intruder ManagerX() => new("Encargado del edificio (no es SuperAdmin)", () => _h.NewSession(_h.Manager, "BuildingManager", null, _h.Building));
    private Intruder OperatorX() => new("Operador del edificio (no es SuperAdmin)", () => _h.NewSession(_h.Operator, "CompanyOperator", null, _h.Building));
    private Intruder AdminX() => new("Administrador de la empresa (no es SuperAdmin)", () => _h.NewSession(_h.CompanyAdmin, "CompanyAdmin", null, _h.Building));

    // Quienes no tienen NADA que ver con el edificio X.
    private IEnumerable<Intruder> Outsiders() => [OwnerY(), ManagerY(), AdminY(), OtherBuildingManager(), TokenOfOtherCompany(), StaffTokenOfOtherCompany()];

    // ── Estado de la base (para exigir que no cambie nada) ───────────────────

    private string Fingerprint()
    {
        var db = _h.T.NewContext();
        string Join<T>(IEnumerable<T> rows) => string.Join(";", rows);
        return string.Join("|",
            Join(db.MarketplaceReservations.OrderBy(x => x.Reference).Select(x => x.Reference + x.Status + x.CreditStatus + x.StartResponse + x.CancelledBy + x.CancelReason)),
            Join(db.MarketplacePayments.OrderBy(x => x.Id).Select(x => x.Id + x.Status.ToString() + x.ComprobanteUrl)),
            Join(db.MarketplaceRefunds.OrderBy(x => x.Id).Select(x => x.Id + x.Status.ToString())),
            Join(db.MarketplaceClaims.OrderBy(x => x.Id).Select(x => x.Id + x.Status.ToString() + x.Resolution)),
            Join(db.MarketplaceListings.OrderBy(x => x.Id).Select(x => x.Id + x.Status.ToString() + x.Title + x.HourlyPrice)),
            Join(db.MarketplaceReservationSlots.Where(x => x.IsDeleted).Select(x => x.Id)),
            Join(db.MarketplaceHandoverNotes.Select(x => x.Id + x.ReadAtUtc.ToString())),
            db.MarketplaceAccountMovements.Count(), db.MarketplaceOwnerDebts.Count(), db.MarketplaceEvents.Count(),
            db.Notifications.Count(), db.OwnerCreditMovements.Count(), db.OwnerCredits.Sum(x => 1));
    }

    private async Task AssertDenied(string what, IEnumerable<Intruder> intruders, Operation operation)
    {
        foreach (var intruder in intruders)
        {
            var before = Fingerprint();
            var session = intruder.Session();
            var (ok, status) = await operation(_h.Build(session), _h.BuildDocuments(session));

            Assert.False(ok, $"{what}: lo logró un {intruder.Name}.");
            Assert.True(status is 403 or 404, $"{what}: un {intruder.Name} recibió {status} en vez de 403/404.");
            Assert.Equal(before, Fingerprint());
        }
    }

    // ── Operaciones sobre una reserva o un pago concretos (solo sus partes o el personal) ──

    [Fact]
    public async Task Nadie_ajeno_a_la_operacion_puede_cancelarla_consultarla_reclamarla_ni_responder_su_aviso()
    {
        var parties = new[] { Maria(), OwnerY(), ManagerY(), AdminY(), OtherBuildingManager(), TokenOfOtherCompany(), ManagerX(), OperatorX(), AdminX() };
        var req = new MarketplaceStartResponseRequest { Attending = true };

        await AssertDenied("cancelar como comprador", parties, (s, d) => R(s.Cancellations.CancelByBuyerAsync(_confirmed.Id, "x", default)));
        await AssertDenied("vista previa de cancelar", parties, (s, d) => R(s.Cancellations.PreviewAsync(_confirmed.Id, default)));
        await AssertDenied("responder el aviso de inicio", parties, (s, d) => R(s.StartNotices.RespondAsync(_started.Id, req, default)));
        await AssertDenied("reportar un problema", parties, (s, d) => R(s.Claims.OpenAsync(_started.Id, "x", default)));
        await AssertDenied("datos para pagar", parties, (s, d) => R(s.Payments.GetPaymentInfoAsync(_unpaid.Id, default)));
        await AssertDenied("subir un comprobante", parties, (s, d) => R(s.Payments.SubmitPaymentAsync(_unpaid.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/z.png" }, default)));
        await AssertDenied("cancelar una reserva sin pagar", parties, (s, d) => R(s.Reservations.CancelPendingAsync(_unpaid.Id, default)));
    }

    [Fact]
    public async Task Nadie_ajeno_puede_cancelar_como_propietario_ni_pedir_el_comprobante()
    {
        var notTheOwner = new[] { Maria(), Pedro(), OwnerY(), ManagerY(), AdminY(), OtherBuildingManager(), TokenOfOtherCompany(), ManagerX(), OperatorX(), AdminX() };
        var notAParty = new[] { Maria(), OwnerY(), ManagerY(), AdminY(), OtherBuildingManager(), TokenOfOtherCompany(), StaffTokenOfOtherCompany() };

        await AssertDenied("cancelar como propietario", notTheOwner, (s, d) => R(s.Cancellations.CancelByOwnerAsync(_confirmed.Id, "x", default)));
        await AssertDenied("comprobante en PDF", notAParty, (s, d) => R(d.Documents.GetReceiptAsync(_confirmed.Id, default)));
    }

    // ── Pantallas del personal ───────────────────────────────────────────────

    [Fact]
    public async Task Los_usuarios_finales_y_los_de_otras_empresas_no_ven_ni_tocan_nada_del_personal()
    {
        var notStaffOfX = new[] { Maria(), Pedro(), Juan(), OwnerY(), ManagerY(), AdminY(), OtherBuildingManager(), TokenOfOtherCompany(), StaffTokenOfOtherCompany() };
        var buildingX = _h.Building.Id;

        await AssertDenied("pagos por revisar", notStaffOfX, (s, d) => R(s.Payments.GetPendingForReviewAsync(buildingX, default)));
        await AssertDenied("confirmar un pago", notStaffOfX, (s, d) => R(s.Payments.ApproveAsync(_submittedPayment.Id, new MarketplaceApproveRequest { ReviewedAmount = _submittedPayment.ExpectedAmount }, default)));
        await AssertDenied("rechazar un pago", notStaffOfX, (s, d) => R(s.Payments.RejectAsync(_submittedPayment.Id, new MarketplaceRejectRequest { Reason = "x" }, default)));
        await AssertDenied("lista de reembolsos", notStaffOfX, (s, d) => R(s.Refunds.GetRefundsAsync(buildingX, true, default)));
        await AssertDenied("marcar un reembolso devuelto", notStaffOfX, (s, d) => R(s.Refunds.MarkReturnedAsync(_refund.Id, default)));
        await AssertDenied("deudas por gestion", notStaffOfX, (s, d) => R(s.Refunds.GetOwnerDebtsAsync(buildingX, default)));
        await AssertDenied("lista de reclamos", notStaffOfX, (s, d) => R(s.Claims.GetClaimsAsync(buildingX, true, default)));
        await AssertDenied("resolver un reclamo", notStaffOfX, (s, d) => R(s.Claims.ResolveAsync(_claim.Id, new MarketplaceClaimResolveRequest { Outcome = "InFavorOfBuyer", Note = "x" }, default)));
        await AssertDenied("extracto de la cuenta", notStaffOfX.Append(OperatorX()), (s, d) => R(s.Account.GetStatementAsync(buildingX, null, null, default)));
        await AssertDenied("historial de la operacion", notStaffOfX, (s, d) => R(d.Documents.GetHistoryAsync(_confirmed.Id, default)));
        await AssertDenied("notas de cambio de propietario", notStaffOfX, (s, d) => R(d.Handover.GetNotesAsync(buildingX, true, default)));
        await AssertDenied("abrir una nota", notStaffOfX, (s, d) => R(d.Handover.GetNoteAsync(_note.Id, default)));
        await AssertDenied("nota en PDF", notStaffOfX, (s, d) => R(d.Documents.GetHandoverPdfAsync(_note.Id, default)));
        await AssertDenied("todas las publicaciones del edificio", notStaffOfX, (s, d) => R(d.Listings.GetAllForStaffAsync(buildingX, default)));
    }

    [Fact]
    public async Task Lo_que_es_solo_del_SuperAdmin_no_lo_hace_ningun_otro_rol_ni_siquiera_del_mismo_edificio()
    {
        var everyoneButSuperAdmin = new[] { Maria(), Pedro(), Juan(), OwnerY(), ManagerY(), AdminY(), OtherBuildingManager(), ManagerX(), OperatorX(), AdminX() };

        await AssertDenied("ajuste manual de la cuenta", everyoneButSuperAdmin,
            (s, d) => R(s.Account.AddAdjustmentAsync(new MarketplaceAdjustmentRequest { BuildingId = _h.Building.Id, Amount = 1_000_000m, Concept = "x" }, default)));
        await AssertDenied("revertir una acreditacion", everyoneButSuperAdmin, (s, d) => R(s.Credits.ReverseCreditAsync(_confirmed.Id, "x", default)));
    }

    // ── Operaciones del propio edificio (vecinos): de afuera no se puede ─────

    [Fact]
    public async Task Quien_no_es_del_edificio_no_explora_cotiza_reserva_ni_publica()
    {
        var buildingX = _h.Building.Id;
        var start = _h.Listing.WindowStartUtc.Date.AddHours(19);
        var quote = new MarketplaceQuoteRequest { ListingId = _h.Listing.Id, StartsAtUtc = start, EndsAtUtc = start.AddHours(2) };

        await AssertDenied("explorar publicaciones", Outsiders(), (s, d) => R(s.Reservations.ExploreAsync(buildingX, default)));
        await AssertDenied("cotizar", Outsiders(), (s, d) => R(s.Reservations.QuoteAsync(quote, default)));
        await AssertDenied("reservar", Outsiders(), (s, d) => R(s.Reservations.ReserveAsync(quote, default)));
        await AssertDenied("mis reservas del edificio", Outsiders(), (s, d) => R(s.Reservations.GetMineAsync(buildingX, default)));
        await AssertDenied("reservas en mis publicaciones", Outsiders(), (s, d) => R(s.Reservations.GetOnMyListingsAsync(buildingX, default)));
        await AssertDenied("mis publicaciones", Outsiders(), (s, d) => R(d.Listings.GetMineAsync(buildingX, default)));
        await AssertDenied("unidades que puedo publicar", Outsiders(), (s, d) => R(d.Listings.GetPublishableUnitsAsync(buildingX, default)));
        await AssertDenied("publicar una unidad ajena", Outsiders().Append(Maria()).Append(Pedro()), (s, d) => R(d.Listings.CreateAsync(new MarketplaceListingCreateRequest
        {
            BuildingId = buildingX, UnitId = _h.Unit.Id, Title = "Robada", HourlyPrice = 1m,
            WindowStartUtc = start.AddDays(5), WindowEndUtc = start.AddDays(5).AddHours(3)
        }, default)));
    }

    [Fact]
    public async Task Nadie_que_no_sea_el_dueno_edita_pausa_reanuda_ni_cierra_una_publicacion_ajena()
    {
        var notTheOwner = new[] { Maria(), Pedro(), OwnerY(), ManagerY(), AdminY(), OtherBuildingManager(), TokenOfOtherCompany(), StaffTokenOfOtherCompany() };
        var update = new MarketplaceListingUpdateRequest
        {
            Title = "Hackeada", HourlyPrice = 1m,
            WindowStartUtc = _h.Listing.WindowStartUtc, WindowEndUtc = _h.Listing.WindowEndUtc
        };

        await AssertDenied("editar la publicacion", notTheOwner, (s, d) => R(d.Listings.UpdateAsync(_h.Listing.Id, update, default)));
        await AssertDenied("pausar la publicacion", notTheOwner, (s, d) => R(d.Listings.SuspendAsync(_h.Listing.Id, "x", default)));
        await AssertDenied("reanudar la publicacion", notTheOwner, (s, d) => R(d.Listings.ResumeAsync(_h.Listing.Id, default)));
        await AssertDenied("cerrar la publicacion", notTheOwner, (s, d) => R(d.Listings.CloseAsync(_h.Listing.Id, "x", default)));
    }

    // ── Manipulacion de datos ────────────────────────────────────────────────

    [Fact]
    public async Task El_precio_y_el_total_nunca_se_aceptan_del_cliente()
    {
        // La solicitud de reserva no trae ningun importe: el servidor calcula todo con la publicacion y la comision del edificio.
        var properties = typeof(MarketplaceQuoteRequest).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Equal(new[] { "EndsAtUtc", "ListingId", "StartsAtUtc" }, properties.OrderBy(x => x).ToArray());

        var start = _h.Listing.WindowStartUtc.Date.AddHours(21);
        _h.LoginAs(_h.Maria, "Resident");
        var reserved = (await _h.Build().Reservations.ReserveAsync(
            new MarketplaceQuoteRequest { ListingId = _h.Listing.Id, StartsAtUtc = start, EndsAtUtc = start.AddHours(2) }, default)).Value!;

        Assert.Equal(20_000m, reserved.HourlyPrice);
        Assert.Equal(40_000m, reserved.BaseAmount);
        Assert.Equal(4_000m, reserved.CommissionAmount);
        Assert.Equal(44_000m, reserved.TotalAmount);
    }

    [Fact]
    public async Task El_comprobante_solo_acepta_archivos_subidos_a_la_propia_plataforma()
    {
        _h.LoginAs(_h.Pedro, "Resident");
        foreach (var bad in new[] { "http://malo.com/x.png", "//malo.com/x.png", "/uploads/../secreto.txt", "/uploads\\x.png", "javascript:alert(1)", "/otra-carpeta/x.png", "" })
        {
            var result = await _h.Build().Payments.SubmitPaymentAsync(_unpaid.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = bad }, default);
            Assert.False(result.Ok, $"Aceptó el comprobante «{bad}».");
            Assert.Equal(400, result.Error!.StatusCode);
        }
    }

    [Fact]
    public async Task La_aprobacion_exige_que_el_monto_coincida_exacto_con_el_total_y_solo_el_personal_aprueba()
    {
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        foreach (var amount in new[] { 1m, _submittedPayment.ExpectedAmount - 1, _submittedPayment.ExpectedAmount + 1, 0m, -66_000m })
        {
            var result = await _h.Build().Payments.ApproveAsync(_submittedPayment.Id, new MarketplaceApproveRequest { ReviewedAmount = amount }, default);
            Assert.False(result.Ok, $"Aprobó con {amount}.");
        }

        Assert.Equal(MarketplacePaymentStatus.Submitted, _h.T.NewContext().MarketplacePayments.Single(x => x.Id == _submittedPayment.Id).Status);
    }

    // ── El modulo apagado o sin plan cierra todo ─────────────────────────────

    [Fact]
    public async Task Con_el_modulo_apagado_ninguna_operacion_del_edificio_responde_aunque_sea_del_personal()
    {
        _h.T.EnableMarketplace(_h.Building, enabled: false);
        var buildingX = _h.Building.Id;
        var anyone = new[] { Pedro(), Juan(), ManagerX(), OperatorX(), AdminX() };

        await AssertDenied("explorar", new[] { Pedro(), Juan() }, (s, d) => R(s.Reservations.ExploreAsync(buildingX, default)));
        await AssertDenied("pagos por revisar", new[] { ManagerX(), OperatorX(), AdminX() }, (s, d) => R(s.Payments.GetPendingForReviewAsync(buildingX, default)));
        await AssertDenied("reembolsos", new[] { ManagerX(), OperatorX(), AdminX() }, (s, d) => R(s.Refunds.GetRefundsAsync(buildingX, true, default)));
        await AssertDenied("reclamos", new[] { ManagerX(), OperatorX(), AdminX() }, (s, d) => R(s.Claims.GetClaimsAsync(buildingX, true, default)));
        await AssertDenied("cancelar", new[] { Pedro() }, (s, d) => R(s.Cancellations.CancelByBuyerAsync(_confirmed.Id, "x", default)));
        await AssertDenied("comprobante", anyone.Take(2), (s, d) => R(d.Documents.GetReceiptAsync(_confirmed.Id, default)));
    }

    // ── El SuperAdmin si puede (y solo el) ───────────────────────────────────

    [Fact]
    public async Task El_SuperAdmin_si_puede_cargar_un_ajuste_y_se_audita_a_su_nombre()
    {
        _h.LoginAs(_h.SuperAdmin, "SuperAdmin");
        _h.Tenant.CompanyId = null;

        var result = await _h.Build().Account.AddAdjustmentAsync(
            new MarketplaceAdjustmentRequest { BuildingId = _h.Building.Id, Amount = 5_000m, Concept = "Ajuste de prueba" }, default);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(_h.SuperAdmin.FullName, result.Value!.CreatedByName);
    }

    // ── Panel de administracion del modulo (controlador) ─────────────────────

    [Fact]
    public async Task El_panel_de_administracion_del_modulo_es_solo_del_SuperAdmin()
    {
        foreach (var role in new[] { "CompanyAdmin", "CompanyOperator", "BuildingManager", "Owner", "Resident" })
        {
            var tenant = new FakeTenantContext { Role = role, CompanyId = _h.Company.Id };
            var db = _h.T.NewContext();
            var controller = new MarketplaceAdminController(db, tenant, new MarketplaceModuleGate(db));
            var update = new MarketplaceAdminUpdateRequest { Enabled = true, CommissionPercent = 0m, TransferInfo = "mi cuenta" };

            Assert.Equal(403, Assert.IsType<ObjectResult>((await controller.GetBuildings(default)).Result).StatusCode);
            Assert.Equal(403, Assert.IsType<ObjectResult>((await controller.Update(_h.Building.Id, update, default)).Result).StatusCode);
        }

        var building = _h.T.NewContext().Buildings.Single(x => x.Id == _h.Building.Id);
        Assert.Equal(10m, building.MarketplaceCommissionPercent);
        Assert.Null(building.MarketplaceTransferInfo);
    }

    // ── Todos los controladores exigen sesion ────────────────────────────────

    [Fact]
    public void Todos_los_controladores_del_marketplace_exigen_sesion_y_ninguna_accion_es_anonima()
    {
        var controllers = typeof(MarketplaceController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.Name.StartsWith("Marketplace") && t.Name.EndsWith("Controller"))
            .ToList();
        Assert.True(controllers.Count >= 8, "No encontró los controladores del marketplace.");

        foreach (var controller in controllers)
        {
            var onType = controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).Any();
            Assert.True(onType, $"{controller.Name} no tiene [Authorize].");
            foreach (var action in controller.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                Assert.False(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true).Any(),
                    $"{controller.Name}.{action.Name} permite acceso anónimo.");
            }
        }
    }
}
