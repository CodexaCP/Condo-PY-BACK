using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Marketplace;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Tests.Finance;

/// <summary>Requiere SQL Server real (la suma decimal por periodo no se traduce en SQLite). Estado de cuenta de una unidad: los cargos de todos los periodos se suman en una sola consulta y el resultado no cambia.</summary>
public class AccountStatementsQueryTests
{
    [SqlServerFact]
    public async Task El_total_de_cargos_por_periodo_ignora_borradores_borrados_y_otras_unidades()
    {
        using var t = new TestDb(useSqlServer: true);
        var tenant = new FakeTenantContext { Role = "SuperAdmin" };
        var access = new FakeAccessScope(tenant);
        var company = t.AddCompany();
        var building = t.AddBuilding(company);
        var unit = t.AddUnit(building);
        var otherUnit = t.AddUnit(building);

        ExpensePeriod Period(int month, ExpensePeriodStatus status) => new()
        {
            CompanyId = company.Id, BuildingId = building.Id, Year = 2026, Month = month, Name = $"Mes {month}",
            StartDate = new DateOnly(2026, month, 1), EndDate = new DateOnly(2026, month, 28), DueDate = new DateOnly(2026, month, 28),
            Status = status
        };
        ExpenseCharge Charge(ExpensePeriod p, Unit u, decimal amount, bool deleted = false) => new()
        {
            CompanyId = company.Id, ExpensePeriodId = p.Id, UnitId = u.Id, Concept = "Expensa", Amount = amount, IsDeleted = deleted
        };

        var published1 = Period(1, ExpensePeriodStatus.Published);
        var published2 = Period(2, ExpensePeriodStatus.Published);
        var draft = Period(3, ExpensePeriodStatus.Draft);
        t.Db.ExpensePeriods.AddRange(published1, published2, draft);
        t.Db.ExpenseCharges.AddRange(
            Charge(published1, unit, 100m), Charge(published1, unit, 50m),
            Charge(published1, unit, 999m, deleted: true),
            Charge(published1, otherUnit, 777m),
            Charge(published2, unit, 30m),
            Charge(draft, unit, 500m));
        t.Db.SaveChanges();

        var controller = new AccountStatementsController(t.Db, access, tenant);
        var result = await controller.GetUnitStatements(unit.Id, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IReadOnlyList<AccountStatementPeriodDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(3, rows.Count);
        Assert.Equal(150m, rows.Single(x => x.ExpensePeriodId == published1.Id).TotalCharges);
        Assert.Equal(30m, rows.Single(x => x.ExpensePeriodId == published2.Id).TotalCharges);
        Assert.Equal(0m, rows.Single(x => x.ExpensePeriodId == draft.Id).TotalCharges);
    }
}
