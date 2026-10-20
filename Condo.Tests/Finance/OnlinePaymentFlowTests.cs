using System.Text;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Marketplace;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Finance;

/// <summary>
/// Pagos online, recorrido completo con el proveedor falso: abrir un cobro, recibir el aviso firmado, aplicar el pago UNA sola vez y los
/// casos limite (duplicados, firma invalida, importe distinto, deuda que cambio, cobro simulado, aviso tardio). El dinero no pasa por Condo-PY:
/// estas pruebas no usan Bancard ni dinero.
/// </summary>
public class OnlinePaymentFlowTests : IDisposable
{
    private const string PrivateKey = "fake-signing-key-0123456789";

    private readonly FinanceEnv _env;
    private readonly AesGcmSecretProtector _protector = new(AesGcmSecretProtector.GenerateMasterKey(), "k1");
    private readonly PaymentConnection _connection;
    private readonly ApplicationUser _owner;
    private readonly Unit _unit;
    private readonly ExpensePeriod _january;
    private readonly ExpensePeriod _february;
    private readonly ExpenseCharge _januaryCharge;
    private readonly ExpenseCharge _februaryCharge;
    private readonly BuildingPaymentConfig _config;
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Payments:CollectionEnabled"] = "true",
        ["Payments:EnableFakeProvider"] = "true"
    };

    public OnlinePaymentFlowTests() : this(false)
    {
    }

    internal OnlinePaymentFlowTests(bool sqlServer, bool testBuilding = true)
    {
        _env = new FinanceEnv(sqlServer);
        _unit = _env.T.AddUnit(_env.Building, "A1");
        _owner = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.AddOwner(_unit, _owner);
        _january = AddPeriod(1);
        _february = AddPeriod(2);
        _januaryCharge = AddCharge(_january, 100_000m);
        _februaryCharge = AddCharge(_february, 60_000m);

        _connection = new PaymentConnection
        {
            CompanyId = _env.Company.Id, Name = "Simulador", ProviderCode = PaymentProviderCatalog.FakeProvider,
            ProductCode = PaymentProviderCatalog.FakeProduct, Environment = PaymentEnvironment.Sandbox, Status = PaymentConnectionStatus.Active,
            HolderName = "Administradora Sol S.A.", HolderTaxId = "80012345-6", StatusChangedByUserId = _env.Admin.Id,
            KeyId = "k1"
        };
        _connection.SecretsCipher = _protector.Protect(new Dictionary<string, string> { ["privateKey"] = PrivateKey }, _connection.PublicId);
        _env.T.Db.PaymentConnections.Add(_connection);
        _config = new BuildingPaymentConfig
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, ConnectionId = _connection.Id, IsTest = testBuilding
        };
        _env.T.Db.BuildingPaymentConfigs.Add(_config);
        _env.T.Db.SaveChanges();
    }

    public void Dispose() => _env.Dispose();

    // ── Armado ───────────────────────────────────────────────────────────────

    private ExpensePeriod AddPeriod(int month)
    {
        var first = new DateOnly(2025, month, 1);
        var period = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = 2025, Month = month, Name = $"{month:00}/2025",
            StartDate = first, EndDate = first.AddMonths(1).AddDays(-1), DueDate = first.AddDays(20), Status = ExpensePeriodStatus.Published
        };
        _env.T.Db.ExpensePeriods.Add(period);
        _env.T.Db.SaveChanges();
        return period;
    }

    private ExpenseCharge AddCharge(ExpensePeriod period, decimal amount, string concept = "Expensas")
    {
        var charge = new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = period.Id, UnitId = _unit.Id, ChargeType = ExpenseChargeType.Ordinary,
            Concept = concept, Amount = amount
        };
        _env.T.Db.ExpenseCharges.Add(charge);
        _env.T.Db.SaveChanges();
        return charge;
    }

    private OnlinePaymentSettings Settings() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());

    private OnlinePaymentService Service(CondoDbContext? db = null)
    {
        db ??= _env.T.Db;
        var credits = new OwnerCreditService(db);
        var comprobantes = new ComprobanteService(db, credits);
        return new OnlinePaymentService(
            db, credits, comprobantes, new OwnerPaymentSettlementService(db, credits, comprobantes), new InvoiceDraftService(db, _env.Access),
            new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance), _protector,
            new PaymentProviderRegistry([new FakePaymentProvider()]), Settings(), NullLogger<OnlinePaymentService>.Instance);
    }

    private Task<OnlinePaymentResult<OnlinePaymentDto>> Create(ExpensePeriod? through = null, OnlinePaymentService? service = null) =>
        (service ?? Service()).CreateAsync(
            _owner.Id, _env.Company.Id,
            new CreateOnlinePaymentRequest { BuildingId = _env.Building.Id, ThroughUnitId = _unit.Id, ThroughExpensePeriodId = (through ?? _january).Id },
            "https://api.test", "1.2.3.4", default);

    private async Task<OnlinePaymentDto> CreateOk(ExpensePeriod? through = null)
    {
        var result = await Create(through);
        Assert.True(result.Ok, $"{result.Code}: {result.Message}");
        return result.Value!;
    }

    private CondoDbContext Fresh() => _env.T.NewContext();

    private PaymentAttempt Attempt(long number) => Fresh().PaymentAttempts.Single(x => x.AttemptNumber == number);

    private Task<WebhookResult> Send(
        PaymentAttempt attempt, string status = "confirmed", decimal? amount = null, string? key = null, string? tx = null,
        string? session = null, OnlinePaymentService? service = null, string? publicId = null)
    {
        var (body, signature) = FakePaymentProvider.BuildSignedCallback(
            key ?? PrivateKey, session ?? attempt.ProviderSessionId!, attempt.ProviderReference, status, amount ?? attempt.Amount, transactionId: tx);
        return (service ?? Service()).HandleCallbackAsync(
            publicId ?? _connection.PublicId, body, new Dictionary<string, string> { [FakePaymentProvider.SignatureHeader] = signature }, "1.2.3.4", default);
    }

    private List<PaymentEvent> Events() => Fresh().PaymentEvents.OrderBy(x => x.ReceivedAtUtc).ToList();

    private PaymentIntent IntentOf(Guid id) => Fresh().PaymentIntents.Include(x => x.Items).Single(x => x.Id == id);

    // ═════════════════════════════════════════════════════════════════════════
    // Proveedor falso
    // ═════════════════════════════════════════════════════════════════════════

    private static ProviderConnectionInfo Info() => new(
        Guid.NewGuid(), "pid", PaymentEnvironment.Sandbox, new Dictionary<string, string>(), new Dictionary<string, string> { ["privateKey"] = PrivateKey });

    [Fact]
    public void ElAvisoFirmadoSeVerificaYSeNormaliza()
    {
        var (body, signature) = FakePaymentProvider.BuildSignedCallback(PrivateKey, "FAKE-7", "7", "confirmed", 100_000m, transactionId: "TK-77");

        var parsed = new FakePaymentProvider().ParseCallback(Info(), body, new Dictionary<string, string> { [FakePaymentProvider.SignatureHeader] = signature });

        Assert.True(parsed.Valid);
        Assert.True(parsed.SignatureOk);
        Assert.Equal(ProviderPaymentStatus.Confirmed, parsed.Callback!.Status);
        Assert.Equal(100_000m, parsed.Callback.Amount);
        Assert.Equal("FAKE-7", parsed.Callback.SessionId);
        Assert.Equal("fake:TK-77:confirmed", parsed.Callback.DedupeKey);
        Assert.DoesNotContain(PrivateKey, parsed.Callback.RedactedPayloadJson);
    }

    [Fact]
    public void UnAvisoSinFirmaConFirmaAjenaOAlteradoNoEsValido()
    {
        var provider = new FakePaymentProvider();
        var (body, signature) = FakePaymentProvider.BuildSignedCallback(PrivateKey, "FAKE-7", "7", "confirmed", 100_000m);

        var noHeader = provider.ParseCallback(Info(), body, new Dictionary<string, string>());
        var wrongKey = provider.ParseCallback(Info(), body, new Dictionary<string, string> { [FakePaymentProvider.SignatureHeader] = FakePaymentProvider.Sign("otra-clave", body) });
        var tampered = provider.ParseCallback(Info(), body.Replace("100000", "1"), new Dictionary<string, string> { [FakePaymentProvider.SignatureHeader] = signature });
        var garbage = "no es json";
        var notJson = provider.ParseCallback(Info(), garbage, new Dictionary<string, string> { [FakePaymentProvider.SignatureHeader] = FakePaymentProvider.Sign(PrivateKey, garbage) });

        Assert.All(new[] { noHeader, wrongKey, tampered }, r => { Assert.False(r.Valid); Assert.False(r.SignatureOk); });
        Assert.False(notJson.Valid);
        Assert.True(notJson.SignatureOk);   // la firma era buena pero el cuerpo no se puede leer
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Opciones y creacion del cobro
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LasOpcionesMuestranElEdificioElMedioDePagoYLosComprobantesConSuAcumulado()
    {
        var options = await Service().GetOptionsAsync(_owner.Id, _env.Company.Id, default);

        Assert.True(options.CollectionEnabled);
        var building = Assert.Single(options.Buildings);
        Assert.Equal(_env.Building.Id, building.BuildingId);
        Assert.Null(building.BlockedReason);
        var connection = Assert.Single(building.Connections);
        Assert.Equal("Administradora Sol S.A.", connection.HolderName);
        Assert.False(connection.IsSimulated);   // edificio de prueba: el cobro SI se aplica
        Assert.Equal([100_000m, 160_000m], building.Comprobantes.Select(c => c.CumulativeTotal).ToArray());
        Assert.Equal(1, building.Comprobantes[0].PeriodMonth);
    }

    [Fact]
    public async Task SinElCobroHabilitadoEnElServidorNadiePuedeAbrirUnPago()
    {
        _settings["Payments:CollectionEnabled"] = "false";

        var result = await Create();

        Assert.False(result.Ok);
        Assert.Equal("collection_disabled", result.Code);
        Assert.Empty(Fresh().PaymentIntents.ToList());
    }

    [Fact]
    public async Task CrearUnCobroGuardaCobroItemsIntentoYEventoConElTitularCongelado()
    {
        var dto = await CreateOk();

        Assert.Equal("Open", dto.Status);
        Assert.Equal(1, dto.Number);
        Assert.Equal(100_000m, dto.Amount);
        Assert.Equal("Administradora Sol S.A.", dto.HolderName);
        Assert.Equal("80012345-6", dto.HolderTaxId);
        Assert.Equal(1, dto.AttemptNumber);
        Assert.Equal("Pending", dto.AttemptStatus);
        Assert.Contains("/fake-checkout/FAKE-1", dto.CheckoutUrl);
        Assert.Single(dto.Items);

        var intent = IntentOf(dto.Id);
        Assert.True(Assert.Single(intent.Items).IsOpen);
        Assert.Contains("Expensas", intent.Items.First().SnapshotJson);
        var attempt = Attempt(1);
        Assert.Equal("FAKE-1", attempt.ProviderSessionId);
        Assert.Equal("1", attempt.ProviderReference);
        var created = Assert.Single(Events());
        Assert.Equal(PaymentEventOutcomes.Created, created.Outcome);
        Assert.Equal(PaymentEventDirection.Outbound, created.Direction);

        // Cambiar el titular despues no reescribe a nombre de quien se cobro.
        var connection = _env.T.Db.PaymentConnections.Single();
        connection.HolderName = "Otro titular";
        _env.T.Db.SaveChanges();
        Assert.Equal("Administradora Sol S.A.", IntentOf(dto.Id).HolderName);
    }

    [Fact]
    public async Task HastaEsteComprobanteIncluyeTodosLosAnterioresDelMasAntiguo()
    {
        var first = await CreateOk(_january);
        Assert.Equal(100_000m, first.Amount);
        Assert.Single(first.Items);
        await Send(Attempt(1), "rejected");   // se libera

        var second = await CreateOk(_february);

        Assert.Equal(160_000m, second.Amount);
        Assert.Equal([1, 2], second.Items.Select(i => i.PeriodMonth).ToArray());
    }

    [Fact]
    public async Task ElImporteSeRedondeaAlGuaraniYElPagoNoPierdeNada()
    {
        _env.T.Db.ExpenseCharges.Single(x => x.Id == _februaryCharge.Id).Amount = 60_000.40m;
        _env.T.Db.SaveChanges();

        var dto = await CreateOk(_february);

        Assert.Equal(160_000m, dto.Amount);
        await Send(Attempt(1));
        Assert.Equal(PaymentIntentStatus.Paid, IntentOf(dto.Id).Status);
        Assert.Equal(2, Fresh().Payments.Count());
    }

    [Fact]
    public async Task UnComprobanteNoPuedeEstarEnDosCobrosAbiertos()
    {
        await CreateOk(_january);

        var again = await Create(_january);
        var wider = await Create(_february);

        Assert.Equal("payment_in_progress", again.Code);
        Assert.Equal("payment_in_progress", wider.Code);
        Assert.Single(Fresh().PaymentIntents.ToList());
    }

    [Fact]
    public async Task UnPagoDeclaradoPendienteBloqueaElCobroOnline()
    {
        _env.T.Db.OwnerPayments.Add(new OwnerPayment
        {
            CompanyId = _env.Company.Id, OwnerId = _owner.Id, PaymentDate = new DateOnly(2026, 10, 1), DeclaredAmount = 100_000m,
            Status = OwnerPaymentStatus.Pending, Reference = "PAY-2026-000001"
        });
        _env.T.Db.SaveChanges();

        var result = await Create();
        var options = await Service().GetOptionsAsync(_owner.Id, _env.Company.Id, default);

        Assert.Equal("declared_payment_pending", result.Code);
        Assert.NotNull(options.Buildings[0].BlockedReason);
    }

    [Fact]
    public async Task ConUnCobroEnCursoElPropietarioNoPuedeDeclararNiElPersonalRegistrarOtroPago()
    {
        await CreateOk();
        var credits = new OwnerCreditService(_env.T.Db);
        var comprobantes = new ComprobanteService(_env.T.Db, credits);
        var api = new OwnerPaymentsController(
            _env.T.Db, _env.Tenant, _env.Access, credits, comprobantes, new InvoiceDraftService(_env.T.Db, _env.Access),
            new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance),
            new OwnerPaymentSettlementService(_env.T.Db, credits, comprobantes));
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Access.Buildings.Add(_env.Building.Id);

        _env.Tenant.Role = "Owner";
        _env.Tenant.UserId = _owner.Id;
        var declared = await api.Create(new OwnerPaymentCreateRequest { DeclaredAmount = 100_000m, PaymentDate = new DateOnly(2026, 10, 1), UnitIds = [_unit.Id] }, default);
        _env.Tenant.Role = "CompanyAdmin";
        _env.Tenant.UserId = _env.Admin.Id;
        var registered = await api.Register(new OwnerPaymentRegisterRequest { OwnerId = _owner.Id, Amount = 100_000m, PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow) }, default);

        Assert.Contains("pago online en curso", Assert.IsType<ConflictObjectResult>(declared.Result).Value!.ToString());
        Assert.Contains("pago online en curso", Assert.IsType<ConflictObjectResult>(registered.Result).Value!.ToString());
        Assert.Empty(Fresh().OwnerPayments.ToList());
    }

    [Fact]
    public async Task SinElSimuladoHabilitadoElMedioDePagoNoEstaDisponible()
    {
        _settings["Payments:EnableFakeProvider"] = "false";

        var result = await Create();
        var options = await Service().GetOptionsAsync(_owner.Id, _env.Company.Id, default);

        Assert.Equal("no_connection", result.Code);
        Assert.Empty(options.Buildings);
    }

    [Fact]
    public async Task UnaConexionInactivaOSinTitularNoCobra()
    {
        var connection = _env.T.Db.PaymentConnections.Single();
        connection.Status = PaymentConnectionStatus.Disabled;
        _env.T.Db.SaveChanges();
        Assert.Equal("no_connection", (await Create()).Code);

        connection.Status = PaymentConnectionStatus.Active;
        connection.HolderName = null;
        _env.T.Db.SaveChanges();
        Assert.Equal("holder_missing", (await Create()).Code);
    }

    [Fact]
    public async Task UnComprobanteDeOtroEdificioOQueNoEsDelPropietarioSeRechaza()
    {
        var other = _env.T.AddBuilding(_env.Company, "Otro");
        var result = await Service().CreateAsync(
            _owner.Id, _env.Company.Id,
            new CreateOnlinePaymentRequest { BuildingId = other.Id, ThroughUnitId = _unit.Id, ThroughExpensePeriodId = _january.Id }, "https://api.test", null, default);
        var wrongComprobante = await Service().CreateAsync(
            _owner.Id, _env.Company.Id,
            new CreateOnlinePaymentRequest { BuildingId = _env.Building.Id, ThroughUnitId = Guid.NewGuid(), ThroughExpensePeriodId = _january.Id }, "https://api.test", null, default);

        Assert.Equal("no_debt", result.Code);
        Assert.Equal("comprobante_not_found", wrongComprobante.Code);
        Assert.Empty(Fresh().PaymentIntents.ToList());
    }

    [Fact]
    public async Task UnCobroVencidoSeCierraYLiberaLosComprobantes()
    {
        var dto = await CreateOk();
        var intent = _env.T.Db.PaymentIntents.Single();
        intent.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        _env.T.Db.SaveChanges();

        var closed = await Service().ExpireOverdueAsync(null, default);

        Assert.Equal(1, closed);
        var read = IntentOf(dto.Id);
        Assert.Equal(PaymentIntentStatus.Closed, read.Status);
        Assert.Equal("Expired", read.CloseReason);
        Assert.False(read.Items.First().IsOpen);
        Assert.Equal(PaymentAttemptStatus.Expired, Attempt(1).Status);
        Assert.Contains(Events(), e => e.Outcome == PaymentEventOutcomes.Expired);
        Assert.True((await Create()).Ok);   // el comprobante vuelve a estar libre
    }

    [Fact]
    public async Task UnCobroVencidoSeCierraSoloAlCrearOConsultar()
    {
        await CreateOk();
        _env.T.Db.PaymentIntents.Single().ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        _env.T.Db.SaveChanges();

        var again = await Create();   // sin esperar al proceso en segundo plano

        Assert.True(again.Ok);
        Assert.Equal(2, again.Value!.Number);
    }

    private sealed class FailingProvider : IPaymentProvider
    {
        public string ProviderCode => PaymentProviderCatalog.FakeProvider;
        public string ProductCode => PaymentProviderCatalog.FakeProduct;
        public ProviderCapabilities Capabilities => new(true, true);
        public Task<ProviderSession> CreateSessionAsync(ProviderConnectionInfo c, CreateSessionRequest r, CancellationToken ct) =>
            throw new HttpRequestException("proveedor caido");
        public CallbackParseResult ParseCallback(ProviderConnectionInfo c, string b, IReadOnlyDictionary<string, string> h) =>
            CallbackParseResult.Invalid("no aplica");
    }

    [Fact]
    public async Task SiElProveedorNoPuedeAbrirLaSesionElCobroSeCierraYLosComprobantesQuedanLibres()
    {
        var credits = new OwnerCreditService(_env.T.Db);
        var comprobantes = new ComprobanteService(_env.T.Db, credits);
        var service = new OnlinePaymentService(
            _env.T.Db, credits, comprobantes, new OwnerPaymentSettlementService(_env.T.Db, credits, comprobantes),
            new InvoiceDraftService(_env.T.Db, _env.Access), new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance),
            _protector, new PaymentProviderRegistry([new FailingProvider()]), Settings(), NullLogger<OnlinePaymentService>.Instance);

        var result = await Create(service: service);

        Assert.Equal("provider_error", result.Code);
        Assert.Equal(502, result.Status);
        var intent = Fresh().PaymentIntents.Include(x => x.Items).Single();
        Assert.Equal(PaymentIntentStatus.Closed, intent.Status);
        Assert.Equal("ProviderError", intent.CloseReason);
        Assert.False(intent.Items.First().IsOpen);
        Assert.Contains(Events(), e => e.Outcome == PaymentEventOutcomes.Failed && e.Error!.Contains("proveedor caido"));
        Assert.True((await Create()).Ok);
    }

    // ── Proceso en segundo plano ─────────────────────────────────────────────

    private PaymentIntentMaintenanceService Maintenance()
    {
        var provider = new ServiceCollection().AddSingleton(Service()).BuildServiceProvider();
        return new PaymentIntentMaintenanceService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PaymentIntentMaintenanceService>.Instance);
    }

    [Fact]
    public async Task ElProcesoVenceSoloLosCobrosAbiertosQuePasaronSuPlazoDeTodosLosPropietarios()
    {
        var overdue = await CreateOk();   // propietario 1: vencido
        _env.T.Db.PaymentIntents.Single(x => x.Id == overdue.Id).ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);

        var unit2 = _env.T.AddUnit(_env.Building, "B1");
        var owner2 = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.AddOwner(unit2, owner2);
        _env.T.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = _january.Id, UnitId = unit2.Id, ChargeType = ExpenseChargeType.Ordinary, Concept = "Expensas", Amount = 80_000m
        });
        _env.T.Db.SaveChanges();
        var current = await Service().CreateAsync(
            owner2.Id, _env.Company.Id,
            new CreateOnlinePaymentRequest { BuildingId = _env.Building.Id, ThroughUnitId = unit2.Id, ThroughExpensePeriodId = _january.Id }, "https://api.test", null, default);
        Assert.True(current.Ok);   // propietario 2: todavia dentro del plazo

        var closed = await Maintenance().RunOnceAsync(default);

        Assert.Equal(1, closed);
        Assert.Equal(PaymentIntentStatus.Closed, IntentOf(overdue.Id).Status);
        Assert.Equal("Expired", IntentOf(overdue.Id).CloseReason);
        Assert.Equal(PaymentIntentStatus.Open, IntentOf(current.Value!.Id).Status);
        Assert.True(IntentOf(current.Value.Id).Items.All(i => i.IsOpen));
        // Una segunda pasada no encuentra nada que vencer.
        Assert.Equal(0, await Maintenance().RunOnceAsync(default));
    }

    [Fact]
    public async Task ElProcesoNoTocaCobrosPagadosNiEnRevision()
    {
        var paid = await CreateOk(_january);
        await Send(Attempt(1));
        Assert.Equal(PaymentIntentStatus.Paid, IntentOf(paid.Id).Status);
        // Aunque su plazo ya haya pasado, un cobro pagado sigue pagado.
        _env.T.Db.PaymentIntents.Single(x => x.Id == paid.Id).ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-30);
        _env.T.Db.SaveChanges();

        var closed = await Maintenance().RunOnceAsync(default);

        Assert.Equal(0, closed);
        Assert.Equal(PaymentIntentStatus.Paid, IntentOf(paid.Id).Status);
        Assert.Single(Fresh().Payments.ToList());
    }

    [Fact]
    public async Task ElProcesoSeDetieneLimpioAlCancelarse()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Maintenance().StartAsync(cancel.Token);
        await Maintenance().StopAsync(CancellationToken.None);   // no lanza aunque se cancele durante la espera inicial
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Aviso confirmado: se aplica una sola vez
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnAvisoConfirmadoAplicaElPagoConLaMismaLiquidacionDeSiempre()
    {
        var dto = await CreateOk(_february);

        var response = await Send(Attempt(1), tx: "TK-1001");

        Assert.True(response.Success, $"{response.StatusCode} {response.Message} | {string.Join(" ; ", Events().Select(e => e.Outcome + ":" + e.Error))}");
        Assert.Equal(200, response.StatusCode);

        var db = Fresh();
        var intent = db.PaymentIntents.Include(x => x.Items).Single();
        Assert.Equal(PaymentIntentStatus.Paid, intent.Status);
        Assert.All(intent.Items, i => Assert.False(i.IsOpen));

        var ownerPayment = db.OwnerPayments.Include(x => x.Units).Single();
        Assert.Equal(intent.OwnerPaymentId, ownerPayment.Id);
        Assert.Equal(intent.Id, ownerPayment.PaymentIntentId);
        Assert.Equal(OwnerPaymentStatus.Approved, ownerPayment.Status);
        Assert.Equal(OwnerPaymentChannel.Online, ownerPayment.Channel);
        Assert.Equal(PaymentMethod.Online, ownerPayment.Method);
        Assert.Equal(160_000m, ownerPayment.DeclaredAmount);
        Assert.Equal("TK-1001", ownerPayment.ExternalReference);
        Assert.Matches(@"^PAY-\d{4}-000001$", ownerPayment.Reference);
        Assert.Equal(160_000m, ownerPayment.Units.Single().AllocatedAmount);

        var payments = db.Payments.Include(x => x.Allocations).ToList();
        Assert.Equal(2, payments.Count);   // un Payment por comprobante
        Assert.All(payments, p => Assert.Equal(PaymentMethod.Online, p.Method));
        Assert.Contains("Pago online confirmado por el proveedor", payments[0].Notes);

        var attempt = db.PaymentAttempts.Single();
        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal("TK-1001", attempt.ProviderTransactionId);
        Assert.Equal("FAKE", attempt.CardBrand);
        Assert.Equal("0000", attempt.CardLast4);
        Assert.NotNull(attempt.ConfirmedAtUtc);

        var applied = Events().Single(e => e.Direction == PaymentEventDirection.Inbound);
        Assert.Equal(PaymentEventOutcomes.Applied, applied.Outcome);
        Assert.True(applied.SignatureOk);
        Assert.Equal("fake:TK-1001:confirmed", applied.DedupeKey);
        Assert.DoesNotContain(PrivateKey, applied.PayloadJson);
        _ = dto;
    }

    [Fact]
    public async Task ElPagoOnlineGeneraElAvisoAlPropietarioYLosBorradoresDeFacturaSinSesion()
    {
        // El aviso del proveedor llega sin sesion: nadie tiene el edificio asignado, y aun asi los borradores se generan.
        _env.Access.Buildings.Clear();
        await CreateOk(_february);

        await Send(Attempt(1));

        var notice = Assert.Single(Fresh().Notifications.Where(n => n.RecipientId == _owner.Id).ToList());
        Assert.Equal(NotificationType.PaymentApproved, notice.Type);
        Assert.Contains("Tu pago online fue aprobado", notice.Title);
        var drafts = Fresh().Invoices.ToList();
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d =>
        {
            Assert.Equal(InvoiceStatus.Draft, d.Status);
            Assert.Equal(_env.Admin.Id, d.CreatedByUserId);   // quien activo la conexion
        });
    }

    [Fact]
    public async Task ElAvisoPuedeApagarseCuandoElEdificioDesactivoElAvisoDePagoRecibido()
    {
        _env.T.Db.BuildingNoticeRules.Add(new BuildingNoticeRule
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Kind = NoticeKind.PaymentReceived, IsActive = false
        });
        _env.T.Db.SaveChanges();
        await CreateOk();

        await Send(Attempt(1));

        Assert.Empty(Fresh().Notifications.Where(n => n.RecipientId == _owner.Id).ToList());
        Assert.Single(Fresh().OwnerPayments.ToList());
    }

    [Fact]
    public async Task ElMismoAvisoDosVecesResponderExitoYNoAplicaNadaNuevo()
    {
        await CreateOk();
        var attempt = Attempt(1);

        var first = await Send(attempt, tx: "TK-1");
        var second = await Send(attempt, tx: "TK-1");
        var third = await Send(attempt, tx: "TK-1");

        Assert.True(first.Success && second.Success && third.Success);
        Assert.Single(Fresh().OwnerPayments.ToList());
        Assert.Single(Fresh().Payments.ToList());
        Assert.Single(Fresh().Invoices.ToList());
        var inbound = Events().Where(e => e.Direction == PaymentEventDirection.Inbound).ToList();
        Assert.Equal(3, inbound.Count);
        Assert.Equal([PaymentEventOutcomes.Applied, PaymentEventOutcomes.Duplicate, PaymentEventOutcomes.Duplicate], inbound.Select(e => e.Outcome).ToArray());
        Assert.Single(inbound, e => e.DedupeKey != null);
    }

    [Fact]
    public async Task OtroAvisoDeConfirmacionDelMismoIntentoConOtraTransaccionNoPagaDeNuevo()
    {
        await CreateOk();
        var attempt = Attempt(1);

        await Send(attempt, tx: "TK-1");
        var again = await Send(attempt, tx: "TK-2");   // otra clave, mismo intento ya confirmado

        Assert.True(again.Success);
        Assert.Single(Fresh().OwnerPayments.ToList());
        Assert.Equal(PaymentEventOutcomes.Duplicate, Events().Last().Outcome);
    }

    [Fact]
    public async Task UnaFirmaInvalidaSeRechazaSeRegistraYNoCambiaNada()
    {
        var dto = await CreateOk();

        var response = await Send(Attempt(1), key: "clave-de-un-atacante");

        Assert.False(response.Success);
        Assert.Equal(401, response.StatusCode);
        Assert.Equal(PaymentIntentStatus.Open, IntentOf(dto.Id).Status);
        Assert.Empty(Fresh().OwnerPayments.ToList());
        var rejected = Events().Last();
        Assert.Equal(PaymentEventOutcomes.InvalidSignature, rejected.Outcome);
        Assert.False(rejected.SignatureOk);
        Assert.Null(rejected.DedupeKey);   // no bloquea un aviso legitimo posterior
        Assert.True((await Send(Attempt(1))).Success);
    }

    [Fact]
    public async Task UnImporteDistintoAlDelCobroSeRechazaYElCobroSigueAbierto()
    {
        var dto = await CreateOk();

        var response = await Send(Attempt(1), amount: 1_000m);

        Assert.False(response.Success);
        Assert.Equal(409, response.StatusCode);
        Assert.Equal(PaymentIntentStatus.Open, IntentOf(dto.Id).Status);
        Assert.Equal(PaymentAttemptStatus.Pending, Attempt(1).Status);
        Assert.Empty(Fresh().Payments.ToList());
        Assert.Equal(PaymentEventOutcomes.AmountMismatch, Events().Last().Outcome);
    }

    [Fact]
    public async Task UnAvisoDeUnCobroQueNoExisteSeRegistraYSeResponde404()
    {
        await CreateOk();
        var attempt = Attempt(1);
        var (body, signature) = FakePaymentProvider.BuildSignedCallback(PrivateKey, "FAKE-999", "999", "confirmed", attempt.Amount);

        var response = await Service().HandleCallbackAsync(
            _connection.PublicId, body, new Dictionary<string, string> { [FakePaymentProvider.SignatureHeader] = signature }, "1.2.3.4", default);

        Assert.Equal(404, response.StatusCode);
        Assert.False(response.Success);
        Assert.Equal(PaymentEventOutcomes.UnknownAttempt, Events().Last().Outcome);
        Assert.Empty(Fresh().OwnerPayments.ToList());
    }

    [Fact]
    public async Task UnaConexionDesconocidaOBorradorNoRecibeAvisos()
    {
        await CreateOk();
        var attempt = Attempt(1);

        var unknown = await Send(attempt, publicId: new string('a', 32));
        var malformed = await Send(attempt, publicId: "no-es-un-identificador");
        var connection = _env.T.Db.PaymentConnections.Single();
        connection.Status = PaymentConnectionStatus.Draft;
        _env.T.Db.SaveChanges();
        var draft = await Send(attempt);

        Assert.Equal(404, unknown.StatusCode);
        Assert.Equal(404, malformed.StatusCode);
        Assert.Equal(404, draft.StatusCode);
        Assert.Empty(Fresh().OwnerPayments.ToList());
    }

    [Fact]
    public async Task UnAvisoDeRechazoCierraElCobroLiberaLosComprobantesYSePuedeReintentar()
    {
        var dto = await CreateOk();

        var response = await Send(Attempt(1), "rejected");

        Assert.True(response.Success);
        var intent = IntentOf(dto.Id);
        Assert.Equal(PaymentIntentStatus.Closed, intent.Status);
        Assert.Equal("Rejected", intent.CloseReason);
        Assert.False(intent.Items.First().IsOpen);
        Assert.Equal(PaymentAttemptStatus.Rejected, Attempt(1).Status);
        Assert.Empty(Fresh().Payments.ToList());
        // Un reintento es un intento (y cobro) nuevo pedido por el propietario.
        var retry = await Create();
        Assert.True(retry.Ok);
        Assert.Equal(2, retry.Value!.AttemptNumber);
    }

    [Fact]
    public async Task UnAvisoPendienteSoloSeRegistra()
    {
        var dto = await CreateOk();

        var response = await Send(Attempt(1), "pending");

        Assert.True(response.Success);
        Assert.Equal(PaymentIntentStatus.Open, IntentOf(dto.Id).Status);
        Assert.Equal(PaymentEventOutcomes.Ignored, Events().Last().Outcome);
    }

    // ── Cobros de prueba ─────────────────────────────────────────────────────

    [Fact]
    public async Task UnCobroDelSimuladoEnUnEdificioRealQuedaSimuladoYNoToqueLaDeudaReal()
    {
        using var real = new OnlinePaymentFlowTests(sqlServer: false, testBuilding: false);
        var created = await real.CreateOk();
        Assert.True(created.IsSimulated);

        var response = await real.Send(real.Attempt(1));

        Assert.True(response.Success);
        var intent = real.IntentOf(created.Id);
        Assert.Equal(PaymentIntentStatus.Closed, intent.Status);
        Assert.Equal("Simulated", intent.CloseReason);
        Assert.False(intent.Items.First().IsOpen);
        Assert.Equal(PaymentAttemptStatus.Confirmed, real.Attempt(1).Status);
        Assert.Empty(real.Fresh().OwnerPayments.ToList());
        Assert.Empty(real.Fresh().Payments.ToList());
        Assert.Equal(PaymentEventOutcomes.Simulated, real.Events().Last().Outcome);
        // La deuda sigue intacta: el propietario todavia debe el comprobante.
        Assert.Single((await real.Service().GetOptionsAsync(real._owner.Id, real._env.Company.Id, default)).Buildings[0].Comprobantes, c => c.PeriodMonth == 1);
    }

    [Fact]
    public async Task ElEdificioDePruebaSiAplicaElCobroDelSimulado()
    {
        var created = await CreateOk();

        Assert.False(created.IsSimulated);
        await Send(Attempt(1));

        Assert.Single(Fresh().Payments.ToList());
    }

    // ── Cuando no se puede aplicar ───────────────────────────────────────────

    [Fact]
    public async Task SiLaDeudaCambioElCobroPasaARevisionNoSeAplicaYElProveedorDevuelveElDinero()
    {
        var dto = await CreateOk(_february);
        AddCharge(_february, 2_000m, "Mora");   // un recargo entre la apertura y la confirmacion

        var response = await Send(Attempt(1));

        // El simulador, como Tpago, devuelve el dinero si no se le responde exito.
        Assert.False(response.Success);
        Assert.Equal(409, response.StatusCode);
        var intent = IntentOf(dto.Id);
        Assert.Equal(PaymentIntentStatus.ReviewRequired, intent.Status);
        Assert.Contains("importe", intent.ReviewReason);
        Assert.All(intent.Items, i => Assert.False(i.IsOpen));
        Assert.Equal(PaymentAttemptStatus.Confirmed, Attempt(1).Status);
        Assert.Empty(Fresh().OwnerPayments.ToList());
        Assert.Empty(Fresh().Payments.ToList());
        Assert.Equal(PaymentEventOutcomes.ReviewRequired, Events().Last().Outcome);

        var notice = Assert.Single(Fresh().Notifications.Where(n => n.Type == NotificationType.OnlinePaymentReviewRequired).ToList());
        Assert.Equal(_env.Admin.Id, notice.RecipientId);
        Assert.Equal(intent.Id, notice.EntityId);

        // Si el proveedor repite el aviso, recibe la misma respuesta y no se duplica nada.
        var repeated = await Send(Attempt(1));
        Assert.False(repeated.Success);
        Assert.Single(Fresh().Notifications.Where(n => n.Type == NotificationType.OnlinePaymentReviewRequired).ToList());
    }

    [Fact]
    public async Task SiUnComprobanteYaEstabaPagadoElCobroPasaARevision()
    {
        var dto = await CreateOk(_february);
        // Se salda enero por otro camino mientras el cobro seguia abierto.
        var payment = new Payment
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = _january.Id, UnitId = _unit.Id, PaymentDate = new DateOnly(2026, 10, 1), Amount = 100_000m,
            Method = PaymentMethod.Cash, Reference = "OTRO-1"
        };
        _env.T.Db.Payments.Add(payment);
        _env.T.Db.PaymentAllocations.Add(new PaymentAllocation
        {
            CompanyId = _env.Company.Id, PaymentId = payment.Id, ExpenseChargeId = _januaryCharge.Id, AllocatedAmount = 100_000m
        });
        _env.T.Db.SaveChanges();

        var response = await Send(Attempt(1));

        Assert.False(response.Success);
        var intent = IntentOf(dto.Id);
        Assert.Equal(PaymentIntentStatus.ReviewRequired, intent.Status);
        Assert.Contains("ya estaba pagado", intent.ReviewReason);
        Assert.Empty(Fresh().OwnerPayments.ToList());
    }

    [Fact]
    public async Task UnAvisoTardioSeAplicaSiLosComprobantesSiguenLibres()
    {
        var dto = await CreateOk();
        _env.T.Db.PaymentIntents.Single().ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        _env.T.Db.SaveChanges();
        await Service().ExpireOverdueAsync(null, default);
        Assert.Equal(PaymentIntentStatus.Closed, IntentOf(dto.Id).Status);

        var response = await Send(Attempt(1));

        Assert.True(response.Success);
        Assert.Equal(PaymentIntentStatus.Paid, IntentOf(dto.Id).Status);
        Assert.Single(Fresh().OwnerPayments.ToList());
    }

    [Fact]
    public async Task UnAvisoTardioConLosComprobantesEnOtroCobroAbiertoPasaARevision()
    {
        var first = await CreateOk();
        _env.T.Db.PaymentIntents.Single().ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        _env.T.Db.SaveChanges();
        var second = await Create();   // vencio el primero: el segundo toma el mismo comprobante
        Assert.True(second.Ok);

        var response = await Send(Attempt(1));   // llega tarde el aviso del primero

        Assert.False(response.Success);
        var intent = IntentOf(first.Id);
        Assert.Equal(PaymentIntentStatus.ReviewRequired, intent.Status);
        Assert.Contains("otro cobro abierto", intent.ReviewReason);
        Assert.Equal(PaymentIntentStatus.Open, IntentOf(second.Value!.Id).Status);   // el segundo sigue su curso
        Assert.Empty(Fresh().OwnerPayments.ToList());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Controladores
    // ═════════════════════════════════════════════════════════════════════════

    private OnlinePaymentsController Controller(string role, Guid userId, bool fullScope = true)
    {
        _env.Tenant.Role = role;
        _env.Tenant.UserId = userId;
        _env.Tenant.CompanyId = _env.Company.Id;
        if (fullScope) _env.Access.Buildings.Add(_env.Building.Id);
        return new OnlinePaymentsController(Service(), _env.Tenant, _env.Access, Settings())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Request = { Host = new HostString("api.test") } } }
        };
    }

    [Fact]
    public async Task ElPropietarioAbreYConsultaSuCobroPeroNoElDeOtro()
    {
        var api = Controller("Owner", _owner.Id);

        var created = Assert.IsType<OkObjectResult>((await api.Create(
            new CreateOnlinePaymentRequest { BuildingId = _env.Building.Id, ThroughUnitId = _unit.Id, ThroughExpensePeriodId = _january.Id }, default)).Result);
        var dto = Assert.IsType<OnlinePaymentDto>(created.Value);
        Assert.Contains("api.test", Fresh().PaymentAttempts.Single().CheckoutUrl);

        Assert.IsType<OkObjectResult>((await api.GetById(dto.Id, default)).Result);
        var stranger = _env.T.AddUser(_env.Company, UserRole.Owner);
        Assert.IsType<NotFoundResult>((await Controller("Owner", stranger.Id).GetById(dto.Id, default)).Result);
        Assert.IsType<ForbidResult>((await Controller("CompanyAdmin", _env.Admin.Id).GetOptions(default)).Result);
    }

    [Fact]
    public async Task ElPersonalVeLosCobrosDeSuAlcanceYSoloElAdministradorVeLosEventos()
    {
        var dto = await CreateOk();
        await Send(Attempt(1));

        var admin = Controller("CompanyAdmin", _env.Admin.Id);
        var detail = Assert.IsType<OnlinePaymentDto>(Assert.IsType<OkObjectResult>((await admin.GetAdmin(dto.Id, null, default)).Result).Value);
        Assert.NotNull(detail.Events);
        Assert.Contains(detail.Events!, e => e.Outcome == PaymentEventOutcomes.Applied);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OnlinePaymentDto>>(
            Assert.IsType<OkObjectResult>((await admin.GetAll(null, null, null, 100, default)).Result).Value));

        var operatorWithScope = Controller("CompanyOperator", _env.Admin.Id);
        var operatorDetail = Assert.IsType<OnlinePaymentDto>(Assert.IsType<OkObjectResult>((await operatorWithScope.GetAdmin(dto.Id, null, default)).Result).Value);
        Assert.Null(operatorDetail.Events);

        _env.Access.Buildings.Clear();   // un encargado sin ese edificio no lo ve
        var outside = Controller("BuildingManager", _env.Admin.Id, fullScope: false);
        Assert.IsType<NotFoundResult>((await outside.GetAdmin(dto.Id, null, default)).Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<OnlinePaymentDto>>(
            Assert.IsType<OkObjectResult>((await outside.GetAll(null, null, null, 100, default)).Result).Value));
        Assert.IsType<ForbidResult>((await Controller("Owner", _owner.Id).GetAll(null, null, null, 100, default)).Result);
    }

    private static PaymentWebhooksController Webhook(OnlinePaymentService service, string body, string? signature)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (signature is not null) context.Request.Headers[FakePaymentProvider.SignatureHeader] = signature;
        return new PaymentWebhooksController(service) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Fact]
    public async Task ElWebhookResponde_status_success_SoloCuandoElPagoQuedoRegistrado()
    {
        await CreateOk();
        var attempt = Attempt(1);
        var (body, signature) = FakePaymentProvider.BuildSignedCallback(PrivateKey, attempt.ProviderSessionId!, attempt.ProviderReference, "confirmed", attempt.Amount);

        var ok = Assert.IsType<ObjectResult>(await Webhook(Service(), body, signature).Receive(_connection.PublicId, default));
        var bad = Assert.IsType<ObjectResult>(await Webhook(Service(), body, "firma-mala").Receive(_connection.PublicId, default));

        Assert.Equal(200, ok.StatusCode);
        Assert.Equal("{ status = success }", ok.Value!.ToString());
        Assert.Equal(401, bad.StatusCode);
        Assert.Contains("error", bad.Value!.ToString());
        Assert.Single(Fresh().OwnerPayments.ToList());
    }

    [Fact]
    public async Task ElWebhookRechazaUnCuerpoDemasiadoGrande()
    {
        var result = Assert.IsType<ObjectResult>(await Webhook(Service(), new string('x', 70_000), null).Receive(_connection.PublicId, default));

        Assert.Equal(413, result.StatusCode);
    }

    [Fact]
    public void ElWebhookTieneSuPropioLimiteDeTasaPorConexion()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/payment-webhooks/{_connection.PublicId}";
        context.Request.Method = "POST";

        var partition = MarketplaceRateLimiting.GetPartition(context);

        Assert.StartsWith("webhook:", partition.PartitionKey);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Simulador
    // ═════════════════════════════════════════════════════════════════════════

    private PaymentFakeController Simulator(string role = "CompanyAdmin", bool enabled = true)
    {
        _settings["Payments:EnableFakeProvider"] = enabled ? "true" : "false";
        _env.Tenant.Role = role;
        _env.Tenant.UserId = _env.Admin.Id;
        _env.Tenant.CompanyId = _env.Company.Id;
        return new PaymentFakeController(_env.T.Db, Service(), _env.Tenant, _env.Access, _protector, Settings())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task ElSimuladorConfirmaPorElMismoCaminoDelWebhookConAvisoFirmado()
    {
        await CreateOk();

        var result = Assert.IsType<OkObjectResult>(await Simulator().Confirm(1, null, default));

        Assert.Contains("success = True", result.Value!.ToString());
        Assert.Equal(PaymentIntentStatus.Paid, IntentOf(Fresh().PaymentIntents.Single().Id).Status);
        var inbound = Events().Single(e => e.Direction == PaymentEventDirection.Inbound);
        Assert.True(inbound.SignatureOk);   // paso por la verificacion de firma real
        Assert.Equal(PaymentEventOutcomes.Applied, inbound.Outcome);
    }

    [Fact]
    public async Task ElSimuladorPuedeRechazarYSimularUnImporteDistinto()
    {
        await CreateOk();

        var mismatch = Assert.IsType<OkObjectResult>(await Simulator().Confirm(1, new FakePaymentRequest { Amount = 5m }, default));
        Assert.Contains("success = False", mismatch.Value!.ToString());
        Assert.Equal(PaymentEventOutcomes.AmountMismatch, Events().Last().Outcome);

        var reject = Assert.IsType<OkObjectResult>(await Simulator().Reject(1, default));
        Assert.Contains("success = True", reject.Value!.ToString());
        Assert.Equal(PaymentAttemptStatus.Rejected, Attempt(1).Status);
        Assert.Empty(Fresh().Payments.ToList());
    }

    [Fact]
    public async Task ElSimuladorSoloExisteConElProveedorFalsoEncendidoYParaQuienAdministra()
    {
        await CreateOk();

        Assert.IsType<NotFoundResult>(await Simulator(enabled: false).Confirm(1, null, default));
        Assert.IsType<ForbidResult>(await Simulator(role: "CompanyOperator").Confirm(1, null, default));
        Assert.IsType<NotFoundResult>(await Simulator().Confirm(999, null, default));
        Assert.Empty(Fresh().Payments.ToList());
    }

    [Fact]
    public async Task ElSimuladorNoActuaSobreUnIntentoYaResuelto()
    {
        await CreateOk();
        await Simulator().Confirm(1, null, default);

        var again = await Simulator().Confirm(1, null, default);

        Assert.IsType<ConflictObjectResult>(again);
        Assert.Single(Fresh().OwnerPayments.ToList());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Concurrencia contra SQL Server real
    // ═════════════════════════════════════════════════════════════════════════

    [SqlServerFact]
    public async Task CincoAvisosIgualesALaVezAplicanElPagoUnaSolaVez()
    {
        using var sql = new OnlinePaymentFlowTests(sqlServer: true);
        var created = await sql.CreateOk(sql._february);
        var attempt = sql.Attempt(1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
        {
            var db = sql._env.T.NewContext();
            return sql.Send(attempt, tx: "TK-CONCURRENT", service: sql.Service(db));
        }));

        Assert.All(responses, r => Assert.True(r.Success, $"{r.StatusCode} {r.Message}"));
        Assert.Single(sql.Fresh().OwnerPayments.Where(x => x.PaymentIntentId == created.Id).ToList());
        Assert.Equal(2, sql.Fresh().Payments.Count());
        Assert.Equal(PaymentIntentStatus.Paid, sql.IntentOf(created.Id).Status);
        Assert.Single(sql.Fresh().PaymentEvents.Where(e => e.DedupeKey == "fake:TK-CONCURRENT:confirmed").ToList());
    }

    [SqlServerFact]
    public async Task DosCobrosDelMismoComprobanteALaVezSoloAbreUno()
    {
        using var sql = new OnlinePaymentFlowTests(sqlServer: true);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => sql.Create(sql._january, sql.Service(sql._env.T.NewContext()))));

        Assert.Single(results, r => r.Ok);
        Assert.All(results.Where(r => !r.Ok), r => Assert.Equal("payment_in_progress", r.Code));
        Assert.Single(sql.Fresh().PaymentIntents.ToList());
    }
}
