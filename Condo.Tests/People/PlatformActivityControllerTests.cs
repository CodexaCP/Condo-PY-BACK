using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Condo.Tests.People;

// Panel del SuperAdmin: movimientos recientes de la plataforma.
public class PlatformActivityControllerTests
{
    [Fact]
    public async Task Cuenta_las_altas_de_la_semana_y_lista_los_ultimos_movimientos()
    {
        using var t = new TestDb();
        var company = t.AddCompany("Empresa Uno");
        t.AddUser(company, UserRole.CompanyAdmin, "Ana");
        t.AddBuilding(company, "Edificio Central");
        var controller = new PlatformActivityController(t.Db, new FakeTenantContext { Role = "SuperAdmin" });

        var result = await controller.Get(7, 8, CancellationToken.None);

        var dto = Assert.IsType<OkObjectResult>(result.Result).Value as PlatformActivityDto;
        Assert.Equal(7, dto!.Days.Count);
        Assert.Equal(3, dto.Days.Sum(d => d.Count));
        Assert.Contains(dto.Items, i => i.Kind == "CompanyCreated" && i.Detail.Contains("Empresa Uno"));
        Assert.Contains(dto.Items, i => i.Kind == "AdminCreated" && i.Detail.Contains("asignado a Empresa Uno"));
        Assert.Contains(dto.Items, i => i.Kind == "BuildingCreated" && i.Detail.Contains("Edificio Central"));
    }

    [Fact]
    public async Task Solo_el_superadmin_puede_verlo()
    {
        using var t = new TestDb();
        var controller = new PlatformActivityController(t.Db, new FakeTenantContext { Role = "CompanyAdmin" });

        var result = await controller.Get(7, 8, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
