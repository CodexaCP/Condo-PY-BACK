using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Tests.Finance;

/// <summary>Resumen del panel principal: sin edificio suma todos los accesibles; con edificio, solo ese.</summary>
public class DashboardSummaryByBuildingTests
{
    // Dos edificios de la misma empresa: dos unidades en el primero y una en el segundo.
    private static Building TwoBuildings(FinanceEnv env)
    {
        var second = env.T.AddBuilding(env.Company, "Segundo edificio");
        env.T.AddUnit(env.Building, "A1");
        env.T.AddUnit(env.Building, "A2");
        env.T.AddUnit(second, "B1");
        return second;
    }

    private static DashboardController Api(FinanceEnv env, FakeAccessScope access) => new(env.T.Db, access);

    private static DashboardSummaryDto Summary(ActionResult<DashboardSummaryDto> result) =>
        Assert.IsType<DashboardSummaryDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

    // El resumen suma importes decimales en la base: SQLite no lo soporta, por eso corre solo con SQL Server.
    [SqlServerFact]
    public async Task SinEdificio_SumaTodos_YConEdificio_SoloEse()
    {
        using var env = new FinanceEnv(sqlServer: true);
        var second = TwoBuildings(env);
        var api = Api(env, env.Access);

        var all = Summary(await api.GetSummary(null, default));
        var first = Summary(await api.GetSummary(env.Building.Id, default));
        var only = Summary(await api.GetSummary(second.Id, default));

        Assert.Equal(2, all.TotalBuildings);
        Assert.Equal(3, all.TotalUnits);
        Assert.Equal(1, first.TotalBuildings);
        Assert.Equal(2, first.TotalUnits);
        Assert.Equal(1, only.TotalBuildings);
        Assert.Equal(1, only.TotalUnits);
    }

    [Fact]
    public async Task UnEdificioAjeno_SeRechaza()
    {
        using var env = new FinanceEnv();
        var second = TwoBuildings(env);
        var tenant = new FakeTenantContext { CompanyId = env.Company.Id, Role = "BuildingManager" };
        var access = new FakeAccessScope(tenant);
        access.Buildings.Add(env.Building.Id);

        var result = await Api(env, access).GetSummary(second.Id, default);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
