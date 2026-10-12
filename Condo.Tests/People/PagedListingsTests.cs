using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Condo.Tests.People;

// Listados paginados en el servidor de Unidades y Usuarios: busqueda, filtros, contadores y alcance por empresa.
public class PagedListingsTests
{
    private sealed class Scenario : IDisposable
    {
        public TestDb T { get; } = new();
        public Company Company { get; }
        public Building Building { get; }
        public FakeAccessScope Scope { get; }

        public Scenario(string role = "SuperAdmin")
        {
            Company = T.AddCompany();
            Building = T.AddBuilding(Company);
            var tenant = new FakeTenantContext { Role = role, CompanyId = Company.Id };
            Scope = new FakeAccessScope(tenant);
            Scope.Buildings.Add(Building.Id);
        }

        public UnitsController Units() => new(T.Db, Scope);
        public UsersController Users() => new(T.Db, Scope, new FakePasswordHasher());

        public void AddUnits(Building building, int count, string floor)
        {
            for (var i = 1; i <= count; i++)
            {
                var unit = T.AddUnit(building, $"{floor.PadLeft(2, '0')}-{i:00}");
                unit.Floor = floor;
            }
            T.Db.SaveChanges();
        }

        public void Dispose() => T.Dispose();
    }

    private sealed class FakePasswordHasher : Condo.Application.Abstractions.IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string hash) => password == hash;
    }

    private static T Value<T>(ActionResult<T> result) => Assert.IsType<OkObjectResult>(result.Result).Value is T v ? v : throw new InvalidCastException();

    // ─── Unidades ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unidades_pagina_en_el_servidor_y_dice_cuantas_hay_en_total()
    {
        using var s = new Scenario();
        s.AddUnits(s.Building, 30, "1");
        s.AddUnits(s.Building, 30, "2");

        var first = Value(await s.Units().GetPaged(null, null, null, null, 1, 25));
        Assert.Equal(60, first.TotalCount);
        Assert.Equal(25, first.Items.Count);

        var last = Value(await s.Units().GetPaged(null, null, null, null, 3, 25));
        Assert.Equal(10, last.Items.Count);
        Assert.Equal(60, last.Counts.Total);
    }

    [Fact]
    public async Task Unidades_filtra_por_edificio_y_por_piso_y_devuelve_los_pisos_en_orden_natural()
    {
        using var s = new Scenario();
        var other = s.T.AddBuilding(s.Company);
        s.AddUnits(s.Building, 3, "2");
        s.AddUnits(s.Building, 3, "10");
        s.AddUnits(s.Building, 3, "PB");
        s.AddUnits(other, 4, "1");

        var inBuilding = Value(await s.Units().GetPaged(null, s.Building.Id, null, null));
        Assert.Equal(9, inBuilding.TotalCount);
        Assert.Equal(["2", "10", "PB"], inBuilding.Floors);

        var floor = Value(await s.Units().GetPaged(null, s.Building.Id, "10", null));
        Assert.Equal(3, floor.TotalCount);
        Assert.All(floor.Items, u => Assert.Equal("10", u.Floor));
        Assert.Equal(9, floor.Counts.Total);   // los contadores no se achican con el piso elegido

        var everything = Value(await s.Units().GetPaged(null, null, null, null));
        Assert.Equal(13, everything.TotalCount);
        Assert.Empty(everything.Floors);       // sin edificio elegido no hay atajos de piso
    }

    [Fact]
    public async Task Unidades_busca_por_codigo_y_filtra_por_estado()
    {
        using var s = new Scenario();
        s.AddUnits(s.Building, 5, "1");
        var inactive = s.T.Db.Units.First(u => u.Code == "01-02");
        inactive.IsActive = false;
        var exempt = s.T.Db.Units.First(u => u.Code == "01-03");
        exempt.LateFeeExempt = true;
        s.T.Db.SaveChanges();

        var byCode = Value(await s.Units().GetPaged("01-04", null, null, null));
        Assert.Single(byCode.Items);
        Assert.Equal("01-04", byCode.Items[0].Code);

        var inactives = Value(await s.Units().GetPaged(null, null, null, "inactive"));
        Assert.Equal(["01-02"], inactives.Items.Select(u => u.Code));

        var exempts = Value(await s.Units().GetPaged(null, null, null, "lateFeeExempt"));
        Assert.Equal(["01-03"], exempts.Items.Select(u => u.Code));

        Assert.Equal(1, exempts.Counts.Inactive);
        Assert.Equal(4, exempts.Counts.Active);
        Assert.Equal(1, exempts.Counts.LateFeeExempt);
    }

    [Fact]
    public async Task Unidades_no_muestra_las_de_otra_empresa_al_administrador_de_empresa()
    {
        using var s = new Scenario("CompanyAdmin");
        s.AddUnits(s.Building, 4, "1");
        var foreignCompany = s.T.AddCompany();
        var foreignBuilding = s.T.AddBuilding(foreignCompany);
        s.AddUnits(foreignBuilding, 6, "1");

        var page = Value(await s.Units().GetPaged(null, null, null, null));

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(4, page.Counts.Total);
        Assert.All(page.Items, u => Assert.Equal(s.Building.Id, u.BuildingId));
    }

    [Fact]
    public async Task Unidades_acota_el_tamano_de_pagina_y_corrige_paginas_invalidas()
    {
        using var s = new Scenario();
        s.AddUnits(s.Building, 3, "1");

        var page = Value(await s.Units().GetPaged(null, null, null, null, page: -4, pageSize: 100_000));

        Assert.Equal(1, page.Page);
        Assert.Equal(PagedQuery.MaxPageSize, page.PageSize);
        Assert.Equal(3, page.Items.Count);
    }

    // ─── Usuarios ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Usuarios_separa_el_personal_de_propietarios_y_residentes_y_cuenta_cada_grupo()
    {
        using var s = new Scenario();
        s.T.AddUser(s.Company, UserRole.CompanyAdmin, "Ana");
        s.T.AddUser(s.Company, UserRole.BuildingManager, "Beto");
        for (var i = 0; i < 7; i++) s.T.AddUser(s.Company, UserRole.Owner, $"Prop{i}");
        for (var i = 0; i < 3; i++) s.T.AddUser(s.Company, UserRole.Resident, $"Res{i}");

        var staff = Value(await s.Users().GetPaged(null, "staff", null, s.Company.Id));
        Assert.Equal(2, staff.TotalCount);
        Assert.Equal(12, staff.Counts.All);
        Assert.Equal(2, staff.Counts.Staff);
        Assert.Equal(7, staff.Counts.Owners);
        Assert.Equal(3, staff.Counts.Residents);

        var owners = Value(await s.Users().GetPaged(null, "Owner", null, s.Company.Id));
        Assert.Equal(7, owners.TotalCount);

        var all = Value(await s.Users().GetPaged(null, null, null, s.Company.Id));
        Assert.Equal(12, all.TotalCount);
    }

    [Fact]
    public async Task Usuarios_busca_por_nombre_usuario_o_correo_y_filtra_por_estado()
    {
        using var s = new Scenario();
        var marta = s.T.AddUser(s.Company, UserRole.BuildingManager, "Marta");
        marta.Email = "marta.gomez@edificio.test";
        var pedro = s.T.AddUser(s.Company, UserRole.BuildingManager, "Pedro");
        pedro.IsActive = false;
        s.T.Db.SaveChanges();

        Assert.Single(Value(await s.Users().GetPaged("marta", null, null, s.Company.Id)).Items);
        Assert.Single(Value(await s.Users().GetPaged("gomez@edificio", null, null, s.Company.Id)).Items);
        Assert.Empty(Value(await s.Users().GetPaged("zzz", null, null, s.Company.Id)).Items);

        var inactive = Value(await s.Users().GetPaged(null, null, "inactive", s.Company.Id));
        Assert.Equal(["Pedro"], inactive.Items.Select(u => u.FirstName));
        Assert.Equal(2, inactive.Counts.All);  // el contador no se achica con el estado
    }

    [Fact]
    public async Task Usuarios_pagina_y_el_administrador_de_empresa_solo_ve_su_empresa()
    {
        using var s = new Scenario("CompanyAdmin");
        for (var i = 0; i < 30; i++) s.T.AddUser(s.Company, UserRole.Owner, $"Mine{i:00}");
        var foreign = s.T.AddCompany();
        for (var i = 0; i < 10; i++) s.T.AddUser(foreign, UserRole.Owner, $"Other{i}");

        var page2 = Value(await s.Users().GetPaged(null, null, null, null, 2, 25));

        Assert.Equal(30, page2.TotalCount);
        Assert.Equal(5, page2.Items.Count);
        Assert.All(page2.Items, u => Assert.Equal(s.Company.Id, u.CompanyId));
    }

    [Fact]
    public async Task Usuarios_el_super_admin_puede_filtrar_por_empresa()
    {
        using var s = new Scenario();
        var foreign = s.T.AddCompany();
        s.T.AddUser(s.Company, UserRole.BuildingManager, "Mia");
        s.T.AddUser(foreign, UserRole.BuildingManager, "Otra");

        var onlyForeign = Value(await s.Users().GetPaged(null, null, null, foreign.Id));

        Assert.Equal(["Otra"], onlyForeign.Items.Select(u => u.FirstName));
        Assert.Equal(1, onlyForeign.Counts.All);
    }
}
