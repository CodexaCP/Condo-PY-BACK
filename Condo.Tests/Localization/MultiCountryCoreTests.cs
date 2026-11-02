using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Localization;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Condo.Tests.Localization;

// Nucleo multipais: moneda central, perfiles de pais, documentos de otros paises, impuesto por empresa, alta de empresa con pais,
// cadena de aprobacion y mayoria absoluta. Paraguay tiene que seguir comportandose como siempre.
public class MultiCountryCoreTests
{
    [Fact]
    public void Guaranies_se_escriben_con_puntos_y_sin_decimales()
    {
        Assert.Equal("Gs. 1.234.567", Money.Format(1234567m));
        Assert.Equal("Gs. 1.235", Money.Format(1234.5m));
        Assert.Equal("Gs. -1.000", Money.Format(-1000m));
    }

    [Fact]
    public void Pesos_se_escriben_con_dos_decimales_y_coma()
    {
        Assert.Equal("$ 1.234,50", Money.Format(1234.5m, Currencies.Ars));
        Assert.Equal("$ 0,00", Money.Format(0m, Currencies.Ars));
        Assert.Equal("US$ 1,234.50", Money.Format(1234.5m, Currencies.Usd));
    }

    [Fact]
    public void La_moneda_del_contexto_se_fija_y_se_libera()
    {
        Assert.Equal("PYG", Money.Current.Code);
        using (Money.Use(Currencies.Ars))
        {
            Assert.Equal("$ 10,00", Money.Format(10m));
            Assert.Equal(12.35m, Money.Round(12.345m));
        }

        Assert.Equal("PYG", Money.Current.Code);
        Assert.Equal(12m, Money.Round(12.345m));
    }

    [Fact]
    public async Task La_moneda_del_contexto_se_hereda_a_la_tarea_hija_y_se_libera_al_terminar()
    {
        using (Money.Use(Currencies.Ars))
        {
            var inner = await Task.Run(() => Money.Current.Code);
            Assert.Equal("ARS", inner);
        }

        var after = await Task.Run(() => Money.Current.Code);
        Assert.Equal("PYG", after);
    }

    [Fact]
    public void Los_perfiles_de_pais_traen_moneda_impuesto_y_capacidades()
    {
        var py = CountryProfiles.Find("py")!;
        Assert.Equal("PYG", py.DefaultCurrencyCode);
        Assert.Equal(10m, py.TaxGeneralRate);
        Assert.Equal(5m, py.TaxReducedRate);
        Assert.True(py.Capabilities.EmitsInvoices);
        Assert.True(py.Capabilities.OnlinePayments);

        var ar = CountryProfiles.Find("AR")!;
        Assert.Equal("ARS", ar.DefaultCurrencyCode);
        Assert.Equal(21m, ar.TaxGeneralRate);
        Assert.Null(ar.TaxReducedRate);
        Assert.False(ar.Capabilities.EmitsInvoices);
        Assert.False(ar.Capabilities.OnlinePayments);
        Assert.Contains(CountryProfiles.CabaJurisdiction, ar.Jurisdictions);
        Assert.Equal("Porcentual", ar.Glossary["coeficiente"]);

        var ve = CountryProfiles.Find("VE")!;
        Assert.True(ve.IsDraft);
        Assert.Null(ve.TaxGeneralRate);

        Assert.Null(CountryProfiles.Find("ZZ"));
        Assert.Equal("PY", CountryProfiles.FindOrDefault("ZZ").Code);
    }

    [Fact]
    public void Una_empresa_sin_datos_de_pais_sale_como_paraguay()
    {
        var locale = CompanyLocaleDto.From(null);
        Assert.Equal("PY", locale.CountryCode);
        Assert.Equal("PYG", locale.CurrencyCode);
        Assert.Equal(0, locale.CurrencyDecimals);
        Assert.Equal(10m, locale.TaxGeneralRate);

        var ar = CompanyLocaleDto.From(new Company { CountryCode = "AR", CurrencyCode = "ARS", LocaleCode = "es-AR", TaxName = "IVA", TaxGeneralRate = 21m, TaxReducedRate = null });
        Assert.Equal("ARS", ar.CurrencyCode);
        Assert.Equal(2, ar.CurrencyDecimals);
        Assert.Equal("CUIT", ar.TaxIdLabel);
        Assert.Equal(21m, ar.TaxGeneralRate);
        Assert.Null(ar.TaxReducedRate);
    }

    [Theory]
    [InlineData("DNI", "12.345.678", true)]
    [InlineData("DNI", "12ab", false)]
    [InlineData("CUIT", "20-12345678-3", true)]
    [InlineData("CUIL", "20123456783", true)]
    [InlineData("CUIT", "123", false)]
    [InlineData("CedulaVE", "V-12345678", true)]
    [InlineData("CedulaVE", "12345678", false)]
    [InlineData("RIF", "J-12345678-9", true)]
    [InlineData("RIF", "12345678", false)]
    [InlineData("CedulaParaguaya", "1234567", true)]
    [InlineData("CedulaParaguaya", "12ab", false)]
    public void Los_documentos_de_cada_pais_se_validan_por_su_tipo(string type, string number, bool valid)
    {
        var error = PersonRules.ValidateDocument(type, number);
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void El_ruc_paraguayo_sigue_validandose_con_su_digito_verificador()
    {
        var digit = BuildingProfile.ComputeRucCheckDigit("80012345");
        Assert.Null(PersonRules.ValidateDocument("RUC", $"80012345-{digit}"));
        Assert.NotNull(PersonRules.ValidateDocument("RUC", $"80012345-{(digit + 1) % 10}"));
        Assert.True(BuildingProfile.IsValidTaxId($"80012345-{digit}", "PY"));
        Assert.False(BuildingProfile.IsValidTaxId("80012345-0123", "PY"));
        Assert.True(BuildingProfile.IsValidTaxId("30-71234567-8", "AR"));
        Assert.False(BuildingProfile.IsValidTaxId("30-7123", "AR"));
    }

    [Fact]
    public void Las_tasas_de_impuesto_son_las_de_la_empresa()
    {
        Assert.True(VatMath.IsValidRate(10m, 10m, 5m));
        Assert.True(VatMath.IsValidRate(5m, 10m, 5m));
        Assert.True(VatMath.IsValidRate(0m, 10m, 5m));
        Assert.False(VatMath.IsValidRate(21m, 10m, 5m));
        Assert.True(VatMath.IsValidRate(21m, 21m, null));
        Assert.False(VatMath.IsValidRate(10m, 21m, null));
        Assert.Equal("10, 5", VatMath.RatesText(10m, 5m));
        Assert.Equal("21", VatMath.RatesText(21m, null));
        Assert.Equal(21m, VatMath.RateOf(VatTreatment.Vat10, 21m, null));
        Assert.Null(VatMath.RateOf(VatTreatment.Vat5, 21m, null));
        Assert.Equal(0m, VatMath.RateOf(VatTreatment.Exempt, 21m, null));
        // Paraguay: la sobrecarga de siempre no cambia.
        Assert.Equal(10m, VatMath.RateOf(VatTreatment.Vat10));
    }

    [Fact]
    public void La_tolerancia_de_un_comprobante_sigue_la_moneda()
    {
        var comprobante = new Comprobante { Lines = [(new ExpenseCharge(), 100m)] };

        // Guaranies: media unidad de diferencia se acepta.
        Assert.True(ComprobanteService.Cover(100.4m, [comprobante]).Exact);
        // Pesos: 40 centavos de diferencia no son lo mismo que el comprobante.
        using (Money.Use(Currencies.Ars))
        {
            Assert.False(ComprobanteService.Cover(100.4m, [comprobante]).Exact);
            Assert.True(ComprobanteService.Cover(100.00m, [comprobante]).Exact);
        }
    }

    [Fact]
    public void Los_aportes_de_la_liquidacion_se_redondean_a_la_unidad_de_la_moneda()
    {
        var py = SettlementContributions.Compute(1000m, 0m, 3.333m, null);
        Assert.Equal(33m, py.ReserveContribution);

        using (Money.Use(Currencies.Ars))
        {
            var ar = SettlementContributions.Compute(1000m, 0m, 3.333m, null);
            Assert.Equal(33.33m, ar.ReserveContribution);
        }
    }

    private static CompaniesController NewCompaniesController(TestDb t) =>
        new(t.Db, new FakeTenantContext { Role = "SuperAdmin" }, new CompanyLocaleService(t.Db, new MemoryCache(new MemoryCacheOptions())));

    [Fact]
    public async Task Una_empresa_nueva_sin_pais_queda_en_paraguay_con_su_iva()
    {
        using var t = new TestDb();
        var result = await NewCompaniesController(t).Create(new CompanyUpsertRequest { Name = "Admin PY", Slug = "admin-py" }, CancellationToken.None);

        var dto = Assert.IsType<CompanyDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("PY", dto.CountryCode);
        Assert.Equal("PYG", dto.CurrencyCode);
        Assert.Equal("IVA", dto.TaxName);
        Assert.Equal(10m, dto.TaxGeneralRate);
        Assert.Equal(5m, dto.TaxReducedRate);
    }

    [Fact]
    public async Task Una_empresa_argentina_toma_pesos_y_el_iva_del_pais()
    {
        using var t = new TestDb();
        var result = await NewCompaniesController(t).Create(
            new CompanyUpsertRequest { Name = "Admin AR", Slug = "admin-ar", CountryCode = "ar" }, CancellationToken.None);

        var dto = Assert.IsType<CompanyDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("AR", dto.CountryCode);
        Assert.Equal("ARS", dto.CurrencyCode);
        Assert.Equal("es-AR", dto.LocaleCode);
        Assert.Equal(21m, dto.TaxGeneralRate);
        Assert.Null(dto.TaxReducedRate);

        var saved = t.NewContext().Companies.Single(x => x.Slug == "admin-ar");
        Assert.Equal("ARS", saved.CurrencyCode);
    }

    [Fact]
    public async Task El_impuesto_se_puede_fijar_a_mano_al_crear_la_empresa()
    {
        using var t = new TestDb();
        var result = await NewCompaniesController(t).Create(new CompanyUpsertRequest
        {
            Name = "Admin VE", Slug = "admin-ve", CountryCode = "VE", TaxConfigured = true, TaxName = "IVA", TaxGeneralRate = 16m
        }, CancellationToken.None);

        var dto = Assert.IsType<CompanyDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.True(dto.IsDraftCountry);
        Assert.Equal(16m, dto.TaxGeneralRate);
        Assert.Null(dto.TaxReducedRate);
        Assert.Equal("VES", dto.CurrencyCode);
    }

    [Fact]
    public async Task Pais_moneda_o_tasas_invalidos_se_rechazan()
    {
        using var t = new TestDb();
        var controller = NewCompaniesController(t);

        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            new CompanyUpsertRequest { Name = "X", Slug = "x1", CountryCode = "ZZ" }, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            new CompanyUpsertRequest { Name = "X", Slug = "x2", CountryCode = "AR", CurrencyCode = "XXX" }, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            new CompanyUpsertRequest { Name = "X", Slug = "x3", TaxConfigured = true, TaxGeneralRate = 5m, TaxReducedRate = 10m }, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(
            new CompanyUpsertRequest { Name = "X", Slug = "x4", TaxConfigured = true, TaxGeneralRate = 120m }, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task La_moneda_no_cambia_cuando_la_empresa_ya_tiene_gastos()
    {
        using var t = new TestDb();
        var company = t.AddCompany("Con gastos");
        var building = t.AddBuilding(company);
        var period = new ExpensePeriod
        {
            CompanyId = company.Id, BuildingId = building.Id, Year = 2026, Month = 10, Name = "Octubre 2026",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), DueDate = new DateOnly(2026, 11, 10)
        };
        t.Db.ExpensePeriods.Add(period);
        t.Db.SaveChanges();
        t.Db.BuildingExpenses.Add(new BuildingExpense
        {
            CompanyId = company.Id, BuildingId = building.Id, ExpensePeriodId = period.Id, Category = BuildingExpenseCategory.Cleaning, Description = "Limpieza", Amount = 100m,
            ExpenseDate = new DateOnly(2026, 10, 1), SupplierName = "Prov"
        });
        t.Db.SaveChanges();
        var controller = NewCompaniesController(t);

        var blocked = await controller.Update(company.Id, new CompanyUpsertRequest
        {
            Name = company.Name, Slug = company.Slug, CountryCode = "AR"
        }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(blocked.Result);

        // Mismo pais y moneda: se puede editar lo demas.
        var ok = await controller.Update(company.Id, new CompanyUpsertRequest { Name = "Nuevo nombre", Slug = company.Slug, CountryCode = "PY" }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok.Result);
    }

    [Fact]
    public async Task La_mayoria_absoluta_se_mide_sobre_la_totalidad_de_unidades_y_coeficiente()
    {
        using var t = new TestDb();
        var company = t.AddCompany();
        var building = t.AddBuilding(company);
        var units = Enumerable.Range(1, 4).Select(i => t.AddUnit(building, $"U{i}")).ToList();
        foreach (var u in units) u.Coefficient = 0.25m;
        t.Db.SaveChanges();

        var tenant = new FakeTenantContext { Role = "SuperAdmin", CompanyId = company.Id };
        var controller = new VotesController(t.Db, new FakeAccessScope(tenant), tenant,
            new PushDispatcher(t.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));

        async Task<VoteDto> RunAsync(int votesForA)
        {
            var vote = new Vote { BuildingId = building.Id, Title = "Obra", MajorityRule = "DoubleAbsolute", WeightType = "ByCoefficient", Status = "Open" };
            var a = new VoteOption { Label = "Si", DisplayOrder = 0 };
            var b = new VoteOption { Label = "No", DisplayOrder = 1 };
            vote.Options.Add(a);
            vote.Options.Add(b);
            t.Db.Votes.Add(vote);
            await t.Db.SaveChangesAsync();
            for (var i = 0; i < votesForA; i++)
                t.Db.VoteCasts.Add(new VoteCast { VoteId = vote.Id, VoteOptionId = a.Id, UnitId = units[i].Id, CoefficientWeight = 0.25m });
            await t.Db.SaveChangesAsync();

            var result = await controller.GetById(vote.Id, CancellationToken.None);
            return Assert.IsType<VoteDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        }

        var tres = await RunAsync(3);
        Assert.Equal(75m, tres.Options.First(o => o.Label == "Si").PercentOfTotalUnits);
        Assert.True(tres.Options.First(o => o.Label == "Si").HasAbsoluteMajority);
        Assert.Equal(tres.Options.First(o => o.Label == "Si").Id, tres.ApprovedOptionId);

        // La mitad justa no es mayoria absoluta.
        var dos = await RunAsync(2);
        Assert.False(dos.Options.First(o => o.Label == "Si").HasAbsoluteMajority);
        Assert.Null(dos.ApprovedOptionId);
    }

    [Fact]
    public async Task Una_votacion_nueva_es_informativa_fuera_de_paraguay_y_formal_en_paraguay()
    {
        using var t = new TestDb();
        var py = t.AddCompany("PY");
        var ar = t.AddCompany("AR");
        ar.CountryCode = "AR";
        ar.CurrencyCode = "ARS";
        t.Db.SaveChanges();
        var buildingPy = t.AddBuilding(py);
        var buildingAr = t.AddBuilding(ar);

        var admin = t.AddUser(py, UserRole.CompanyAdmin, "Admin");
        var tenant = new FakeTenantContext { Role = "SuperAdmin", UserId = admin.Id };
        var controller = new VotesController(t.Db, new FakeAccessScope(tenant), tenant,
            new PushDispatcher(t.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));

        VoteUpsertRequest Req(Guid buildingId) => new() { BuildingId = buildingId, Title = "Consulta", OptionLabels = ["Si", "No"] };

        var votePy = Assert.IsType<VoteDto>(Assert.IsType<CreatedAtActionResult>((await controller.Create(Req(buildingPy.Id), CancellationToken.None)).Result).Value);
        var voteAr = Assert.IsType<VoteDto>(Assert.IsType<CreatedAtActionResult>((await controller.Create(Req(buildingAr.Id), CancellationToken.None)).Result).Value);

        Assert.False(votePy.IsInformative);
        Assert.True(voteAr.IsInformative);
        Assert.Equal("Simple", votePy.MajorityRule);
    }
}
