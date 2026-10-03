using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Marketplace;

/// <summary>Se omite sola si no hay un SQL Server real configurado (variable de entorno CONDO_TEST_SQLSERVER).</summary>
internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (TestDb.SqlServerServer is null)
        {
            Skip = $"Sin SQL Server real: definí {TestDb.SqlServerVariable} (p. ej. Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True).";
        }
    }
}

/// <summary>
/// Concurrencia contra SQL Server REAL (transacciones Serializable, indices unicos filtrados y reintentos como en produccion): varios
/// pedidos al mismo tiempo sobre lo mismo. En cada caso tiene que ganar exactamente uno, el resto recibe un conflicto claro (nunca una
/// excepcion sin controlar) y los importes quedan asentados una sola vez.
/// </summary>
public class MarketplaceConcurrencySqlServerTests
{
    private static async Task<(T? Value, Exception? Error)[]> Race<T>(params Func<Task<T>>[] work)
    {
        using var gate = new ManualResetEventSlim(false);
        var tasks = work.Select(w => Task.Run(async () =>
        {
            gate.Wait();
            try { return (await w(), (Exception?)null); }
            catch (Exception ex) { return (default(T), ex); }
        })).ToArray();
        gate.Set();
        return await Task.WhenAll(tasks);
    }

    private static void AssertNoCrashes<T>((T? Value, Exception? Error)[] results) =>
        Assert.True(results.All(r => r.Error is null), "Excepciones sin controlar: " + string.Join(" | ", results.Where(r => r.Error is not null).Select(r => r.Error!.GetType().Name + ": " + r.Error.Message)));

    private static int Wins<T>((MarketplaceResult<T>? Value, Exception? Error)[] results) => results.Count(r => r.Value!.Ok);

    private static IEnumerable<int> Statuses<T>((MarketplaceResult<T>? Value, Exception? Error)[] results) =>
        results.Where(r => !r.Value!.Ok).Select(r => r.Value!.Error!.StatusCode);

    // ── Reservas ─────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task Ocho_compradores_reservando_el_mismo_horario_a_la_vez_solo_uno_lo_consigue()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var buyers = new List<ApplicationUser> { h.Pedro, h.Maria };
        for (var i = 0; i < 6; i++)
        {
            var extra = h.T.AddUser(h.Company, UserRole.Resident, $"Vecino{i}");
            h.T.AddResident(h.BuyerUnit, extra);
            buyers.Add(extra);
        }

        var start = h.Listing.WindowStartUtc.Date.AddHours(19);
        var request = new MarketplaceQuoteRequest { ListingId = h.Listing.Id, StartsAtUtc = start, EndsAtUtc = start.AddHours(3) };

        var results = await Race(buyers.Select(b => (Func<Task<MarketplaceResult<MarketplaceReservationDto>>>)(
            () => h.BuildAs(b, "Resident").Reservations.ReserveAsync(request, CancellationToken.None))).ToArray());

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        Assert.All(Statuses(results), s => Assert.Equal(409, s));
        var db = h.T.NewContext();
        Assert.Equal(6, db.MarketplaceReservationSlots.Count(x => !x.IsDeleted));      // 3 horas = 6 bloques, de una sola reserva
        Assert.Equal(1, db.MarketplaceReservations.Count(x => x.Status == MarketplaceReservationStatus.PendingPayment));
    }

    [SqlServerFact]
    public async Task El_mismo_comprador_con_dos_reservas_simultaneas_solo_conserva_una_esperando_pago()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var day = h.Listing.WindowStartUtc.Date;

        var results = await Race(
            () => h.BuildAs(h.Pedro, "Resident").Reservations.ReserveAsync(
                new MarketplaceQuoteRequest { ListingId = h.Listing.Id, StartsAtUtc = day.AddHours(19), EndsAtUtc = day.AddHours(21) }, CancellationToken.None),
            () => h.BuildAs(h.Pedro, "Resident").Reservations.ReserveAsync(
                new MarketplaceQuoteRequest { ListingId = h.Listing.Id, StartsAtUtc = day.AddHours(22), EndsAtUtc = day.AddHours(24) }, CancellationToken.None));

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        Assert.Equal(1, h.T.NewContext().MarketplaceReservations.Count(x => x.BuyerUserId == h.Pedro.Id && x.Status == MarketplaceReservationStatus.PendingPayment));
    }

    // ── Pago ─────────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task Dos_revisores_confirmando_el_mismo_pago_a_la_vez_asientan_el_ingreso_una_sola_vez()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var start = h.Listing.WindowStartUtc.Date.AddHours(19);
        h.LoginAs(h.Pedro, "Resident");
        var reservation = (await h.Build().Reservations.ReserveAsync(
            new MarketplaceQuoteRequest { ListingId = h.Listing.Id, StartsAtUtc = start, EndsAtUtc = start.AddHours(3) }, CancellationToken.None)).Value!;
        await h.Build().Payments.SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/a.png" }, CancellationToken.None);
        var paymentId = h.T.NewContext().MarketplacePayments.Single().Id;
        var approve = new MarketplaceApproveRequest { ReviewedAmount = 66_000m };

        var results = await Race(
            () => h.BuildAs(h.Manager, "BuildingManager", h.Building).Payments.ApproveAsync(paymentId, approve, CancellationToken.None),
            () => h.BuildAs(h.Operator, "CompanyOperator", h.Building).Payments.ApproveAsync(paymentId, approve, CancellationToken.None));

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        var db = h.T.NewContext();
        Assert.Single(db.MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.PaymentIn).ToList());
        Assert.Equal(MarketplaceReservationStatus.Confirmed, db.MarketplaceReservations.Single().Status);
    }

    [SqlServerFact]
    public async Task Aprobar_y_rechazar_el_mismo_pago_a_la_vez_deja_un_unico_resultado_coherente()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var start = h.Listing.WindowStartUtc.Date.AddHours(19);
        h.LoginAs(h.Pedro, "Resident");
        var reservation = (await h.Build().Reservations.ReserveAsync(
            new MarketplaceQuoteRequest { ListingId = h.Listing.Id, StartsAtUtc = start, EndsAtUtc = start.AddHours(3) }, CancellationToken.None)).Value!;
        await h.Build().Payments.SubmitPaymentAsync(reservation.Id, new MarketplaceSubmitPaymentRequest { ComprobanteUrl = "/uploads/a.png" }, CancellationToken.None);
        var paymentId = h.T.NewContext().MarketplacePayments.Single().Id;

        var results = await Race(
            () => h.BuildAs(h.Manager, "BuildingManager", h.Building).Payments.ApproveAsync(paymentId, new MarketplaceApproveRequest { ReviewedAmount = 66_000m }, CancellationToken.None),
            () => h.BuildAs(h.Operator, "CompanyOperator", h.Building).Payments.RejectAsync(paymentId, new MarketplaceRejectRequest { Reason = "No coincide" }, CancellationToken.None));

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        var db = h.T.NewContext();
        var saved = db.MarketplaceReservations.Include(x => x.Slots).Single();
        var payment = db.MarketplacePayments.Single();
        if (saved.Status == MarketplaceReservationStatus.Confirmed)
        {
            Assert.Equal(MarketplacePaymentStatus.Approved, payment.Status);
            Assert.Single(db.MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.PaymentIn).ToList());
            Assert.Contains(saved.Slots, s => !s.IsDeleted);
        }
        else
        {
            Assert.Equal(MarketplaceReservationStatus.Rejected, saved.Status);
            Assert.Equal(MarketplacePaymentStatus.Rejected, payment.Status);
            Assert.Empty(db.MarketplaceAccountMovements.ToList());
            Assert.All(saved.Slots, s => Assert.True(s.IsDeleted));
        }
    }

    // ── Cancelaciones, reembolsos y reclamos ─────────────────────────────────

    [SqlServerFact]
    public async Task El_comprador_y_el_propietario_cancelando_a_la_vez_solo_uno_cancela_y_el_reembolso_es_unico()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var reservation = h.SeedConfirmed(startsInHours: 5);

        var results = await Race<MarketplaceResult<object>>(
            async () => Map(await h.BuildAs(h.Pedro, "Resident").Cancellations.CancelByBuyerAsync(reservation.Id, "Cambié de planes", CancellationToken.None)),
            async () => Map(await h.BuildAs(h.Juan, "Owner").Cancellations.CancelByOwnerAsync(reservation.Id, "Se me complicó", CancellationToken.None)));

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        var db = h.T.NewContext();
        var refund = Assert.Single(db.MarketplaceRefunds.ToList());
        var saved = db.MarketplaceReservations.Include(x => x.Slots).Single();
        Assert.Equal(MarketplaceReservationStatus.Cancelled, saved.Status);
        Assert.All(saved.Slots, s => Assert.True(s.IsDeleted));
        // Lo cobrado al propietario es coherente con quien cancelo.
        if (saved.CancelledBy == MarketplaceCancellationActor.Buyer)
        {
            Assert.Equal(60_000m, refund.Amount);
            Assert.Empty(db.MarketplaceOwnerDebts.ToList());
        }
        else
        {
            Assert.Equal(66_000m, refund.Amount);
            Assert.Equal(6_000m, Assert.Single(db.MarketplaceOwnerDebts.ToList()).Amount);
        }
    }

    [SqlServerFact]
    public async Task Comprador_y_propietario_reportando_a_la_vez_dejan_un_solo_reclamo_abierto()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var reservation = h.SeedConfirmed(startsInHours: -4, status: MarketplaceReservationStatus.Completed);

        var results = await Race(
            () => h.BuildAs(h.Pedro, "Resident").Claims.OpenAsync(reservation.Id, "No pude usarlo", CancellationToken.None),
            () => h.BuildAs(h.Juan, "Owner").Claims.OpenAsync(reservation.Id, "Lo dejaron sucio", CancellationToken.None));

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        var db = h.T.NewContext();
        Assert.Equal(1, db.MarketplaceClaims.Count(x => x.Status == MarketplaceClaimStatus.Open));
        Assert.Equal(MarketplaceCreditStatus.Held, db.MarketplaceReservations.Single().CreditStatus);
    }

    [SqlServerFact]
    public async Task Resolver_el_mismo_reclamo_para_los_dos_lados_a_la_vez_deja_un_resultado_coherente()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var reservation = h.SeedConfirmed(startsInHours: -4, status: MarketplaceReservationStatus.Completed);
        h.GiveCredit(h.Juan, 10_000m);
        var claim = (await h.BuildAs(h.Pedro, "Resident").Claims.OpenAsync(reservation.Id, "x", CancellationToken.None)).Value!;

        var results = await Race(
            () => h.BuildAs(h.Manager, "BuildingManager", h.Building).Claims.ResolveAsync(
                claim.Id, new MarketplaceClaimResolveRequest { Outcome = "InFavorOfBuyer", Note = "Se devuelve" }, CancellationToken.None),
            () => h.BuildAs(h.Operator, "CompanyOperator", h.Building).Claims.ResolveAsync(
                claim.Id, new MarketplaceClaimResolveRequest { Outcome = "InFavorOfOwner", Note = "Se acredita" }, CancellationToken.None));

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        var db = h.T.NewContext();
        var saved = db.MarketplaceReservations.Single();
        if (saved.CreditStatus == MarketplaceCreditStatus.None)
        {
            Assert.Equal(66_000m, Assert.Single(db.MarketplaceRefunds.ToList()).Amount);                 // gano el comprador
            Assert.Equal(4_000m, h.Balance(h.Juan));
        }
        else
        {
            Assert.Equal(MarketplaceCreditStatus.Pending, saved.CreditStatus);                           // gano el propietario
            Assert.Empty(db.MarketplaceRefunds.ToList());
            Assert.Equal(10_000m, h.Balance(h.Juan));
        }
    }

    [SqlServerFact]
    public async Task Tres_encargados_marcando_devuelto_el_mismo_reembolso_a_la_vez_asientan_una_sola_salida()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        var reservation = h.SeedConfirmed(startsInHours: 5);
        await h.BuildAs(h.Pedro, "Resident").Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None);
        var refundId = h.T.NewContext().MarketplaceRefunds.Single().Id;

        var results = await Race(
            () => h.BuildAs(h.Manager, "BuildingManager", h.Building).Refunds.MarkReturnedAsync(refundId, CancellationToken.None),
            () => h.BuildAs(h.Operator, "CompanyOperator", h.Building).Refunds.MarkReturnedAsync(refundId, CancellationToken.None),
            () => h.BuildAs(h.CompanyAdmin, "CompanyAdmin", h.Building).Refunds.MarkReturnedAsync(refundId, CancellationToken.None));

        AssertNoCrashes(results);
        Assert.Equal(1, Wins(results));
        var db = h.T.NewContext();
        Assert.Equal(-60_000m, Assert.Single(db.MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.RefundOut).ToList()).Amount);
        Assert.Equal(1, h.Notices(h.Pedro.Id, NotificationType.MarketplaceRefundReturned));
    }

    // ── Acreditacion al saldo ────────────────────────────────────────────────

    [SqlServerFact]
    public async Task Cuatro_procesos_acreditando_a_la_vez_acreditan_la_reserva_una_sola_vez()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        h.SeedConfirmed(startsInHours: -30, status: MarketplaceReservationStatus.Completed);

        var results = await Race(
            () => h.Build().Credits.CreditDueAsync(CancellationToken.None),
            () => h.Build().Credits.CreditDueAsync(CancellationToken.None),
            () => h.Build().Credits.CreditDueAsync(CancellationToken.None),
            () => h.Build().Credits.CreditDueAsync(CancellationToken.None));

        AssertNoCrashes(results);
        Assert.Equal(1, results.Sum(r => r.Value));
        var db = h.T.NewContext();
        Assert.Equal(60_000m, h.Balance(h.Juan));
        Assert.Single(db.OwnerCreditMovements.Where(x => x.Kind == OwnerCreditMovementKind.Generated).ToList());
        Assert.Single(db.MarketplaceAccountMovements.Where(x => x.Kind == MarketplaceAccountMovementKind.OwnerCredit).ToList());
    }

    [SqlServerFact]
    public async Task Cancelar_la_reserva_y_acreditar_a_la_vez_nunca_dejan_la_ganancia_y_el_reembolso_juntos()
    {
        using var h = new Phase7Harness(useSqlServer: true);
        // Reserva que ya terminó hace mas de 24 horas (se acredita) y el comprador intenta cancelar justo en ese momento: no se puede
        // cancelar una reserva empezada, asi que gana siempre la acreditacion y no se crea ningun reembolso.
        var reservation = h.SeedConfirmed(startsInHours: -30, status: MarketplaceReservationStatus.Completed);

        var results = await Race<object>(
            async () => await h.Build().Credits.CreditDueAsync(CancellationToken.None),
            async () => Map(await h.BuildAs(h.Pedro, "Resident").Cancellations.CancelByBuyerAsync(reservation.Id, null, CancellationToken.None)));

        AssertNoCrashes(results);
        var db = h.T.NewContext();
        Assert.Empty(db.MarketplaceRefunds.ToList());
        Assert.Equal(60_000m, h.Balance(h.Juan));
    }

    private static MarketplaceResult<object> Map<T>(MarketplaceResult<T> result) =>
        result.Ok ? MarketplaceResult<object>.Success(result.Value!) : MarketplaceResult<object>.Fail(result.Error!);
}
