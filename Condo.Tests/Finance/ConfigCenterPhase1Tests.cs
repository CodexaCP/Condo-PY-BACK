using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>Centro de configuracion, fase 1: resumen por seccion (estados y roles), historial de cambios y su enganche en las pantallas que ya configuran.</summary>
public class ConfigCenterPhase1Tests : IDisposable
{
    private readonly FinanceEnv _env = new();

    public void Dispose() => _env.Dispose();

    private BuildingConfigController Api() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new BuildingConfigOverviewService(_env.T.Db, new FinanceModuleGate(_env.T.Db)));

    private FinanceAccountsController Accounts() => new(_env.T.Db, _env.Access, _env.Tenant, new FinanceModuleGate(_env.T.Db));

    private FinanceController FinanceApi() => new(_env.T.Db, _env.Access, _env.Tenant, new FinanceModuleGate(_env.T.Db));

    private async Task<BuildingConfigOverviewDto> Overview() => FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default));

    private async Task<ConfigAuditPageDto> Audit(string? section = null, int page = 1, int pageSize = 25) =>
        FinanceEnv.Ok(await Api().GetAudit(_env.Building.Id, section, null, null, null, page, pageSize, default));

    private static ConfigSectionDto Section(BuildingConfigOverviewDto o, string key) => o.Sections.Single(s => s.Key == key);

    /// <summary>Deja el edificio con todo lo obligatorio cargado (ficha fiscal, cobro, timbrado y Finanzas completa).</summary>
    private void ConfigureEverything()
    {
        var b = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        b.Ruc = "80012345-6";
        b.LegalName = "Consorcio Prueba";
        b.VatRegime = VatRegime.General;
        b.DefaultDueDay = 10;
        b.PaymentInstructions = "Transferir al banco X";
        b.LateFeePolicyConfirmed = true;   // decision explicita: sin mora
        b.FundPolicyConfirmed = true;      // decision explicita: sin aportes a fondos
        _env.T.Db.InvoiceSeries.Add(new InvoiceSeries
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Ruc = "80012345-6", RazonSocial = "Consorcio Prueba",
            Establecimiento = "001", PuntoExpedicion = "001", NumeroTimbrado = "12345678", RangoDesde = 1, RangoHasta = 100,
            VigenciaDesde = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30), VigenciaHasta = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300), Activo = true
        });
        _env.T.Db.SaveChanges();
        _env.Setup();
        _env.SeedTemplate();
        // Regimen general: el IVA de las compras es obligatorio, asi que se clasifican todas las cuentas de egresos.
        foreach (var category in _env.T.Db.LedgerCategories.Where(x => x.BuildingId == _env.Building.Id && x.Type == LedgerCategoryType.Expense))
            category.VatTreatment = VatTreatment.Vat10;
        _env.T.Db.SaveChanges();
    }

    // ── Resumen: estados ─────────────────────────────────────────────────────

    [Fact]
    public async Task EdificioSinConfigurar_MuestraLoQueFaltaConSuMotivo()
    {
        var o = await Overview();

        var identity = Section(o, ConfigSectionKeys.Identity);
        Assert.Equal(ConfigSectionStatus.Incomplete, identity.Status);
        Assert.Contains(identity.Reasons, r => r.Contains("RUC"));
        Assert.Contains(identity.Reasons, r => r.Contains("razón social"));
        Assert.Contains(identity.Reasons, r => r.Contains("régimen de IVA"));
        Assert.Contains(identity.Reasons, r => r.Contains("timbrado"));

        var collection = Section(o, ConfigSectionKeys.Collection);
        Assert.Equal(ConfigSectionStatus.Incomplete, collection.Status);
        Assert.Equal(2, collection.Reasons.Count);

        Assert.Equal(ConfigSectionStatus.Complete, Section(o, ConfigSectionKeys.PaymentRule).Status);
        Assert.Equal(ConfigSectionStatus.Incomplete, Section(o, ConfigSectionKeys.LateFee).Status);   // fase 3: mora y fondos son obligatorios
        Assert.Equal(ConfigSectionStatus.Incomplete, Section(o, ConfigSectionKeys.Funds).Status);
        Assert.Equal(ConfigSectionStatus.Incomplete, Section(o, ConfigSectionKeys.Chart).Status);
        Assert.Equal(ConfigSectionStatus.Optional, Section(o, ConfigSectionKeys.Budget).Status);

        // Obligatorias: identidad, cobro, mora, regla de pago, fondos y plan de cuentas (Finanzas esta disponible); solo la regla de pago esta completa.
        Assert.Equal(6, o.RequiredCount);
        Assert.Equal(1, o.ReadyCount);
        Assert.False(o.ReadyToOperate);
        Assert.True(o.FinanceAvailable);
    }

    [Fact]
    public async Task EdificioConTodoCargado_QuedaListoParaOperar()
    {
        ConfigureEverything();

        var o = await Overview();

        Assert.Equal(7, o.RequiredCount);
        Assert.Equal(7, o.ReadyCount);
        Assert.True(o.ReadyToOperate);
        Assert.All(o.Sections.Where(s => s.Required), s => Assert.Equal(ConfigSectionStatus.Complete, s.Status));
    }

    [Fact]
    public async Task UnTimbradoVencidoOInactivo_NoCuentaComoVigente()
    {
        ConfigureEverything();
        var series = _env.T.Db.InvoiceSeries.Single(x => x.BuildingId == _env.Building.Id);
        series.VigenciaHasta = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        _env.T.Db.SaveChanges();
        Assert.Equal(ConfigSectionStatus.Incomplete, Section(await Overview(), ConfigSectionKeys.Identity).Status);

        series.VigenciaHasta = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        series.Activo = false;
        _env.T.Db.SaveChanges();
        Assert.Equal(ConfigSectionStatus.Incomplete, Section(await Overview(), ConfigSectionKeys.Identity).Status);
    }

    [Fact]
    public async Task CobroSeCompletaConCuentaBancariaOConInstrucciones()
    {
        var b = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        b.DefaultDueDay = 10;
        _env.T.Db.SaveChanges();
        Assert.Equal(ConfigSectionStatus.Incomplete, Section(await Overview(), ConfigSectionKeys.Collection).Status);

        _env.T.Db.BuildingBankAccounts.Add(new BuildingBankAccount
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, BankName = "Banco X", AccountNumber = "123", HolderName = "Consorcio"
        });
        _env.T.Db.SaveChanges();
        Assert.Equal(ConfigSectionStatus.Complete, Section(await Overview(), ConfigSectionKeys.Collection).Status);
    }

    [Fact]
    public async Task MoraYFondos_ConfiguradosQuedanCompletos()
    {
        var b = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        b.LateFeeRatePercentage = 1.5m;
        b.LateFeeFrequency = LateFeeFrequency.Weekly;
        b.ReserveFundPercentage = 5m;
        _env.T.Db.SaveChanges();

        var o = await Overview();

        Assert.Equal(ConfigSectionStatus.Complete, Section(o, ConfigSectionKeys.LateFee).Status);
        Assert.Equal(ConfigSectionStatus.Complete, Section(o, ConfigSectionKeys.Funds).Status);
        Assert.True(Section(o, ConfigSectionKeys.LateFee).Required);
        Assert.Equal(6, o.RequiredCount);
        Assert.Equal(3, o.ReadyCount);   // regla de pago, mora y fondos
    }

    [Fact]
    public async Task SinFinanzasHabilitadoElPlanDeCuentasNoEstaDisponibleNiCuenta()
    {
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();

        var o = await Overview();

        Assert.Equal(ConfigSectionStatus.NotAvailable, Section(o, ConfigSectionKeys.Chart).Status);
        Assert.Equal(ConfigSectionStatus.NotAvailable, Section(o, ConfigSectionKeys.Budget).Status);
        Assert.False(o.FinanceAvailable);
        Assert.Equal(5, o.RequiredCount);
    }

    [Fact]
    public async Task SinPlanQueIncluyaFinanzasElMotivoEsElPlan()
    {
        _env.T.Db.Plans.Single(x => x.Name == "Plan con finanzas").IncludesFinanceModule = false;
        _env.T.Db.SaveChanges();

        var chart = Section(await Overview(), ConfigSectionKeys.Chart);

        Assert.Equal(ConfigSectionStatus.NotAvailable, chart.Status);
        Assert.Contains("plan", chart.Reasons.Single());
    }

    [Fact]
    public async Task ElPresupuestoCargadoMarcaLaSeccionComoCompleta()
    {
        ConfigureEverything();
        var category = _env.AllCats().First(c => c.ParentId != null && c.Type == LedgerCategoryType.Expense);
        _env.AddBudget(category.Id);

        var budget = Section(await Overview(), ConfigSectionKeys.Budget);

        Assert.Equal(ConfigSectionStatus.Complete, budget.Status);
        Assert.Equal("finance", budget.LinkKind);
    }

    // ── Resumen: roles y acceso ──────────────────────────────────────────────

    [Fact]
    public async Task CadaRolVeSoloSusSecciones()
    {
        _env.Access.Buildings.Add(_env.Building.Id);

        _env.LoginAs("CompanyAdmin");
        Assert.Equal(13, (await Overview()).Sections.Count);

        _env.LoginAs("CompanyOperator");
        Assert.Equal(13, (await Overview()).Sections.Count);

        _env.LoginAs("BuildingManager");
        var manager = await Overview();
        Assert.Equal(
            [ConfigSectionKeys.LateFee, ConfigSectionKeys.Chart, ConfigSectionKeys.Taxes, ConfigSectionKeys.Suppliers, ConfigSectionKeys.Closing, ConfigSectionKeys.Budget, ConfigSectionKeys.Reconciliation, ConfigSectionKeys.Accounting],
            manager.Sections.Select(s => s.Key).ToArray());
    }

    [Fact]
    public async Task CanEditSigueLaTablaDePermisos()
    {
        _env.Access.Buildings.Add(_env.Building.Id);

        _env.LoginAs("SuperAdmin");
        var superAdmin = await Overview();
        Assert.True(Section(superAdmin, ConfigSectionKeys.Chart).CanEdit);
        Assert.True(Section(superAdmin, ConfigSectionKeys.Identity).CanEdit);
        Assert.False(Section(superAdmin, ConfigSectionKeys.PaymentRule).CanEdit);   // regla fija: nadie la edita

        _env.LoginAs("CompanyAdmin");
        var admin = await Overview();
        Assert.False(Section(admin, ConfigSectionKeys.Chart).CanEdit);              // el plan de cuentas es servicio del SuperAdmin
        Assert.True(Section(admin, ConfigSectionKeys.LateFee).CanEdit);
        Assert.True(Section(admin, ConfigSectionKeys.Budget).CanEdit);

        _env.LoginAs("CompanyOperator");
        var op = await Overview();
        // El operador solo edita proveedores (decision de Finanzas: quienes cargan gastos cargan proveedores).
        Assert.All(op.Sections.Where(s => s.Key != ConfigSectionKeys.Suppliers), s => Assert.False(s.CanEdit));
        Assert.True(Section(op, ConfigSectionKeys.Suppliers).CanEdit);
    }

    [Fact]
    public async Task UnRolQueNoEsAdministrativoRecibe403()
    {
        _env.LoginAs("Owner");

        var result = await Api().GetOverview(_env.Building.Id, default);

        Assert.Equal(403, (result.Result as ObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task UnEdificioAjenoOInexistenteResponde404()
    {
        _env.LoginAs("CompanyAdmin");   // sin edificios asignados

        Assert.IsType<NotFoundResult>((await Api().GetOverview(_env.Building.Id, default)).Result);

        _env.LoginAs("SuperAdmin");
        Assert.IsType<NotFoundResult>((await Api().GetOverview(Guid.NewGuid(), default)).Result);
    }

    // ── Historial: enganche en las pantallas que ya configuran ────────────────

    [Fact]
    public async Task CrearEditarYEliminarUnaCuentaQuedaEnElHistorial()
    {
        var created = FinanceEnv.Ok(await Accounts().Create(
            new FinancialAccountUpsertRequest { BuildingId = _env.Building.Id, Name = "Banco Uno", Type = FinancialAccountType.Bank, OpeningBalance = 1_000_000m, IsActive = true }, default));

        FinanceEnv.Ok(await Accounts().Update(created.Id,
            new FinancialAccountUpsertRequest { BuildingId = _env.Building.Id, Name = "Banco Dos", Type = FinancialAccountType.Bank, OpeningBalance = 2_000_000m, IsActive = true }, default));

        await Accounts().Delete(created.Id, default);

        var page = await Audit(ConfigSectionKeys.Chart);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["Deleted", "Updated", "Created"], page.Items.Select(x => x.Action).ToArray());

        var update = page.Items.Single(x => x.Action == "Updated");
        Assert.Equal(["name", "openingBalance"], update.Changes.Select(c => c.Field).ToArray());
        Assert.Equal("Banco Uno", update.Changes[0].Before);
        Assert.Equal("Banco Dos", update.Changes[0].After);
        Assert.All(page.Items, x => Assert.Equal(created.Id, x.EntityId));
        Assert.All(page.Items, x => Assert.Equal(_env.Tenant.Email, x.UserEmail));
    }

    [Fact]
    public async Task UnaEdicionSinCambiosNoDejaHistorial()
    {
        var created = FinanceEnv.Ok(await Accounts().Create(
            new FinancialAccountUpsertRequest { BuildingId = _env.Building.Id, Name = "Banco Uno", Type = FinancialAccountType.Bank, OpeningBalance = 5m, IsActive = true }, default));

        FinanceEnv.Ok(await Accounts().Update(created.Id,
            new FinancialAccountUpsertRequest { BuildingId = _env.Building.Id, Name = "Banco Uno", Type = FinancialAccountType.Bank, OpeningBalance = 5m, IsActive = true }, default));

        Assert.Equal(1, (await Audit()).TotalCount);   // solo el alta
    }

    [Fact]
    public async Task LaFechaDeArranqueQuedaEnElHistorialConSuAntesYDespues()
    {
        var api = FinanceApi();
        FinanceEnv.Ok(await api.UpdateSettings(_env.Building.Id,
            new FinanceSettingsUpdateRequest { FinanceStartDate = new DateOnly(2026, 1, 1), FiscalYearStartMonth = 1 }, default));
        FinanceEnv.Ok(await api.UpdateSettings(_env.Building.Id,
            new FinanceSettingsUpdateRequest { FinanceStartDate = new DateOnly(2026, 3, 1), FiscalYearStartMonth = 4 }, default));

        var page = await Audit(ConfigSectionKeys.Chart);

        Assert.Equal(2, page.TotalCount);
        var second = page.Items[0];
        Assert.Equal(["financeStartDate", "fiscalYearStartMonth"], second.Changes.Select(c => c.Field).ToArray());
        Assert.Equal("01/01/2026", second.Changes[0].Before);
        Assert.Equal("01/03/2026", second.Changes[0].After);
        Assert.Equal("1", second.Changes[1].Before);
        Assert.Equal("4", second.Changes[1].After);
    }

    [Fact]
    public async Task ElPlanGenericoQuedaRegistradoComoUnaSolaEntrada()
    {
        _env.Setup();
        var api = _env.Controller();

        var result = await api.ApplyTemplate(
            new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace, ConfirmReplace = true }, default);
        Assert.NotNull(FinanceEnv.Ok(result));

        var page = await Audit(ConfigSectionKeys.Chart);
        var entry = Assert.Single(page.Items);
        Assert.Equal("Replaced", entry.Action);
        Assert.Contains("plan genérico de CondoPY", entry.Summary);
    }

    [Fact]
    public async Task CrearUnaCuentaDelPlanQuedaEnElHistorial()
    {
        _env.Setup();
        var api = _env.Controller();

        FinanceEnv.Ok(await api.Create(
            new LedgerCategoryUpsertRequest { BuildingId = _env.Building.Id, Code = "9", Name = "OTROS", Type = LedgerCategoryType.Expense, IsActive = true }, default));

        var entry = Assert.Single((await Audit(ConfigSectionKeys.Chart)).Items);
        Assert.Equal("Created", entry.Action);
        Assert.Equal("LedgerCategory", entry.EntityType);
    }

    // ── Historial: consulta ──────────────────────────────────────────────────

    [Fact]
    public async Task ElHistorialSeFiltraPorSeccionYSePagina()
    {
        var audit = new ConfigAuditWriter(_env.T.Db, _env.Tenant);
        for (var i = 0; i < 5; i++)
        {
            audit.Add(_env.Company.Id, _env.Building.Id, ConfigSectionKeys.Chart, "Updated", $"Cambio {i}");
        }
        audit.Add(_env.Company.Id, _env.Building.Id, ConfigSectionKeys.Budget, "Updated", "Presupuesto");
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(6, (await Audit()).TotalCount);
        Assert.Equal(5, (await Audit(ConfigSectionKeys.Chart)).TotalCount);

        var second = await Audit(page: 2, pageSize: 4);
        Assert.Equal(6, second.TotalCount);
        Assert.Equal(2, second.Items.Count);
    }

    [Fact]
    public async Task ElHistorialMuestraElNombreDeQuienLoHizo()
    {
        _env.Tenant.UserId = _env.Admin.Id;
        new ConfigAuditWriter(_env.T.Db, _env.Tenant).Add(_env.Company.Id, _env.Building.Id, ConfigSectionKeys.Chart, "Updated", "Algo");
        await _env.T.Db.SaveChangesAsync();

        var entry = Assert.Single((await Audit()).Items);

        Assert.Equal(_env.Admin.FullName, entry.UserName);
        Assert.Equal("Plan de cuentas y cuentas financieras", entry.SectionName);
    }

    [Fact]
    public async Task ElHistorialNoMezclaEdificios()
    {
        var other = _env.T.AddBuilding(_env.Company, "Otro edificio");
        var audit = new ConfigAuditWriter(_env.T.Db, _env.Tenant);
        audit.Add(_env.Company.Id, other.Id, ConfigSectionKeys.Chart, "Updated", "De otro");
        audit.Add(_env.Company.Id, _env.Building.Id, ConfigSectionKeys.Chart, "Updated", "Propio");
        await _env.T.Db.SaveChangesAsync();

        var entry = Assert.Single((await Audit()).Items);

        Assert.Equal("Propio", entry.Summary);
    }

    [Fact]
    public async Task SoloSuperAdminYAdministradorVenElHistorial()
    {
        _env.Access.Buildings.Add(_env.Building.Id);

        foreach (var role in new[] { "CompanyOperator", "BuildingManager", "Owner" })
        {
            _env.LoginAs(role);
            var result = await Api().GetAudit(_env.Building.Id, null, null, null, null, 1, 25, default);
            Assert.Equal(403, (result.Result as ObjectResult)?.StatusCode);
        }

        _env.LoginAs("CompanyAdmin");
        Assert.NotNull(await Audit());
    }

    // ── Ficha del edificio: el guardado real deja el historial por seccion ────

    private BuildingsController BuildingsApi()
    {
        var controller = new BuildingsController(_env.T.Db, _env.Access, _env.Tenant,
            new PushDispatcher(_env.T.Db, new Support.NoopPushSender(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PushDispatcher>.Instance),
            new Support.StubWebHostEnvironment(Path.GetTempPath()));
        // El controlador consulta User.IsInRole: se le da una identidad con el rol de la prueba.
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, _env.Tenant.Role)], "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private BuildingUpsertRequest FichaRequest(Action<BuildingUpsertRequest>? tweak = null)
    {
        // El edificio de prueba arranca con el codigo y la direccion que valida la ficha, para que "sin cambios" sea de verdad sin cambios.
        var tracked = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        tracked.Code = tracked.Code.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(tracked.Address)) tracked.Address = "Av. Test 123";
        _env.T.Db.SaveChanges();

        var b = _env.T.Db.Buildings.AsNoTracking().Single(x => x.Id == _env.Building.Id);
        var request = new BuildingUpsertRequest
        {
            Name = b.Name, Code = b.Code, Address = b.Address,
            IsActive = true, InvoicingMode = b.InvoicingMode, IncomeTreatment = b.IncomeTreatment
        };
        tweak?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task GuardarLaFichaDejaUnaEntradaPorCadaSeccionQueCambio()
    {
        var result = await BuildingsApi().Update(_env.Building.Id, FichaRequest(r =>
        {
            r.DefaultDueDay = 12;
            r.LateFeeRatePercentage = 2m;
            r.LateFeeFrequency = LateFeeFrequency.Weekly;
            r.ReserveFundPercentage = 5m;
            r.BankAccounts = [new BuildingBankAccountDto { BankName = "Banco X", AccountNumber = "123456", HolderName = "Consorcio" }];
        }), default);
        Assert.NotNull(FinanceEnv.Ok(result));

        var entries = (await Audit()).Items;
        Assert.Equal(
            [ConfigSectionKeys.Collection, ConfigSectionKeys.Funds, ConfigSectionKeys.LateFee],
            entries.Select(x => x.Section).OrderBy(x => x, StringComparer.Ordinal).ToArray());

        var collection = entries.Single(x => x.Section == ConfigSectionKeys.Collection);
        Assert.Equal(["defaultDueDay", "bankAccounts"], collection.Changes.Select(c => c.Field).ToArray());
        Assert.Equal("12", collection.Changes[0].After);
        Assert.Equal("Banco X 123456", collection.Changes[1].After);
        Assert.Equal("Building", collection.EntityType);
    }

    [Fact]
    public async Task GuardarLaFichaSinCambiosNoDejaHistorial()
    {
        FinanceEnv.Ok(await BuildingsApi().Update(_env.Building.Id, FichaRequest(), default));

        Assert.Equal(0, (await Audit()).TotalCount);
    }

    // ── Foto de la ficha del edificio ────────────────────────────────────────

    [Fact]
    public void LaFotoDeLaFichaAgrupaLosCambiosPorSeccion()
    {
        var b = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        var before = BuildingConfigSnapshot.Capture(b, []);

        b.Ruc = "80099999-1";
        b.DefaultDueDay = 15;
        b.LateFeeRatePercentage = 2m;
        b.LateFeeFrequency = LateFeeFrequency.Daily;
        b.ReserveFundPercentage = 3.5m;
        var after = BuildingConfigSnapshot.Capture(b,
            [new BuildingBankAccount { BankName = "Banco X", AccountNumber = "99" }]);

        var diff = BuildingConfigSnapshot.Diff(before, after);

        Assert.Equal(
            [ConfigSectionKeys.Identity, ConfigSectionKeys.Collection, ConfigSectionKeys.LateFee, ConfigSectionKeys.Funds],
            diff.Keys.ToArray());
        Assert.Equal(["ruc"], diff[ConfigSectionKeys.Identity].Select(c => c.Field).ToArray());
        Assert.Equal(["defaultDueDay", "bankAccounts"], diff[ConfigSectionKeys.Collection].Select(c => c.Field).ToArray());
        Assert.Equal("Banco X 99", diff[ConfigSectionKeys.Collection][1].After);
        Assert.Equal(["lateFeeRatePercentage", "lateFeeFrequency"], diff[ConfigSectionKeys.LateFee].Select(c => c.Field).ToArray());
        Assert.Equal("3.5", diff[ConfigSectionKeys.Funds].Single().After);
    }

    [Fact]
    public void SinCambiosLaFotoNoDaDiferencias_YVacioEsLoMismoQueNulo()
    {
        var b = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        var before = BuildingConfigSnapshot.Capture(b, []);

        b.PaymentInstructions = "   ";   // en blanco = sin dato
        var after = BuildingConfigSnapshot.Capture(b, []);

        Assert.Empty(BuildingConfigSnapshot.Diff(before, after));
    }

    [Fact]
    public async Task LasCuentasBancariasDadasDeBajaNoCuentanEnLaFoto()
    {
        var account = new BuildingBankAccount { CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, BankName = "Banco X", AccountNumber = "1", HolderName = "H" };
        var b = await _env.T.Db.Buildings.SingleAsync(x => x.Id == _env.Building.Id);
        var before = BuildingConfigSnapshot.Capture(b, [account]);

        account.IsDeleted = true;
        var after = BuildingConfigSnapshot.Capture(b, [account]);

        var change = Assert.Single(BuildingConfigSnapshot.Diff(before, after)[ConfigSectionKeys.Collection]);
        Assert.Equal("Banco X 1", change.Before);
        Assert.Null(change.After);
    }
}
