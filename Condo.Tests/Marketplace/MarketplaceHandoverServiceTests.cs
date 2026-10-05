using System.Text;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Cambio de propietario principal con operaciones abiertas: se suspenden las publicaciones, las reservas conservan al propietario
/// original y se genera una nota interna (con su PDF) para el personal. La nota se completa sola si el nuevo principal llega despues.
/// </summary>
public class MarketplaceHandoverServiceTests : IDisposable
{
    private readonly Phase7Harness _h = new();
    private readonly ApplicationUser _ana;

    public MarketplaceHandoverServiceTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        _ana = _h.T.AddUser(_h.Company, name: "Ana");
    }

    public void Dispose() => _h.Dispose();

    private static void AssertError<T>(MarketplaceResult<T> result, int status)
    {
        Assert.False(result.Ok);
        Assert.Equal(status, result.Error!.StatusCode);
    }

    // Juan deja de ser el propietario principal de la 302 (se le da de baja el vinculo).
    private void RemoveJuanAsPrimary()
    {
        var link = _h.T.Db.UnitOwners.Single(x => x.UnitId == _h.Unit.Id && x.OwnerId == _h.Juan.Id);
        link.IsDeleted = true;
        _h.T.Db.SaveChanges();
    }

    private MarketplaceListing JuanListing() => _h.T.NewContext().MarketplaceListings.Single(x => x.Id == _h.Listing.Id);

    // ── Se genera la nota ────────────────────────────────────────────────────

    [Fact]
    public async Task Al_dejar_de_ser_principal_con_reservas_abiertas_se_crea_la_nota_y_se_avisa_al_personal()
    {
        var reservation = _h.SeedConfirmed(startsInHours: 5);
        RemoveJuanAsPrimary();

        var note = await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None);

        Assert.NotNull(note);
        Assert.Equal(MarketplaceHandoverTrigger.PrimaryRemoved, note!.Trigger);
        Assert.Equal(1, note.ReservationCount);
        Assert.Equal(_h.Juan.Id, note.PreviousOwnerId);
        Assert.Null(note.NewOwnerId);
        Assert.Contains("Juan Prueba", note.Content);
        Assert.Contains("302", note.Content);
        Assert.Contains(reservation.Reference, note.Content);
        Assert.Contains("no tiene otro propietario principal", note.Content);
        Assert.Contains("conservan", note.Content);                       // la ganancia sigue siendo de quien reservo con el
        Assert.Equal(1, _h.Notices(_h.Manager.Id, NotificationType.MarketplaceHandoverNote));
        Assert.Equal(1, _h.Notices(_h.Operator.Id, NotificationType.MarketplaceHandoverNote));
        Assert.Equal(1, _h.Notices(_h.CompanyAdmin.Id, NotificationType.MarketplaceHandoverNote));
        Assert.Equal(0, _h.Notices(_h.OtherManager.Id, NotificationType.MarketplaceHandoverNote));
        Assert.Equal(1, _h.Events("handover.created"));
    }

    [Fact]
    public async Task Las_publicaciones_del_que_dejo_de_ser_principal_se_suspenden_en_el_momento()
    {
        _h.SeedConfirmed(startsInHours: 5);
        RemoveJuanAsPrimary();
        Assert.Equal(MarketplaceListingStatus.Active, JuanListing().Status);

        await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None);

        var listing = JuanListing();
        Assert.Equal(MarketplaceListingStatus.Suspended, listing.Status);
        Assert.Contains("dejó de ser el principal", listing.StatusReason);
    }

    [Fact]
    public async Task Si_ya_hay_otro_principal_la_nota_lo_dice_y_queda_como_reemplazo()
    {
        _h.SeedConfirmed(startsInHours: 5);
        _h.T.AddOwner(_h.Unit, _ana, primary: true);
        RemoveJuanAsPrimary();

        var note = await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, _ana.Id, CancellationToken.None);

        Assert.Equal(MarketplaceHandoverTrigger.PrimaryReplaced, note!.Trigger);
        Assert.Equal(_ana.Id, note.NewOwnerId);
        Assert.Contains("El nuevo propietario principal es Ana Prueba", note.Content);
    }

    [Fact]
    public async Task Sin_operaciones_abiertas_no_se_crea_nota_pero_las_publicaciones_igual_se_suspenden()
    {
        _h.SeedConfirmed(startsInHours: 5, status: MarketplaceReservationStatus.Expired, credit: MarketplaceCreditStatus.None);
        _h.SeedConfirmed(startsInHours: 20, status: MarketplaceReservationStatus.Cancelled, credit: MarketplaceCreditStatus.None);
        _h.SeedConfirmed(startsInHours: -30, status: MarketplaceReservationStatus.Completed, credit: MarketplaceCreditStatus.Credited);
        RemoveJuanAsPrimary();

        var note = await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None);

        Assert.Null(note);
        Assert.Empty(_h.T.NewContext().MarketplaceHandoverNotes.ToList());
        Assert.Equal(MarketplaceListingStatus.Suspended, JuanListing().Status);
    }

    [Fact]
    public async Task Cuentan_como_abiertas_la_finalizada_sin_acreditar_el_reclamo_y_el_reembolso_pendiente()
    {
        _h.SeedConfirmed(startsInHours: -4, status: MarketplaceReservationStatus.Completed);                                          // acreditacion pendiente
        var held = _h.SeedConfirmed(startsInHours: -10, status: MarketplaceReservationStatus.Completed, credit: MarketplaceCreditStatus.Held);
        _h.T.Db.MarketplaceClaims.Add(new MarketplaceClaim
        {
            CompanyId = _h.Company.Id, ReservationId = held.Id, BuildingId = _h.Building.Id, OpenedByUserId = _h.Pedro.Id,
            OpenedBy = MarketplaceClaimParty.Buyer, Reason = "x"
        });
        var cancelled = _h.SeedConfirmed(startsInHours: 30, status: MarketplaceReservationStatus.Cancelled, credit: MarketplaceCreditStatus.None);
        _h.T.Db.MarketplaceRefunds.Add(new MarketplaceRefund
        {
            CompanyId = _h.Company.Id, ReservationId = cancelled.Id, BuildingId = _h.Building.Id, RecipientUserId = _h.Pedro.Id,
            Amount = 66_000m, Origin = MarketplaceRefundOrigin.OwnerCancellation, Reason = "x"
        });
        _h.T.Db.SaveChanges();
        RemoveJuanAsPrimary();

        var note = await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None);

        Assert.Equal(3, note!.ReservationCount);
    }

    [Fact]
    public async Task Las_reservas_de_otro_propietario_o_de_otra_unidad_no_cuentan()
    {
        var other = _h.SeedConfirmed(startsInHours: 5);
        other.OwnerId = _ana.Id;                                      // creada cuando el principal era otro
        _h.T.Db.SaveChanges();
        RemoveJuanAsPrimary();

        Assert.Null(await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None));
        Assert.Null(await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.BuyerUnit.Id, _h.Juan.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task Si_el_mismo_propietario_sigue_siendo_el_principal_no_hay_nota_ni_suspension()
    {
        _h.SeedConfirmed(startsInHours: 5);

        var note = await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, _h.Juan.Id, CancellationToken.None);

        Assert.Null(note);
        Assert.Equal(MarketplaceListingStatus.Active, JuanListing().Status);
    }

    // ── La nota espera al nuevo principal ────────────────────────────────────

    [Fact]
    public async Task Si_el_nuevo_principal_llega_despues_la_nota_se_completa_sola()
    {
        _h.SeedConfirmed(startsInHours: 5);
        RemoveJuanAsPrimary();
        await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None);

        Assert.True(await _h.BuildDocuments().Handover.OnPrimaryAssignedAsync(_h.Unit.Id, _ana.Id, CancellationToken.None));

        var note = Assert.Single(_h.T.NewContext().MarketplaceHandoverNotes.ToList());
        Assert.Equal(_ana.Id, note.NewOwnerId);
        Assert.Equal(MarketplaceHandoverTrigger.PrimaryReplaced, note.Trigger);
        Assert.Contains("Actualización", note.Content);
        Assert.Contains("Ana Prueba", note.Content);
        Assert.Equal(1, _h.Events("handover.completed"));

        // Una nota ya completada no se vuelve a tocar.
        Assert.False(await _h.BuildDocuments().Handover.OnPrimaryAssignedAsync(_h.Unit.Id, _ana.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Asignar_un_principal_a_una_unidad_sin_notas_pendientes_no_hace_nada()
    {
        Assert.False(await _h.BuildDocuments().Handover.OnPrimaryAssignedAsync(_h.Unit.Id, _ana.Id, CancellationToken.None));
        Assert.Empty(_h.T.NewContext().MarketplaceHandoverNotes.ToList());
    }

    [Fact]
    public async Task Una_nota_vieja_no_se_completa_con_un_alta_de_meses_despues()
    {
        _h.SeedConfirmed(startsInHours: 5);
        RemoveJuanAsPrimary();
        var note = (await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None))!;
        var row = _h.T.Db.MarketplaceHandoverNotes.Single(x => x.Id == note.Id);
        row.CreatedAtUtc = DateTime.UtcNow.AddDays(-90);
        _h.T.Db.SaveChanges();

        Assert.False(await _h.BuildDocuments().Handover.OnPrimaryAssignedAsync(_h.Unit.Id, _ana.Id, CancellationToken.None));
    }

    // ── Lectura por el personal ──────────────────────────────────────────────

    private async Task<MarketplaceHandoverNote> NoteWithOpenReservation()
    {
        _h.SeedConfirmed(startsInHours: 5);
        RemoveJuanAsPrimary();
        return (await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None))!;
    }

    [Fact]
    public async Task El_personal_ve_las_notas_no_leidas_y_abrirla_la_marca_como_leida_una_sola_vez()
    {
        var note = await NoteWithOpenReservation();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var unread = (await _h.BuildDocuments().Handover.GetNotesAsync(_h.Building.Id, false, CancellationToken.None)).Value!;
        Assert.Equal(note.Id, Assert.Single(unread).Id);
        Assert.Null(unread[0].ReadAtUtc);
        Assert.Equal("Juan Prueba", unread[0].PreviousOwnerName);
        Assert.Equal("302", unread[0].UnitCode);

        var opened = (await _h.BuildDocuments().Handover.GetNoteAsync(note.Id, CancellationToken.None)).Value!;
        Assert.Equal(_h.Manager.FullName, opened.ReadByName);
        var firstRead = opened.ReadAtUtc;
        Assert.NotNull(firstRead);

        // Otro Encargado/Operador que la abre despues no pisa quien la leyo primero.
        _h.LoginAsStaff(_h.Operator, "CompanyOperator");
        var again = (await _h.BuildDocuments().Handover.GetNoteAsync(note.Id, CancellationToken.None)).Value!;
        Assert.Equal(_h.Manager.FullName, again.ReadByName);
        Assert.Equal(firstRead, again.ReadAtUtc);
        Assert.Equal(1, _h.Events("handover.read"));

        Assert.Empty((await _h.BuildDocuments().Handover.GetNotesAsync(_h.Building.Id, false, CancellationToken.None)).Value!);
        Assert.Single((await _h.BuildDocuments().Handover.GetNotesAsync(_h.Building.Id, true, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task La_nota_muestra_la_situacion_actual_de_cada_operacion_y_no_la_del_momento_del_cambio()
    {
        var note = await NoteWithOpenReservation();
        var reservation = _h.T.NewContext().MarketplaceReservations.Single();

        // Despues del cambio, el comprador cancela la reserva.
        _h.LoginAs(_h.Pedro, "Resident");
        await _h.Build().Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None);

        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        var opened = (await _h.BuildDocuments().Handover.GetNoteAsync(note.Id, CancellationToken.None)).Value!;

        var op = Assert.Single(opened.Operations);
        Assert.Equal(reservation.Reference, op.Reference);
        Assert.Equal("Cancelled", op.Status);
        Assert.Equal("Pending", op.RefundStatus);
        Assert.Equal(60_000m, op.OwnerNetAmount);
        Assert.Contains("Operaciones abiertas al momento del cambio", opened.Content);   // el texto original no cambia
    }

    [Fact]
    public async Task Las_notas_son_solo_del_personal_del_edificio()
    {
        var note = await NoteWithOpenReservation();

        _h.LoginAsStaff(_h.OtherManager, "BuildingManager");
        AssertError(await _h.BuildDocuments().Handover.GetNotesAsync(_h.Building.Id, false, CancellationToken.None), 404);
        AssertError(await _h.BuildDocuments().Handover.GetNoteAsync(note.Id, CancellationToken.None), 404);

        _h.LoginAs(_h.Pedro, "Resident");
        AssertError(await _h.BuildDocuments().Handover.GetNotesAsync(_h.Building.Id, false, CancellationToken.None), 404);
        AssertError(await _h.BuildDocuments().Handover.GetNoteAsync(note.Id, CancellationToken.None), 404);

        // El propio propietario anterior tampoco la ve.
        _h.LoginAs(_h.Juan, "Owner");
        AssertError(await _h.BuildDocuments().Handover.GetNoteAsync(note.Id, CancellationToken.None), 404);
        AssertError(await _h.BuildDocuments().Handover.GetNoteAsync(Guid.NewGuid(), CancellationToken.None), 404);

        Assert.Null(_h.T.NewContext().MarketplaceHandoverNotes.Single().ReadAtUtc);
    }

    [Fact]
    public async Task La_nota_se_descarga_en_pdf_y_queda_leida()
    {
        var note = await NoteWithOpenReservation();
        _h.LoginAsStaff(_h.Manager, "BuildingManager");

        var pdf = await _h.BuildDocuments().Documents.GetHandoverPdfAsync(note.Id, CancellationToken.None);

        Assert.True(pdf.Ok, pdf.Error?.Message);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf.Value!.Bytes, 0, 5));
        Assert.Equal("cambio-propietario-unidad-302.pdf", pdf.Value.FileName);
        Assert.NotNull(_h.T.NewContext().MarketplaceHandoverNotes.Single().ReadAtUtc);

        _h.LoginAs(_h.Pedro, "Resident");
        AssertError(await _h.BuildDocuments().Documents.GetHandoverPdfAsync(note.Id, CancellationToken.None), 404);
    }

    [Fact]
    public async Task Con_muchas_operaciones_el_texto_no_pasa_el_maximo_y_la_nota_las_lista_todas()
    {
        for (var i = 0; i < 30; i++)
        {
            _h.SeedConfirmed(startsInHours: 5 + i * 4);
        }

        RemoveJuanAsPrimary();
        var note = (await _h.BuildDocuments().Handover.OnPrimaryRemovedAsync(_h.Unit.Id, _h.Juan.Id, null, CancellationToken.None))!;

        Assert.Equal(30, note.ReservationCount);
        Assert.True(note.Content.Length <= 4000);
        Assert.Contains("y 15 más", note.Content);
        _h.LoginAsStaff(_h.Manager, "BuildingManager");
        Assert.Equal(30, (await _h.BuildDocuments().Handover.GetNoteAsync(note.Id, CancellationToken.None)).Value!.Operations.Count);
    }

    // ── El gancho en el alta y la baja de propietarios de la unidad ──────────

    private sealed class NoSync : IOwnerResidencySyncService
    {
        public Task SyncAsync(ApplicationUser owner, Guid companyId, CancellationToken ct) => Task.CompletedTask;
    }

    private UnitOwnersController Controller()
    {
        var db = _h.T.NewContext();
        var gate = new MarketplaceModuleGate(db);
        var scope = new MarketplaceScope(db, _h.Tenant, _h.Access, gate);
        var audit = new MarketplaceAudit(db, _h.Tenant, new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() });
        var listings = new MarketplaceListingService(db, _h.Tenant, scope, audit);
        var push = new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance);
        var handover = new MarketplaceHandoverService(db, _h.Tenant, scope, audit, push, listings);
        return new UnitOwnersController(db, _h.Access, new NoSync(), handover, NullLogger<UnitOwnersController>.Instance, new OwnerCreditService(db), push);
    }

    private void LoginAsAdmin()
    {
        _h.LoginAs(_h.CompanyAdmin, "CompanyAdmin");
        _h.Access.Buildings.Clear();
        _h.Access.Buildings.Add(_h.Building.Id);
    }

    [Fact]
    public async Task Dar_de_baja_al_propietario_principal_desde_la_unidad_genera_la_nota_y_suspende_la_publicacion()
    {
        _h.SeedConfirmed(startsInHours: 5);
        LoginAsAdmin();
        var linkId = _h.T.NewContext().UnitOwners.Single(x => x.UnitId == _h.Unit.Id && x.OwnerId == _h.Juan.Id).Id;

        var result = await Controller().Delete(linkId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var note = Assert.Single(_h.T.NewContext().MarketplaceHandoverNotes.ToList());
        Assert.Equal(_h.Juan.Id, note.PreviousOwnerId);
        Assert.Null(note.NewOwnerId);
        Assert.Equal(MarketplaceListingStatus.Suspended, JuanListing().Status);
    }

    [Fact]
    public async Task Dar_de_baja_a_un_copropietario_que_no_es_el_principal_no_genera_nada()
    {
        _h.SeedConfirmed(startsInHours: 5);
        var second = _h.T.AddOwner(_h.Unit, _ana, primary: false);
        LoginAsAdmin();

        await Controller().Delete(second.Id, CancellationToken.None);

        Assert.Empty(_h.T.NewContext().MarketplaceHandoverNotes.ToList());
        Assert.Equal(MarketplaceListingStatus.Active, JuanListing().Status);
    }

    [Fact]
    public async Task El_cambio_de_principal_en_dos_pasos_baja_al_anterior_y_luego_alta_al_nuevo_deja_una_nota_completa()
    {
        _h.SeedConfirmed(startsInHours: 5);
        LoginAsAdmin();
        var linkId = _h.T.NewContext().UnitOwners.Single(x => x.UnitId == _h.Unit.Id && x.OwnerId == _h.Juan.Id).Id;
        await Controller().Delete(linkId, CancellationToken.None);

        var created = await Controller().Create(
            new CreateUnitOwnerRequest { UnitId = _h.Unit.Id, OwnerId = _ana.Id, IsPrimary = true, StartDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(created.Result);
        var note = Assert.Single(_h.T.NewContext().MarketplaceHandoverNotes.ToList());
        Assert.Equal(_ana.Id, note.NewOwnerId);
        Assert.Equal(MarketplaceHandoverTrigger.PrimaryReplaced, note.Trigger);
    }

    [Fact]
    public async Task El_cambio_en_el_orden_inverso_alta_del_nuevo_y_luego_baja_del_anterior_tambien_deja_la_nota()
    {
        _h.SeedConfirmed(startsInHours: 5);
        LoginAsAdmin();
        await Controller().Create(
            new CreateUnitOwnerRequest { UnitId = _h.Unit.Id, OwnerId = _ana.Id, IsPrimary = true, StartDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            CancellationToken.None);
        Assert.Empty(_h.T.NewContext().MarketplaceHandoverNotes.ToList());        // Juan sigue siendo principal: todavia no cambio nada

        var linkId = _h.T.NewContext().UnitOwners.Single(x => x.UnitId == _h.Unit.Id && x.OwnerId == _h.Juan.Id).Id;
        await Controller().Delete(linkId, CancellationToken.None);

        var note = Assert.Single(_h.T.NewContext().MarketplaceHandoverNotes.ToList());
        Assert.Equal(_ana.Id, note.NewOwnerId);
        Assert.Equal(MarketplaceHandoverTrigger.PrimaryReplaced, note.Trigger);
    }

    [Fact]
    public async Task Un_fallo_al_generar_la_nota_no_impide_la_baja_del_propietario()
    {
        _h.SeedConfirmed(startsInHours: 5);
        LoginAsAdmin();
        var linkId = _h.T.NewContext().UnitOwners.Single(x => x.UnitId == _h.Unit.Id && x.OwnerId == _h.Juan.Id).Id;
        var controller = Controller();
        // Se rompe el servicio de la nota: la baja del propietario igual queda hecha.
        _h.T.Db.Database.ExecuteSqlRaw("DROP TABLE MarketplaceHandoverNotes");

        var result = await controller.Delete(linkId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.True(_h.T.NewContext().UnitOwners.Single(x => x.Id == linkId).IsDeleted);
    }
}
