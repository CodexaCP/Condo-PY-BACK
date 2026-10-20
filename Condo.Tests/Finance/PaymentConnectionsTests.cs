using System.Text.Json;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Condo.Tests.Finance;

// Pagos online, configuracion de Bancard: cifrado de las claves del comercio, conexiones de la empresa, asignacion a edificios, estados,
// historial y permisos. Esta etapa no cobra nada: solo deja la conexion lista.
public class PaymentConnectionsTests
{
    private const string PrivateKey = "sk_live_0123456789abcdefWXYZ";   // 28 caracteres: termina en WXYZ

    // ─── Cifrado de secretos ─────────────────────────────────────────────────

    private static string NewKey() => AesGcmSecretProtector.GenerateMasterKey();

    [Fact]
    public void El_secreto_se_cifra_y_se_descifra_y_no_queda_en_claro()
    {
        var protector = new AesGcmSecretProtector(NewKey(), "k1");
        var cipher = protector.Protect(new Dictionary<string, string> { ["privateKey"] = PrivateKey }, "conexion-1");

        Assert.DoesNotContain("0123456789", cipher);
        Assert.DoesNotContain("privateKey", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipher)));
        Assert.Equal(PrivateKey, protector.Unprotect(cipher, "k1", "conexion-1")["privateKey"]);
    }

    [Fact]
    public void Cada_cifrado_usa_su_propio_nonce()
    {
        var protector = new AesGcmSecretProtector(NewKey(), "k1");
        var secrets = new Dictionary<string, string> { ["privateKey"] = PrivateKey };

        Assert.NotEqual(protector.Protect(secrets, "a"), protector.Protect(secrets, "a"));
    }

    [Fact]
    public void Un_secreto_copiado_a_otra_conexion_o_alterado_no_se_puede_descifrar()
    {
        var protector = new AesGcmSecretProtector(NewKey(), "k1");
        var cipher = protector.Protect(new Dictionary<string, string> { ["privateKey"] = PrivateKey }, "conexion-1");

        Assert.Throws<SecretProtectorException>(() => protector.Unprotect(cipher, "k1", "conexion-2"));

        var bytes = Convert.FromBase64String(cipher);
        bytes[^1] ^= 0xFF;
        Assert.Throws<SecretProtectorException>(() => protector.Unprotect(Convert.ToBase64String(bytes), "k1", "conexion-1"));
        Assert.Throws<SecretProtectorException>(() => protector.Unprotect("no-es-base64!", "k1", "conexion-1"));
    }

    [Fact]
    public void Sin_clave_maestra_valida_el_servicio_queda_no_configurado_y_no_cifra()
    {
        foreach (var key in new[] { null, "", "   ", "no-es-base64", Convert.ToBase64String(new byte[16]) })
        {
            var protector = new AesGcmSecretProtector(key, "k1");
            Assert.False(protector.IsConfigured);
            Assert.Throws<SecretProtectorException>(() => protector.Protect(new Dictionary<string, string> { ["a"] = "b" }, "x"));
        }
    }

    [Fact]
    public void La_clave_maestra_se_puede_rotar_conservando_las_anteriores_para_descifrar()
    {
        var oldKey = NewKey();
        var cipher = new AesGcmSecretProtector(oldKey, "k1").Protect(new Dictionary<string, string> { ["privateKey"] = PrivateKey }, "c");

        var rotated = new AesGcmSecretProtector(NewKey(), "k2", new Dictionary<string, string> { ["k1"] = oldKey });

        Assert.Equal("k2", rotated.CurrentKeyId);
        Assert.Equal(PrivateKey, rotated.Unprotect(cipher, "k1", "c")["privateKey"]);
        Assert.Throws<SecretProtectorException>(() => new AesGcmSecretProtector(NewKey(), "k2").Unprotect(cipher, "k1", "c"));
    }

    // ─── Conexiones ──────────────────────────────────────────────────────────

    private sealed class Scenario : IDisposable
    {
        public TestDb T { get; } = new();
        public Company Company { get; }
        public Building BuildingA { get; }
        public Building BuildingB { get; }
        public ISecretProtector Protector { get; }
        private readonly string _role;
        private readonly string? _masterKey;
        private readonly FakeTenantContext _tenant;
        private readonly FakeAccessScope _scope;

        public Scenario(string role = "CompanyAdmin", bool masterKey = true)
        {
            _role = role;
            Company = T.AddCompany();
            BuildingA = T.AddBuilding(Company, "Edificio A");
            BuildingB = T.AddBuilding(Company, "Edificio B");
            _masterKey = masterKey ? NewKey() : null;
            Protector = new AesGcmSecretProtector(_masterKey, "k1");
            _tenant = new FakeTenantContext { Role = role, CompanyId = role == "SuperAdmin" ? null : Company.Id, Email = "admin@test.local" };
            _scope = new FakeAccessScope(_tenant);
            _scope.Buildings.Add(BuildingA.Id);
        }

        public FakeAccessScope Scope => _scope;

        public PaymentConnectionsController Controller(ISecretProtector? protector = null, FakeTenantContext? tenant = null, FakeAccessScope? scope = null)
        {
            var t = tenant ?? _tenant;
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Payments:CallbackBaseUrl"] = "https://api.expenses.com.py" }).Build();
            var controller = new PaymentConnectionsController(
                T.Db, t, scope ?? _scope, protector ?? Protector, config, NullLogger<PaymentConnectionsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            return controller;
        }

        public void Dispose() => T.Dispose();
    }

    private static PaymentConnectionRequest TpagoRequest(Scenario s, string name = "Bancard Sol", string? privateKey = PrivateKey,
        string commerce = "COM-001", string environment = "Sandbox") => new()
    {
        CompanyId = s.Company.Id,
        Name = name,
        ProviderCode = "Bancard",
        ProductCode = "Tpago",
        Environment = environment,
        Settings = new() { ["commerceCode"] = commerce, ["branchCode"] = "1", ["publicKey"] = "pk_public_ABCDEF" },
        Secrets = privateKey is null ? [] : new() { ["privateKey"] = privateKey },
        HolderName = "Administradora Sol S.A.",
        HolderTaxId = "80012345-6"
    };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsType<OkObjectResult>(result.Result).Value is T v ? v : throw new InvalidCastException();

    [Fact]
    public async Task El_catalogo_de_Bancard_declara_los_campos_y_cual_es_secreto()
    {
        using var s = new Scenario();

        var catalog = Ok(s.Controller().GetCatalog());

        var tpago = catalog.Providers.Single(p => p.Code == "Bancard").Products.Single(p => p.Code == "Tpago");
        Assert.True(tpago.Fields.Single(f => f.Key == "privateKey").Secret);
        Assert.False(tpago.Fields.Single(f => f.Key == "publicKey").Secret);
        Assert.Contains(tpago.Fields, f => f.Key == "commerceCode" && f.Required);
        Assert.False(tpago.SignedCallback);
        Assert.Contains("VPos", catalog.Providers.Single().Products.Select(p => p.Code));
        Assert.True(catalog.MasterKeyConfigured);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Al_crear_la_clave_privada_se_guarda_cifrada_y_nunca_se_devuelve()
    {
        using var s = new Scenario();

        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));

        Assert.Equal("PendingValidation", created.Status);
        Assert.Equal("••••WXYZ", created.SecretsMasked!["privateKey"]);
        Assert.Equal("COM-001", created.Settings!["commerceCode"]);
        Assert.Empty(created.MissingFields);
        Assert.StartsWith("https://api.expenses.com.py/api/payment-webhooks/", created.CallbackUrl);

        // La clave no sale en ninguna respuesta ni queda en claro en la base.
        Assert.DoesNotContain("0123456789abcdef", JsonSerializer.Serialize(created));
        var row = await s.T.Db.PaymentConnections.AsNoTracking().SingleAsync();
        Assert.NotNull(row.SecretsCipher);
        Assert.DoesNotContain("0123456789abcdef", row.SecretsCipher);
        Assert.DoesNotContain("0123456789abcdef", row.SecretsFingerprint);
        Assert.Equal("k1", row.KeyId);
        Assert.Equal(PrivateKey, s.Protector.Unprotect(row.SecretsCipher!, row.KeyId!, row.PublicId)["privateKey"]);

        // El historial dice que se cargo la clave, nunca cual es.
        var audits = await s.T.Db.PaymentConnectionAudits.AsNoTracking().ToListAsync();
        Assert.Single(audits);
        Assert.DoesNotContain("0123456789abcdef", JsonSerializer.Serialize(audits));
        Assert.Contains("WXYZ", audits[0].ChangesJson);   // solo el final enmascarado
    }

    [Fact]
    public async Task Sin_clave_maestra_en_el_servidor_no_guarda_claves_privadas()
    {
        using var s = new Scenario(masterKey: false);

        var result = await s.Controller().Create(TpagoRequest(s), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("clave maestra", JsonSerializer.Serialize(conflict.Value));
        Assert.Equal(0, await s.T.Db.PaymentConnections.CountAsync());

        // Los datos no secretos si se pueden guardar: queda incompleta hasta cargar la clave.
        var draft = Ok(await s.Controller().Create(TpagoRequest(s, privateKey: null), CancellationToken.None));
        Assert.Equal("Draft", draft.Status);
        Assert.Contains("Clave privada", draft.MissingFields);
    }

    [Fact]
    public async Task Una_conexion_incompleta_queda_en_borrador_y_dice_que_falta()
    {
        using var s = new Scenario();
        var request = TpagoRequest(s, privateKey: null);
        request.Settings.Remove("branchCode");

        var created = Ok(await s.Controller().Create(request, CancellationToken.None));

        Assert.Equal("Draft", created.Status);
        Assert.Contains("Clave privada", created.MissingFields);
        Assert.Contains(created.MissingFields, f => f.Contains("sucursal"));
    }

    [Fact]
    public async Task Valida_el_nombre_el_ambiente_los_campos_y_el_catalogo()
    {
        using var s = new Scenario();
        var c = s.Controller();

        var noName = TpagoRequest(s, name: "  ");
        var badEnv = TpagoRequest(s, environment: "Staging");
        var unknownProduct = TpagoRequest(s); unknownProduct.ProductCode = "Inexistente";
        var alienField = TpagoRequest(s); alienField.Settings["otro"] = "x";
        var secretAsSetting = TpagoRequest(s); secretAsSetting.Settings["privateKey"] = "x";
        var spaces = TpagoRequest(s, privateKey: "clave con espacios");
        var tooLong = TpagoRequest(s, commerce: new string('x', 41));

        foreach (var request in new[] { noName, badEnv, unknownProduct, alienField, secretAsSetting, spaces, tooLong })
            Assert.IsType<BadRequestObjectResult>((await c.Create(request, CancellationToken.None)).Result);
        Assert.Equal(0, await s.T.Db.PaymentConnections.CountAsync());
    }

    [Fact]
    public async Task El_nombre_no_se_repite_en_la_empresa()
    {
        using var s = new Scenario();
        Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));

        var again = await s.Controller().Create(TpagoRequest(s), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(again.Result);
    }

    [Fact]
    public async Task Editar_con_la_clave_vacia_la_conserva_y_con_valor_la_reemplaza_dejandolo_en_el_historial()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));

        // Cambiar un dato sin tocar la clave.
        var edit = TpagoRequest(s, privateKey: "", commerce: "COM-002");
        var kept = Ok(await s.Controller().Update(created.Id, edit, CancellationToken.None));
        Assert.Equal("COM-002", kept.Settings!["commerceCode"]);
        Assert.Equal("••••WXYZ", kept.SecretsMasked!["privateKey"]);
        var row = await s.T.Db.PaymentConnections.AsNoTracking().SingleAsync();
        Assert.Equal(PrivateKey, s.Protector.Unprotect(row.SecretsCipher!, row.KeyId!, row.PublicId)["privateKey"]);

        // Reemplazar la clave.
        var replaced = Ok(await s.Controller().Update(created.Id, TpagoRequest(s, privateKey: "sk_live_NUEVA_clave_0000ZZZZ", commerce: "COM-002"), CancellationToken.None));
        Assert.Equal("••••ZZZZ", replaced.SecretsMasked!["privateKey"]);
        row = await s.T.Db.PaymentConnections.AsNoTracking().SingleAsync();
        Assert.Equal("sk_live_NUEVA_clave_0000ZZZZ", s.Protector.Unprotect(row.SecretsCipher!, row.KeyId!, row.PublicId)["privateKey"]);

        var actions = (await s.T.Db.PaymentConnectionAudits.AsNoTracking().OrderBy(a => a.CreatedAtUtc).ToListAsync()).Select(a => a.Action).ToList();
        Assert.Equal(["Created", "Updated", "SecretsReplaced"], actions);
        Assert.DoesNotContain("NUEVA_clave", JsonSerializer.Serialize(await s.T.Db.PaymentConnectionAudits.AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task El_proveedor_y_la_modalidad_no_se_cambian_al_editar()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));

        var other = TpagoRequest(s); other.ProductCode = "VPos";

        Assert.IsType<BadRequestObjectResult>((await s.Controller().Update(created.Id, other, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Los_edificios_deben_ser_de_la_misma_empresa_y_se_pueden_asignar_y_quitar()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));
        var foreignBuilding = s.T.AddBuilding(s.T.AddCompany(), "Ajeno");

        var foreign = await s.Controller().SetBuildings(created.Id,
            new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id, foreignBuilding.Id] }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(foreign.Result);
        Assert.Equal(0, await s.T.Db.BuildingPaymentConfigs.CountAsync());

        var both = Ok(await s.Controller().SetBuildings(created.Id,
            new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id, s.BuildingB.Id, s.BuildingA.Id] }, CancellationToken.None));
        Assert.Equal(["Edificio A", "Edificio B"], both.Buildings.Select(b => b.BuildingName));

        var onlyB = Ok(await s.Controller().SetBuildings(created.Id,
            new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingB.Id] }, CancellationToken.None));
        Assert.Equal(["Edificio B"], onlyB.Buildings.Select(b => b.BuildingName));
        Assert.Equal(1, await s.T.Db.BuildingPaymentConfigs.CountAsync(x => !x.IsDeleted));
    }

    [Fact]
    public async Task Activar_exige_datos_completos_y_al_menos_un_edificio()
    {
        using var s = new Scenario();
        var incomplete = Ok(await s.Controller().Create(TpagoRequest(s, "Incompleta", privateKey: null), CancellationToken.None));
        var complete = Ok(await s.Controller().Create(TpagoRequest(s, "Completa"), CancellationToken.None));

        Assert.IsType<ConflictObjectResult>((await s.Controller().Activate(incomplete.Id, CancellationToken.None)).Result);
        Assert.IsType<ConflictObjectResult>((await s.Controller().Activate(complete.Id, CancellationToken.None)).Result);   // sin edificios

        Ok(await s.Controller().SetBuildings(complete.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id] }, CancellationToken.None));
        var active = Ok(await s.Controller().Activate(complete.Id, CancellationToken.None));

        Assert.Equal("Active", active.Status);
        Assert.NotNull(active.StatusChangedAtUtc);
    }

    [Fact]
    public async Task No_activa_si_las_claves_guardadas_no_se_pueden_leer_con_la_clave_maestra_actual()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));
        Ok(await s.Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id] }, CancellationToken.None));

        var otherKey = new AesGcmSecretProtector(NewKey(), "k1");
        var result = await s.Controller(protector: otherKey).Activate(created.Id, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("clave maestra", JsonSerializer.Serialize(conflict.Value));
        Assert.Equal(PaymentConnectionStatus.PendingValidation, (await s.T.Db.PaymentConnections.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Cambiar_credenciales_o_ambiente_de_una_conexion_activa_la_devuelve_a_validacion()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));
        Ok(await s.Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id] }, CancellationToken.None));
        Ok(await s.Controller().Activate(created.Id, CancellationToken.None));

        // Renombrar no cambia credenciales: sigue activa.
        var renamed = Ok(await s.Controller().Update(created.Id, TpagoRequest(s, name: "Otro nombre"), CancellationToken.None));
        Assert.Equal("Active", renamed.Status);

        var production = Ok(await s.Controller().Update(created.Id, TpagoRequest(s, name: "Otro nombre", environment: "Production"), CancellationToken.None));
        Assert.Equal("PendingValidation", production.Status);
        Assert.Equal("Production", production.Environment);
    }

    [Fact]
    public async Task Una_conexion_activa_que_se_queda_sin_edificios_vuelve_a_validacion()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));
        Ok(await s.Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id] }, CancellationToken.None));
        Ok(await s.Controller().Activate(created.Id, CancellationToken.None));

        var empty = Ok(await s.Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [] }, CancellationToken.None));

        Assert.Equal("PendingValidation", empty.Status);
    }

    [Fact]
    public async Task Desactivar_conserva_la_configuracion_y_solo_una_inactiva_se_elimina_liberando_el_nombre()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));
        Ok(await s.Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id] }, CancellationToken.None));
        Ok(await s.Controller().Activate(created.Id, CancellationToken.None));

        Assert.IsType<ConflictObjectResult>(await s.Controller().Delete(created.Id, CancellationToken.None));

        var disabled = Ok(await s.Controller().Disable(created.Id, CancellationToken.None));
        Assert.Equal("Disabled", disabled.Status);
        Assert.Equal("••••WXYZ", disabled.SecretsMasked!["privateKey"]);
        Assert.Single(disabled.Buildings);

        Assert.IsType<NoContentResult>(await s.Controller().Delete(created.Id, CancellationToken.None));
        Assert.Equal(0, await s.T.Db.PaymentConnections.CountAsync(x => !x.IsDeleted));
        Assert.Equal(0, await s.T.Db.BuildingPaymentConfigs.CountAsync(x => !x.IsDeleted));
        Assert.IsType<OkObjectResult>((await s.Controller().Create(TpagoRequest(s), CancellationToken.None)).Result);   // el nombre quedo libre
    }

    [Fact]
    public async Task El_historial_registra_cada_cambio_con_quien_lo_hizo()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));
        Ok(await s.Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [s.BuildingA.Id] }, CancellationToken.None));
        Ok(await s.Controller().Activate(created.Id, CancellationToken.None));
        Ok(await s.Controller().Disable(created.Id, CancellationToken.None));

        var history = Assert.IsAssignableFrom<IReadOnlyList<PaymentConnectionAuditDto>>(
            Assert.IsType<OkObjectResult>((await s.Controller().GetHistory(created.Id, CancellationToken.None)).Result).Value);

        Assert.Equal(new[] { "Disabled", "Activated", "Assigned", "Created" }, history.Select(h => h.Action));
        Assert.All(history, h => Assert.Equal("admin@test.local", h.UserEmail));
    }

    // ─── Permisos y alcance ──────────────────────────────────────────────────

    [Fact]
    public async Task El_operador_y_el_encargado_solo_ven_el_estado_y_no_pueden_configurar()
    {
        using var admin = new Scenario();
        var created = Ok(await admin.Controller().Create(TpagoRequest(admin), CancellationToken.None));
        Ok(await admin.Controller().SetBuildings(created.Id,
            new PaymentConnectionBuildingsRequest { BuildingIds = [admin.BuildingA.Id, admin.BuildingB.Id] }, CancellationToken.None));

        var managerTenant = new FakeTenantContext { Role = "BuildingManager", CompanyId = admin.Company.Id };
        var managerScope = new FakeAccessScope(managerTenant);
        managerScope.Buildings.Add(admin.BuildingA.Id);   // el encargado solo tiene el edificio A
        var manager = admin.Controller(tenant: managerTenant, scope: managerScope);

        var list = Ok(await manager.GetAll(null, CancellationToken.None));
        Assert.False(list.CanEdit);
        var item = Assert.Single(list.Items);
        Assert.Equal(["Edificio A"], item.Buildings.Select(b => b.BuildingName));   // solo su edificio
        Assert.Null(item.Settings);
        Assert.Null(item.SecretsMasked);
        Assert.Null(item.CallbackUrl);
        Assert.DoesNotContain("COM-001", JsonSerializer.Serialize(list));

        Assert.Equal(403, ((ObjectResult)(await manager.Create(TpagoRequest(admin, "Otra"), CancellationToken.None)).Result!).StatusCode);
        Assert.Equal(403, ((ObjectResult)(await manager.Activate(created.Id, CancellationToken.None)).Result!).StatusCode);
        Assert.Equal(403, ((ObjectResult)await manager.Delete(created.Id, CancellationToken.None)).StatusCode);
        Assert.Equal(403, ((ObjectResult)(await manager.GetHistory(created.Id, CancellationToken.None)).Result!).StatusCode);
    }

    [Fact]
    public async Task Un_encargado_sin_edificios_asignados_a_ninguna_conexion_no_ve_ninguna()
    {
        using var admin = new Scenario();
        var created = Ok(await admin.Controller().Create(TpagoRequest(admin), CancellationToken.None));
        Ok(await admin.Controller().SetBuildings(created.Id, new PaymentConnectionBuildingsRequest { BuildingIds = [admin.BuildingB.Id] }, CancellationToken.None));

        var managerTenant = new FakeTenantContext { Role = "BuildingManager", CompanyId = admin.Company.Id };
        var managerScope = new FakeAccessScope(managerTenant);
        managerScope.Buildings.Add(admin.BuildingA.Id);

        var list = Ok(await admin.Controller(tenant: managerTenant, scope: managerScope).GetAll(null, CancellationToken.None));

        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task Una_conexion_de_otra_empresa_no_se_ve_ni_se_toca()
    {
        using var s = new Scenario();
        var created = Ok(await s.Controller().Create(TpagoRequest(s), CancellationToken.None));

        var otherCompany = s.T.AddCompany();
        var intruder = new FakeTenantContext { Role = "CompanyAdmin", CompanyId = otherCompany.Id };
        var controller = s.Controller(tenant: intruder, scope: new FakeAccessScope(intruder));

        Assert.Empty(Ok(await controller.GetAll(null, CancellationToken.None)).Items);
        Assert.IsType<NotFoundResult>((await controller.Update(created.Id, TpagoRequest(s), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.Activate(created.Id, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await controller.Delete(created.Id, CancellationToken.None));
        Assert.IsType<NotFoundResult>((await controller.SetBuildings(created.Id, new PaymentConnectionBuildingsRequest(), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task El_super_admin_elige_la_empresa()
    {
        using var s = new Scenario("SuperAdmin");
        var c = s.Controller();

        Assert.IsType<BadRequestObjectResult>((await c.GetAll(null, CancellationToken.None)).Result);

        var request = TpagoRequest(s);
        request.CompanyId = null;
        Assert.IsType<BadRequestObjectResult>((await c.Create(request, CancellationToken.None)).Result);

        var created = Ok(await c.Create(TpagoRequest(s), CancellationToken.None));
        Assert.Equal(s.Company.Id, created.CompanyId);
        Assert.Single(Ok(await c.GetAll(s.Company.Id, CancellationToken.None)).Items);
    }
}
