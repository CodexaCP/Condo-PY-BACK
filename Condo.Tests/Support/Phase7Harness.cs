using Condo.Api.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Condo.Tests.Support;

/// <summary>
/// Escenario comun de las pruebas de la fase 7 (cancelaciones, reembolsos, reclamos, aviso de inicio y deudas por gestion):
/// un edificio con el modulo habilitado, Juan (propietario principal que publica), Pedro y Maria (compradores), un Encargado
/// con el edificio en su alcance, un Operador y un Encargado de OTRO edificio. Las reservas se siembran ya confirmadas.
/// </summary>
internal sealed class Phase7Harness : IDisposable
{
    public readonly TestDb T;
    public readonly FakeTenantContext Tenant = new();
    public readonly FakeAccessScope Access;

    public readonly Company Company;
    public readonly Building Building;
    public readonly Building OtherBuilding;
    public readonly Unit Unit;          // 302: Juan publica
    public readonly Unit BuyerUnit;     // 101: Pedro y Maria
    public readonly ApplicationUser Juan;
    public readonly ApplicationUser Pedro;
    public readonly ApplicationUser Maria;
    public readonly ApplicationUser Manager;
    public readonly ApplicationUser Operator;
    public readonly ApplicationUser OtherManager;
    public readonly ApplicationUser CompanyAdmin;
    public readonly ApplicationUser SuperAdmin;
    public readonly MarketplaceListing Listing;
    private int _ref;

    public Phase7Harness(bool useSqlServer = false)
    {
        T = new TestDb(useSqlServer);
        Access = new FakeAccessScope(Tenant);
        Company = T.AddCompany();
        CompanyAdmin = T.AddUser(Company, UserRole.CompanyAdmin, "Admin");
        Building = T.AddBuilding(Company, "Edificio A");
        OtherBuilding = T.AddBuilding(Company, "Edificio B");
        Unit = T.AddUnit(Building, "302");
        BuyerUnit = T.AddUnit(Building, "101");
        T.AssignPlan(Building, CompanyAdmin, includesMarketplace: true);
        T.EnableMarketplace(Building);

        Juan = T.AddUser(Company, name: "Juan");
        T.AddOwner(Unit, Juan, primary: true);
        Pedro = T.AddUser(Company, UserRole.Resident, "Pedro");
        T.AddResident(BuyerUnit, Pedro);
        Maria = T.AddUser(Company, UserRole.Resident, "Maria");
        T.AddResident(BuyerUnit, Maria);

        Manager = T.AddUser(Company, UserRole.BuildingManager, "Encargado");
        T.AddBuildingAccess(Manager, Building);
        Operator = T.AddUser(Company, UserRole.CompanyOperator, "Operador");
        T.AddBuildingAccess(Operator, Building);
        OtherManager = T.AddUser(Company, UserRole.BuildingManager, "OtroEncargado");
        T.AddBuildingAccess(OtherManager, OtherBuilding);
        SuperAdmin = T.AddUser(null, UserRole.SuperAdmin, "Super");

        Listing = T.AddListing(Building, Unit, Juan, 20_000m);
    }

    public void Dispose()
    {
        T.Dispose();
        try { if (Directory.Exists(WebRoot)) Directory.Delete(WebRoot, recursive: true); } catch (IOException) { /* temporal: no importa */ }
    }

    // Carpeta web de prueba (las imagenes subidas viven en WebRoot/uploads).
    public readonly string WebRoot = Path.Combine(Path.GetTempPath(), "condo-tests-" + Guid.NewGuid().ToString("N"));

    // ── Sesion ───────────────────────────────────────────────────────────────

    public void LoginAs(ApplicationUser user, string role)
    {
        Tenant.UserId = user.Id;
        Tenant.Role = role;
        Tenant.CompanyId = user.CompanyId;
    }

    public void LoginAsStaff(ApplicationUser staff, string role)
    {
        LoginAs(staff, role);
        Access.Buildings.Clear();
        foreach (var access in T.NewContext().UserBuildingAccesses.Where(x => x.ApplicationUserId == staff.Id && !x.IsDeleted))
        {
            Access.Buildings.Add(access.BuildingId);
        }
    }

    // ── Servicios ────────────────────────────────────────────────────────────

    public sealed record Services(
        MarketplaceReservationService Reservations,
        MarketplaceCancellationService Cancellations,
        MarketplaceRefundService Refunds,
        MarketplaceClaimService Claims,
        MarketplaceStartNoticeService StartNotices,
        MarketplaceCreditService Credits,
        MarketplaceAccountService Account,
        MarketplacePaymentService Payments);

    public Services Build() => BuildFor(Tenant, Access);

    // Servicios para OTRA sesion (otro usuario con su propio contexto): lo que hace falta para simular pedidos simultaneos.
    public Services BuildAs(ApplicationUser user, string role, params Building[] staffBuildings)
    {
        var s = NewSession(user, role, null, staffBuildings);
        return BuildFor(s.Tenant, s.Access);
    }

    // Una sesion completa (usuario, rol, empresa del token y edificios del personal) para armar servicios y documentos.
    public sealed record Session(FakeTenantContext Tenant, FakeAccessScope Access);

    // companyId distinto del de la cuenta = un token que dice otra empresa (para probar que no abre puertas).
    public Session NewSession(ApplicationUser user, string role, Guid? companyId = null, params Building[] staffBuildings)
    {
        var tenant = new FakeTenantContext { UserId = user.Id, Role = role, CompanyId = companyId ?? user.CompanyId };
        var access = new FakeAccessScope(tenant);
        foreach (var building in staffBuildings)
        {
            access.Buildings.Add(building.Id);
        }

        return new Session(tenant, access);
    }

    public Services Build(Session session) => BuildFor(session.Tenant, session.Access);

    private Services BuildFor(FakeTenantContext tenant, FakeAccessScope access)
    {
        var db = T.NewContext();
        var gate = new MarketplaceModuleGate(db);
        var scope = new MarketplaceScope(db, tenant, access, gate);
        var audit = new MarketplaceAudit(db, tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var listings = new MarketplaceListingService(db, tenant, scope, audit);
        var push = new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance);
        var reservations = new MarketplaceReservationService(db, tenant, scope, audit, listings, push, Options.Create(new MarketplaceOptions()));
        var credits = new OwnerCreditService(db);
        var refunds = new MarketplaceRefundService(db, tenant, scope, audit, push, credits);
        var cancellations = new MarketplaceCancellationService(db, tenant, scope, audit, push, reservations, refunds);
        var claims = new MarketplaceClaimService(db, tenant, scope, audit, push, refunds);
        var startNotices = new MarketplaceStartNoticeService(db, tenant, scope, audit, push);
        var creditService = new MarketplaceCreditService(db, tenant, audit, push);
        var account = new MarketplaceAccountService(db, tenant, scope, audit);
        var payments = new MarketplacePaymentService(db, tenant, scope, audit, push, new StubOverdueService());
        return new Services(reservations, cancellations, refunds, claims, startNotices, creditService, account, payments);
    }

    public sealed record DocServices(
        MarketplaceHandoverService Handover,
        MarketplaceDocumentService Documents,
        MarketplaceListingService Listings);

    // Documentos de la fase 8: comprobante en PDF, notas de cambio de propietario principal e historial.
    public DocServices BuildDocuments() => BuildDocuments(new Session(Tenant, Access));

    public DocServices BuildDocuments(Session session)
    {
        var tenant = session.Tenant;
        var db = T.NewContext();
        var gate = new MarketplaceModuleGate(db);
        var scope = new MarketplaceScope(db, tenant, session.Access, gate);
        var audit = new MarketplaceAudit(db, tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var listings = new MarketplaceListingService(db, tenant, scope, audit);
        var push = new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance);
        var handover = new MarketplaceHandoverService(db, tenant, scope, audit, push, listings);
        var documents = new MarketplaceDocumentService(db, tenant, scope, audit, handover, new StubWebHostEnvironment(WebRoot));
        return new DocServices(handover, documents, listings);
    }

    // ── Datos sembrados ──────────────────────────────────────────────────────

    /// <summary>
    /// Reserva de Pedro ya confirmada (pagada y aprobada): importes 3 h x 20.000 = 60.000 base, 6.000 de comision, 66.000 total,
    /// con sus bloques de 30 minutos ocupados y el ingreso asentado en la cuenta aparte. startsInHours negativo = ya empezo.
    /// </summary>
    public MarketplaceReservation SeedConfirmed(double startsInHours = 5, int hours = 3, ApplicationUser? buyer = null,
        MarketplaceReservationStatus status = MarketplaceReservationStatus.Confirmed,
        MarketplaceCreditStatus credit = MarketplaceCreditStatus.Pending)
    {
        var startsAt = DateTime.UtcNow.AddHours(startsInHours);
        var reservation = T.NewReservation(Listing, buyer ?? Pedro, $"MP-{++_ref:D8}", status);
        reservation.StartsAtUtc = startsAt;
        reservation.EndsAtUtc = startsAt.AddHours(hours);
        reservation.Hours = hours;
        reservation.ExpiresAtUtc = null;
        reservation.CreditStatus = credit;
        T.Db.MarketplaceReservations.Add(reservation);
        T.Db.SaveChanges();

        for (var i = 0; i < hours * 2; i++)
        {
            T.Db.MarketplaceReservationSlots.Add(T.NewSlot(reservation, startsAt.AddMinutes(30 * i)));
        }

        // Solo una reserva confirmada o finalizada tiene un pago aprobado y su ingreso asentado en la cuenta.
        if (status is not (MarketplaceReservationStatus.Confirmed or MarketplaceReservationStatus.Completed))
        {
            T.Db.SaveChanges();
            return reservation;
        }

        T.Db.MarketplaceAccountMovements.Add(new MarketplaceAccountMovement
        {
            CompanyId = Company.Id,
            BuildingId = Building.Id,
            Kind = MarketplaceAccountMovementKind.PaymentIn,
            Amount = reservation.TotalAmount,
            ReservationId = reservation.Id,
            Concept = $"Pago de reserva {reservation.Reference}",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-1)
        });
        T.Db.MarketplacePayments.Add(new MarketplacePayment
        {
            CompanyId = Company.Id,
            ReservationId = reservation.Id,
            BuildingId = Building.Id,
            BuyerUserId = reservation.BuyerUserId,
            ComprobanteUrl = "/uploads/x.png",
            ExpectedAmount = reservation.TotalAmount,
            ReviewedAmount = reservation.TotalAmount,
            Status = MarketplacePaymentStatus.Approved
        });
        T.Db.SaveChanges();
        return reservation;
    }

    public void GiveCredit(ApplicationUser owner, decimal amount, bool withLot = true)
    {
        T.Db.OwnerCredits.Add(new OwnerCredit { CompanyId = Company.Id, OwnerId = owner.Id, Amount = amount });
        if (withLot)
        {
            T.Db.OwnerCreditMovements.Add(new OwnerCreditMovement
            {
                CompanyId = Company.Id,
                OwnerId = owner.Id,
                Kind = OwnerCreditMovementKind.Generated,
                Amount = amount,
                RemainingAmount = amount,
                SourceReference = "PAGO-1",
                Description = "Saldo de prueba"
            });
        }

        T.Db.SaveChanges();
    }

    // ── Consultas ────────────────────────────────────────────────────────────

    public decimal Balance(ApplicationUser owner) =>
        T.NewContext().OwnerCredits.Where(x => x.OwnerId == owner.Id && x.CompanyId == Company.Id).AsEnumerable().Sum(x => x.Amount);

    public decimal AccountBalance() =>
        T.NewContext().MarketplaceAccountMovements.Where(x => x.BuildingId == Building.Id && !x.IsDeleted).AsEnumerable().Sum(x => x.Amount);

    public MarketplaceReservation Reload(Guid reservationId) =>
        T.NewContext().MarketplaceReservations.Include(x => x.Slots).Single(x => x.Id == reservationId);

    public int Notices(Guid recipientId, NotificationType type) =>
        T.NewContext().Notifications.Count(x => x.RecipientId == recipientId && x.Type == type);

    public int Events(string action) => T.NewContext().MarketplaceEvents.Count(x => x.Action == action);
}
