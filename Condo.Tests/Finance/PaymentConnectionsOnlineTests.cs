using System.Text.Json;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Finance;

/// <summary>
/// Configuracion de pagos online, lo que suma el cobro: el titular del comercio (obligatorio para activar), el simulador (solo si el servidor lo
/// habilita y nunca en produccion), los edificios de prueba y la cuenta de "cobros online por liquidar" que se crea al asignar un edificio con Finanzas.
/// </summary>
public class PaymentConnectionsOnlineTests : IDisposable
{
    private readonly FinanceEnv _env = new();
    private readonly AesGcmSecretProtector _protector = new(AesGcmSecretProtector.GenerateMasterKey(), "k1");
    private readonly Building _plain;   // un edificio SIN Finanzas habilitado
    private readonly Dictionary<string, string?> _settings = new() { ["Payments:CallbackBaseUrl"] = "https://api.tramiya.com.py" };

    public PaymentConnectionsOnlineTests()
    {
        _plain = _env.T.AddBuilding(_env.Company, "Edificio sin Finanzas");
        _env.Tenant.Role = "CompanyAdmin";
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Tenant.UserId = _env.Admin.Id;
        _env.Tenant.Email = "admin@test.local";
        _env.Access.Buildings.Add(_env.Building.Id);
        _env.Access.Buildings.Add(_plain.Id);
    }

    public void Dispose() => _env.Dispose();

    private PaymentConnectionsController Controller() => new(
        _env.T.Db, _env.Tenant, _env.Access, _protector, new ConfigurationBuilder().AddInMemoryCollection(_settings).Build(),
        NullLogger<PaymentConnectionsController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private PaymentConnectionRequest TpagoRequest(string name = "Bancard Sol", string? holderName = "Administradora Sol S.A.", string? holderTaxId = "80012345-6") => new()
    {
        CompanyId = _env.Company.Id, Name = name, ProviderCode = "Bancard", ProductCode = "Tpago", Environment = "Sandbox",
        Settings = new() { ["commerceCode"] = "COM-001", ["branchCode"] = "1", ["publicKey"] = "pk_public_ABCDEF" },
        Secrets = new() { ["privateKey"] = "sk_live_0123456789abcdefWXYZ" },
        HolderName = holderName, HolderTaxId = holderTaxId
    };

    private PaymentConnectionRequest FakeRequest(string environment = "Sandbox") => new()
    {
        CompanyId = _env.Company.Id, Name = "Simulador", ProviderCode = "Fake", ProductCode = "Simulator", Environment = environment,
        Secrets = new() { ["privateKey"] = "clave-de-firma-del-simulador" }, HolderName = "Consorcio Sol", HolderTaxId = "80099999-1"
    };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsType<OkObjectResult>(result.Result).Value is T v ? v : throw new InvalidCastException();

    private static string Text(object? value) => JsonSerializer.Serialize(value);

    private async Task<PaymentConnectionDto> Assigned(PaymentConnectionRequest request, params Guid[] buildings)
    {
        var created = Ok(await Controller().Create(request, default));
        return Ok(await Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = buildings.ToList() }, default));
    }

    // ── Titular ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElTitularDelComercioSeGuardaYSoloLoVeQuienConfigura()
    {
        var created = await Assigned(TpagoRequest(), _env.Building.Id);

        Assert.Equal("Administradora Sol S.A.", created.HolderName);
        Assert.Equal("80012345-6", created.HolderTaxId);
        var row = await _env.T.NewContext().PaymentConnections.SingleAsync();
        Assert.Equal("80012345-6", row.HolderTaxId);
        Assert.Contains(created.Id.ToString(), Text(created));

        _env.Tenant.Role = "CompanyOperator";
        var listed = Assert.IsType<OkObjectResult>((await Controller().GetAll(null, default)).Result).Value as PaymentConnectionListDto;
        Assert.Null(listed!.Items.Single().HolderName);
    }

    [Fact]
    public async Task NoSeActivaUnaConexionSinTitularYSiConElCargado()
    {
        var created = Ok(await Controller().Create(TpagoRequest(holderName: null, holderTaxId: null), default));
        Ok(await Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [_env.Building.Id] }, default));

        var blocked = await Controller().Activate(created.Id, default);
        Assert.Contains("payments_holder_missing", Text(Assert.IsType<ConflictObjectResult>(blocked.Result).Value));

        var onlyName = TpagoRequest(holderName: "Administradora Sol S.A.", holderTaxId: null);
        Ok(await Controller().Update(created.Id, onlyName, default));
        Assert.IsType<ConflictObjectResult>((await Controller().Activate(created.Id, default)).Result);   // falta el RUC

        Ok(await Controller().Update(created.Id, TpagoRequest(), default));
        Assert.Equal("Active", Ok(await Controller().Activate(created.Id, default)).Status);
    }

    [Fact]
    public async Task ElCambioDeTitularQuedaEnElHistorialYUnaConexionActivaNoPuedeQuedarseSinEl()
    {
        var connection = await Assigned(TpagoRequest(), _env.Building.Id);
        Ok(await Controller().Activate(connection.Id, default));

        var changed = TpagoRequest(holderName: "Consorcio Edificio Sol");
        var updated = Ok(await Controller().Update(connection.Id, changed, default));
        Assert.Equal("Consorcio Edificio Sol", updated.HolderName);
        Assert.Equal("Active", updated.Status);   // el titular no es una credencial: no devuelve la conexion a validacion

        var history = await _env.T.NewContext().PaymentConnectionAudits.Where(x => x.ConnectionId == connection.Id).ToListAsync();
        Assert.Contains(history, h => h.Action == "Updated" && h.ChangesJson!.Contains("Consorcio Edificio Sol"));

        var cleared = await Controller().Update(connection.Id, TpagoRequest(holderName: "  "), default);
        Assert.IsType<BadRequestObjectResult>(cleared.Result);
        Assert.Equal("Consorcio Edificio Sol", (await _env.T.NewContext().PaymentConnections.SingleAsync()).HolderName);
    }

    [Fact]
    public async Task ElTitularTieneLongitudesMaximas()
    {
        var tooLong = await Controller().Create(TpagoRequest(holderName: new string('x', 201)), default);
        var badTax = await Controller().Create(TpagoRequest(holderTaxId: new string('9', 31)), default);

        Assert.IsType<BadRequestObjectResult>(tooLong.Result);
        Assert.IsType<BadRequestObjectResult>(badTax.Result);
        Assert.Empty(await _env.T.NewContext().PaymentConnections.ToListAsync());
    }

    // ── Simulador ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElSimuladorNoApareceNiSeCreaSiElServidorNoLoHabilito()
    {
        var catalog = Ok(Controller().GetCatalog());
        Assert.DoesNotContain(catalog.Providers, p => p.Code == "Fake");

        var denied = await Controller().Create(FakeRequest(), default);
        Assert.Contains("no está habilitado", Assert.IsType<BadRequestObjectResult>(denied.Result).Value!.ToString());

        _settings["Payments:EnableFakeProvider"] = "true";
        Assert.Contains(Ok(Controller().GetCatalog()).Providers, p => p.Code == "Fake");
        Assert.Equal("Sandbox", Ok(await Controller().Create(FakeRequest(), default)).Environment);
    }

    [Fact]
    public async Task ElSimuladorNuncaPuedeEstarEnProduccion()
    {
        _settings["Payments:EnableFakeProvider"] = "true";

        var production = await Controller().Create(FakeRequest("Production"), default);
        Assert.Contains("producción", Assert.IsType<BadRequestObjectResult>(production.Result).Value!.ToString());

        var fake = Ok(await Controller().Create(FakeRequest(), default));
        var request = FakeRequest("Production");
        var switched = await Controller().Update(fake.Id, request, default);
        Assert.IsType<BadRequestObjectResult>(switched.Result);
        Assert.Equal(PaymentEnvironment.Sandbox, (await _env.T.NewContext().PaymentConnections.SingleAsync()).Environment);
    }

    [Fact]
    public async Task UnSimuladorYaCreadoNoSeActivaSiElServidorLoApago()
    {
        _settings["Payments:EnableFakeProvider"] = "true";
        var fake = await Assigned(FakeRequest(), _env.Building.Id);

        _settings["Payments:EnableFakeProvider"] = "false";
        var blocked = await Controller().Activate(fake.Id, default);

        Assert.Contains("payments_fake_not_allowed", Text(Assert.IsType<ConflictObjectResult>(blocked.Result).Value));

        _settings["Payments:EnableFakeProvider"] = "true";
        Assert.Equal("Active", Ok(await Controller().Activate(fake.Id, default)).Status);
    }

    [Fact]
    public async Task ElInterruptorDelCobroSeReflejaEnLaLista()
    {
        Assert.False((Assert.IsType<OkObjectResult>((await Controller().GetAll(null, default)).Result).Value as PaymentConnectionListDto)!.CollectionEnabled);

        _settings["Payments:CollectionEnabled"] = "true";

        Assert.True((Assert.IsType<OkObjectResult>((await Controller().GetAll(null, default)).Result).Value as PaymentConnectionListDto)!.CollectionEnabled);
    }

    // ── Edificios de prueba y cuenta por liquidar ────────────────────────────

    [Fact]
    public async Task ElEdificioDePruebaSeMarcaAlAsignarYSeCambiaDespuesSinTocarLosDemas()
    {
        var created = Ok(await Controller().Create(TpagoRequest(), default));

        var assigned = Ok(await Controller().SetBuildings(created.Id,
            new PaymentConnectionBuildingsRequest { BuildingIds = [_env.Building.Id, _plain.Id], TestBuildingIds = [_env.Building.Id] }, default));
        Assert.True(assigned.Buildings.Single(b => b.BuildingId == _env.Building.Id).IsTest);
        Assert.False(assigned.Buildings.Single(b => b.BuildingId == _plain.Id).IsTest);

        // Sin TestBuildingIds no se cambia nada.
        var untouched = Ok(await Controller().SetBuildings(created.Id,
            new PaymentConnectionBuildingsRequest { BuildingIds = [_env.Building.Id, _plain.Id] }, default));
        Assert.True(untouched.Buildings.Single(b => b.BuildingId == _env.Building.Id).IsTest);

        var switched = Ok(await Controller().SetBuildings(created.Id,
            new PaymentConnectionBuildingsRequest { BuildingIds = [_env.Building.Id, _plain.Id], TestBuildingIds = [_plain.Id] }, default));
        Assert.False(switched.Buildings.Single(b => b.BuildingId == _env.Building.Id).IsTest);
        Assert.True(switched.Buildings.Single(b => b.BuildingId == _plain.Id).IsTest);

        var history = await _env.T.NewContext().PaymentConnectionAudits.Where(x => x.Action == "Assigned").ToListAsync();
        Assert.Contains(history, h => h.ChangesJson!.Contains("Edificio de prueba"));
    }

    [Fact]
    public async Task AlAsignarUnEdificioConFinanzasSeCreaSuCuentaDeCobrosOnlinePorLiquidar()
    {
        await Assigned(TpagoRequest(), _env.Building.Id, _plain.Id);

        var accounts = await _env.T.NewContext().FinancialAccounts.Where(x => x.Type == FinancialAccountType.Clearing).ToListAsync();
        var account = Assert.Single(accounts);   // solo el edificio con Finanzas; el otro no tiene libro
        Assert.Equal(_env.Building.Id, account.BuildingId);
        Assert.Equal(ClearingAccounts.DefaultName, account.Name);
        Assert.Equal(0m, account.OpeningBalance);
        Assert.True(account.IsActive);
    }

    [Fact]
    public async Task LaCuentaPorLiquidarNoSeDuplicaAunqueSeVuelvaAAsignarOHayaOtraConexion()
    {
        var first = await Assigned(TpagoRequest("Primera"), _env.Building.Id);
        var second = await Assigned(TpagoRequest("Segunda"), _env.Building.Id);   // otra conexion en el mismo edificio

        Ok(await Controller().SetBuildings(first.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [] }, default));
        Ok(await Controller().SetBuildings(first.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [_env.Building.Id] }, default));

        Assert.Single(await _env.T.NewContext().FinancialAccounts.Where(x => x.Type == FinancialAccountType.Clearing).ToListAsync());
        _ = second;
    }
}
