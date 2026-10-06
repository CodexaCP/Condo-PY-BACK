using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Condo.Tests.People;

// Eliminacion definitiva de una unidad creada por error (solo SuperAdmin): se borra de la base y su codigo queda libre.
public class UnitPermanentDeleteTests
{
    private sealed class Scenario : IDisposable
    {
        public TestDb T { get; } = new();
        public UnitsController Controller { get; }
        public Company Company { get; }
        public Building Building { get; }

        public Scenario(string role = "SuperAdmin")
        {
            Company = T.AddCompany();
            Building = T.AddBuilding(Company);
            var tenant = new FakeTenantContext { Role = role, CompanyId = Company.Id };
            Controller = new UnitsController(T.Db, new FakeAccessScope(tenant));
        }

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public async Task Borra_la_unidad_y_sus_vinculos_y_el_codigo_queda_libre()
    {
        using var s = new Scenario();
        var unit = s.T.AddUnit(s.Building, "301");
        var owner = s.T.AddUser(s.Company);
        s.T.AddOwner(unit, owner);

        var result = await s.Controller.Delete(unit.Id, CancellationToken.None, permanent: true);

        Assert.IsType<NoContentResult>(result);
        using var check = s.T.NewContext();
        Assert.False(await check.Units.AnyAsync(x => x.Id == unit.Id));
        Assert.False(await check.UnitOwners.AnyAsync(x => x.UnitId == unit.Id));

        // El mismo codigo se puede volver a crear en el edificio.
        var again = await s.Controller.Create(
            new UnitUpsertRequest { BuildingId = s.Building.Id, Code = "301", Floor = "3", Coefficient = 0.1m }, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(again.Result);
    }

    [Fact]
    public async Task Tambien_borra_una_unidad_que_ya_estaba_eliminada_de_forma_logica()
    {
        using var s = new Scenario();
        var unit = s.T.AddUnit(s.Building, "302");
        unit.IsDeleted = true;
        s.T.Db.SaveChanges();

        // La eliminacion normal la dejo marcada: su codigo seguiria ocupado en el edificio.
        var result = await s.Controller.Delete(unit.Id, CancellationToken.None, permanent: true);

        Assert.IsType<NoContentResult>(result);
        using var check = s.T.NewContext();
        Assert.False(await check.Units.AnyAsync(x => x.Id == unit.Id));
    }

    [Fact]
    public async Task No_borra_una_unidad_con_movimientos()
    {
        using var s = new Scenario();
        var unit = s.T.AddUnit(s.Building, "303");
        var period = new ExpensePeriod
        {
            CompanyId = s.Company.Id, BuildingId = s.Building.Id, Year = 2026, Month = 9, Name = "Septiembre 2026",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), DueDate = new DateOnly(2026, 10, 10)
        };
        s.T.Db.ExpensePeriods.Add(period);
        s.T.Db.SaveChanges();
        s.T.Db.Payments.Add(new Payment { CompanyId = s.Company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, Amount = 100m, Reference = "P1" });
        s.T.Db.SaveChanges();

        var result = await s.Controller.Delete(unit.Id, CancellationToken.None, permanent: true);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("pagos", (string)bad.Value!);
        using var check = s.T.NewContext();
        Assert.True(await check.Units.AnyAsync(x => x.Id == unit.Id));
    }

    [Fact]
    public async Task Solo_el_superadmin_puede_eliminar_definitivamente()
    {
        using var s = new Scenario(role: "CompanyAdmin");
        var unit = s.T.AddUnit(s.Building, "304");

        var result = await s.Controller.Delete(unit.Id, CancellationToken.None, permanent: true);

        Assert.IsType<ForbidResult>(result);
        Assert.True(await s.T.Db.Units.AnyAsync(x => x.Id == unit.Id));
    }
}
