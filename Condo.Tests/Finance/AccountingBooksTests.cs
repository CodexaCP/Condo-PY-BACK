using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Condo.Tests.Finance;

/// <summary>
/// Libros contables del edificio (Centro de configuracion, seccion «Libros contables»): titular configurable (consorcio o empresa
/// administradora), datos por defecto, validaciones, permisos, historial y estado en el resumen del Centro.
/// </summary>
public class AccountingBooksTests : IDisposable
{
    // Formato 80012345-0 con su digito verificador correcto.
    private const string ValidRuc = "80012345-0";
    private const string OtherValidRuc = "80000001-3";

    private readonly FinanceEnv _env = new();

    public AccountingBooksTests()
    {
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Tenant.UserId = _env.Admin.Id;
        _env.Access.Buildings.Add(_env.Building.Id);
    }

    public void Dispose() => _env.Dispose();

    private BuildingConfigBooksController Api() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new AccountingBooksService(_env.T.Db, _env.Tenant));

    private async Task<BuildingConfigOverviewDto> Overview() =>
        FinanceEnv.Ok(await new BuildingConfigController(
            _env.T.Db, _env.Access, _env.Tenant, new BuildingConfigOverviewService(_env.T.Db, new FinanceModuleGate(_env.T.Db)))
            .GetOverview(_env.Building.Id, default));

    private void FillBuildingFiscalData()
    {
        var b = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        b.LegalName = "Consorcio Edificio Finanzas";
        b.Ruc = ValidRuc;
        b.FiscalAddress = "Av. Mcal. López 1234, Asunción";
        _env.T.Db.SaveChanges();
    }

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result.Result).StatusCode);

    private static UpdateAccountingBooksRequest Request(Action<UpdateAccountingBooksRequest>? tweak = null)
    {
        var r = new UpdateAccountingBooksRequest { Holder = AccountingBookHolder.Consortium };
        tweak?.Invoke(r);
        return r;
    }

    private List<FinanceAuditLog> Audit() =>
        _env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Books).OrderBy(x => x.CreatedAtUtc).ToList();

    // ═════════════════════════════════════════════════════════════════════════
    // Lectura y datos por defecto
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SinGuardarRigeElConsorcioConLosDatosDeLaFichaDelEdificio()
    {
        FillBuildingFiscalData();

        var dto = FinanceEnv.Ok(await Api().Get(_env.Building.Id, default));

        Assert.False(dto.Configured);
        Assert.True(dto.FinanceAvailable);
        Assert.Equal(AccountingBookHolder.Consortium, dto.Holder);
        Assert.Equal("Consorcio Edificio Finanzas", dto.Effective.LegalName);
        Assert.Equal(ValidRuc, dto.Effective.Ruc);
        Assert.Equal("Av. Mcal. López 1234, Asunción", dto.Effective.Address);
        Assert.Equal("Empresa Finanzas", dto.AdminCompanyDefaults.LegalName);
        Assert.Null(dto.AdminCompanyDefaults.Ruc);
    }

    [Fact]
    public async Task ElConsorcioSinRazonSocialUsaElNombreDelEdificioYLaDireccionComun()
    {
        var b = _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);
        b.LegalName = null;
        b.FiscalAddress = null;
        b.Address = "Calle sin fiscal 5";
        _env.T.Db.SaveChanges();

        var dto = FinanceEnv.Ok(await Api().Get(_env.Building.Id, default));

        Assert.Equal("Edificio Finanzas", dto.ConsortiumDefaults.LegalName);
        Assert.Equal("Calle sin fiscal 5", dto.ConsortiumDefaults.Address);
    }

    [Fact]
    public void LoCargadoPisaAlPorDefectoYLoVacioCaeAlPorDefecto()
    {
        var building = new Building { Name = "Torre", LegalName = "Torre SA", Ruc = ValidRuc, FiscalAddress = "Fiscal 1", Address = "Calle 2" };
        var settings = new AccountingBookSettings
        {
            Holder = AccountingBookHolder.Consortium, HolderLegalName = "  Otro nombre ", HolderRuc = "   ", AccountantName = "Ana Gómez"
        };

        var header = AccountingBooksService.Resolve(building, settings, "Administradora SA");

        Assert.Equal("Otro nombre", header.LegalName);
        Assert.Equal(ValidRuc, header.Ruc);
        Assert.Equal("Fiscal 1", header.Address);
        Assert.Equal("Ana Gómez", header.AccountantName);

        settings.Holder = AccountingBookHolder.AdminCompany;
        settings.HolderLegalName = null;
        var admin = AccountingBooksService.Resolve(building, settings, "Administradora SA");
        Assert.Equal("Administradora SA", admin.LegalName);
        Assert.Null(admin.Ruc);
        Assert.Null(admin.Address);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Guardar
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConfigurarLaEmpresaAdministradoraQuedaGuardadoYRegistrado()
    {
        var saved = FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r =>
        {
            r.Holder = AccountingBookHolder.AdminCompany;
            r.HolderRuc = OtherValidRuc;
            r.HolderAddress = "Oficina central 10";
            r.AccountantName = "  Ana Gómez ";
            r.AccountantRegistration = "CP-1234";
            r.BookNumber = "1";
            r.RubricDate = new DateOnly(2026, 1, 15);
            r.AuthorizedFolios = 200;
            r.UsedFolios = 12;
        }), default));

        Assert.True(saved.Configured);
        Assert.Equal(AccountingBookHolder.AdminCompany, saved.Holder);
        Assert.Equal("Empresa Finanzas", saved.Effective.LegalName);     // sin razon social cargada: el nombre de la empresa
        Assert.Equal(OtherValidRuc, saved.Effective.Ruc);
        Assert.Equal("Ana Gómez", saved.AccountantName);

        var row = _env.T.NewContext().AccountingBookSettings.Single();
        Assert.Equal(_env.Building.Id, row.BuildingId);
        Assert.Equal(_env.Company.Id, row.CompanyId);
        Assert.Equal(new DateOnly(2026, 1, 15), row.RubricDate);
        Assert.Equal(200, row.AuthorizedFolios);

        var entry = Assert.Single(Audit());
        Assert.Equal("Created", entry.Action);
        Assert.Contains("Empresa administradora", entry.Summary);
    }

    [Fact]
    public async Task GuardarDosVecesLoMismoNoDuplicaLaFilaNiElHistorial_YUnCambioDejaSoloLoQueCambio()
    {
        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => r.AccountantName = "Ana Gómez"), default));
        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => r.AccountantName = "Ana Gómez"), default));

        Assert.Single(_env.T.NewContext().AccountingBookSettings.ToList());
        Assert.Single(Audit());

        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => { r.AccountantName = "Ana Gómez"; r.BookNumber = "2"; }), default));

        var update = Audit().Last();
        Assert.Equal("Updated", update.Action);
        Assert.Equal(2, Audit().Count);
        Assert.Contains("bookNumber", update.ChangesJson);
        Assert.DoesNotContain("accountantName", update.ChangesJson);
    }

    [Fact]
    public async Task LosCamposVaciosSeGuardanComoNulos()
    {
        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => { r.HolderLegalName = "  "; r.BookNumber = ""; }), default));

        var row = _env.T.NewContext().AccountingBookSettings.Single();
        Assert.Null(row.HolderLegalName);
        Assert.Null(row.BookNumber);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Validaciones
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnRucInvalidoSeRechazaPeroUnoViejoYaGuardadoNoBloqueaOtrosCambios()
    {
        var bad = await Api().Update(_env.Building.Id, Request(r => r.HolderRuc = "80012345-9"), default);
        Assert.Contains("RUC", FinanceEnv.BadRequestText(bad));
        Assert.Empty(_env.T.NewContext().AccountingBookSettings.ToList());

        // Un RUC viejo con digito incorrecto ya guardado no se vuelve a controlar si no lo cambian.
        _env.T.Db.AccountingBookSettings.Add(new AccountingBookSettings { CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, HolderRuc = "80012345-9" });
        _env.T.Db.SaveChanges();

        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => { r.HolderRuc = "80012345-9"; r.BookNumber = "3"; }), default));
        Assert.Equal("3", _env.T.NewContext().AccountingBookSettings.Single().BookNumber);
    }

    [Fact]
    public async Task ValidaTitularFechaFoliosYLargos()
    {
        Assert.Contains("titular", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => r.Holder = (AccountingBookHolder)99), default)));
        Assert.Contains("rúbrica", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => r.RubricDate = FinancePeriods.Today().AddDays(3)), default)));
        Assert.Contains("rúbrica", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => r.RubricDate = new DateOnly(1970, 1, 1)), default)));
        Assert.Contains("folios", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => { r.AuthorizedFolios = 100; r.UsedFolios = 101; }), default)));
        Assert.Contains("folios", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => r.AuthorizedFolios = -1), default)));
        Assert.Contains("folios", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => r.UsedFolios = AccountingBooksService.MaxFolios + 1), default)));
        Assert.Contains("200 caracteres", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => r.AccountantName = new string('a', 201)), default)));
        Assert.Contains("30 caracteres", FinanceEnv.BadRequestText(
            await Api().Update(_env.Building.Id, Request(r => r.BookNumber = new string('1', 31)), default)));

        Assert.Empty(_env.T.NewContext().AccountingBookSettings.ToList());
        Assert.Empty(Audit());
    }

    [Fact]
    public async Task UsarTodosLosFoliosAutorizadosEsValido()
    {
        var saved = FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => { r.AuthorizedFolios = 100; r.UsedFolios = 100; }), default));

        Assert.Equal(100, saved.UsedFolios);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Permisos, edificio ajeno y Finanzas apagado
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LaVenSuperAdminAdministradorYOperador_LaEditanSoloSuperAdminYAdministrador()
    {
        foreach (var role in new[] { "SuperAdmin", "CompanyAdmin" })
        {
            _env.LoginAs(role);
            Assert.True(FinanceEnv.Ok(await Api().Get(_env.Building.Id, default)).CanEdit);
        }

        _env.LoginAs("CompanyOperator");
        Assert.False(FinanceEnv.Ok(await Api().Get(_env.Building.Id, default)).CanEdit);
        AssertStatus(await Api().Update(_env.Building.Id, Request(), default), 403);

        _env.LoginAs("BuildingManager");
        AssertStatus(await Api().Get(_env.Building.Id, default), 403);
        AssertStatus(await Api().Update(_env.Building.Id, Request(), default), 403);

        Assert.Empty(_env.T.NewContext().AccountingBookSettings.ToList());
    }

    [Fact]
    public async Task UnEdificioAjenoResponde404()
    {
        var other = Guid.NewGuid();

        AssertStatus(await Api().Get(other, default), 404);
        AssertStatus(await Api().Update(other, Request(), default), 404);
    }

    [Fact]
    public async Task SinFinanzasNoSeEdita_YLaLecturaAvisaQueNoEstaDisponible()
    {
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();

        Assert.False(FinanceEnv.Ok(await Api().Get(_env.Building.Id, default)).FinanceAvailable);
        AssertStatus(await Api().Update(_env.Building.Id, Request(), default), 403);
        Assert.Empty(_env.T.NewContext().AccountingBookSettings.ToList());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Resumen del Centro
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LaSeccionApareceOpcionalSinConfigurarYCompletaAlConfigurarla()
    {
        FillBuildingFiscalData();

        var before = (await Overview()).Sections.Single(s => s.Key == ConfigSectionKeys.Books);
        Assert.Equal(ConfigSectionStatus.Optional, before.Status);
        Assert.False(before.Required);
        Assert.Equal("Libros contables", before.Name);
        Assert.Equal(13, before.Order);
        Assert.Equal("self", before.LinkKind);
        Assert.Equal("books", before.LinkTab);
        Assert.Contains(before.Reasons, r => r.Contains("titular"));

        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(), default));

        var after = (await Overview()).Sections.Single(s => s.Key == ConfigSectionKeys.Books);
        Assert.Equal(ConfigSectionStatus.Complete, after.Status);
        Assert.Empty(after.Reasons);
        Assert.Equal("Consorcio", after.Summary.Single(i => i.Label == "Titular del libro").Value);
        Assert.Equal(ValidRuc, after.Summary.Single(i => i.Label == "RUC").Value);
    }

    [Fact]
    public async Task LaEmpresaAdministradoraSinRucNoQuedaCompletaHastaCargarlo()
    {
        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => r.Holder = AccountingBookHolder.AdminCompany), default));

        var open = (await Overview()).Sections.Single(s => s.Key == ConfigSectionKeys.Books);
        Assert.Equal(ConfigSectionStatus.Optional, open.Status);
        Assert.Contains(open.Reasons, r => r.Contains("RUC"));

        FinanceEnv.Ok(await Api().Update(_env.Building.Id, Request(r => { r.Holder = AccountingBookHolder.AdminCompany; r.HolderRuc = OtherValidRuc; }), default));

        Assert.Equal(ConfigSectionStatus.Complete, (await Overview()).Sections.Single(s => s.Key == ConfigSectionKeys.Books).Status);
    }

    [Fact]
    public async Task ElEncargadoNoVeLaSeccion_YLosDemasOrdenesQuedanCorridos()
    {
        _env.LoginAs("BuildingManager");
        Assert.DoesNotContain((await Overview()).Sections, s => s.Key == ConfigSectionKeys.Books);

        _env.LoginAs("CompanyAdmin");
        var orders = (await Overview()).Sections.ToDictionary(s => s.Key, s => s.Order);
        Assert.Equal(13, orders[ConfigSectionKeys.Books]);
        Assert.Equal(14, orders[ConfigSectionKeys.Documents]);
        Assert.Equal(15, orders[ConfigSectionKeys.Payments]);
        Assert.Equal(orders.Count, orders.Values.Distinct().Count());
    }

    [Fact]
    public async Task SinFinanzasLaSeccionNoEstaDisponible()
    {
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();

        Assert.Equal(ConfigSectionStatus.NotAvailable, (await Overview()).Sections.Single(s => s.Key == ConfigSectionKeys.Books).Status);
    }
}
