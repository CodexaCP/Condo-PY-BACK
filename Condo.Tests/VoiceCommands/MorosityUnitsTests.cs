using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Condo.Tests.VoiceCommands;

public class MorosityUnitsTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly TestDb _t = new();
    private readonly Company _company;
    private readonly Building _building;
    private readonly Unit _a;
    private readonly Unit _b;
    private readonly Unit _c;

    public MorosityUnitsTests()
    {
        _company = _t.AddCompany();
        _building = _t.AddBuilding(_company);
        _a = _t.AddUnit(_building, "A");
        _b = _t.AddUnit(_building, "B");
        _c = _t.AddUnit(_building, "C");

        var old = AddPeriod("viejo", Today.AddDays(-100));
        var recent = AddPeriod("reciente", Today.AddDays(-5));
        AddCharge(old, _a, 100_000m);
        AddCharge(recent, _a, 50_000m);
        AddCharge(recent, _b, 70_000m);
        AddCharge(recent, _c, 30_000m);
        _t.Db.Payments.Add(new Payment
        {
            CompanyId = _company.Id, ExpensePeriodId = recent.Id, UnitId = _c.Id, Amount = 30_000m, PaymentDate = Today
        });
        _t.Db.SaveChanges();
    }

    private ExpensePeriod AddPeriod(string name, DateOnly due)
    {
        var period = new ExpensePeriod
        {
            CompanyId = _company.Id, BuildingId = _building.Id, Year = due.Year, Month = due.Month, Name = name,
            StartDate = due.AddDays(-10), EndDate = due.AddDays(-2), DueDate = due, Status = ExpensePeriodStatus.Published
        };
        _t.Db.ExpensePeriods.Add(period);
        _t.Db.SaveChanges();
        return period;
    }

    private void AddCharge(ExpensePeriod period, Unit unit, decimal amount)
    {
        _t.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = _company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, Concept = "Expensa", Amount = amount
        });
        _t.Db.SaveChanges();
    }

    private MorosityController Controller(string role = "SuperAdmin", bool accessible = true)
    {
        var scope = new FakeAccessScope(new FakeTenantContext { Role = role });
        if (accessible)
        {
            scope.Buildings.Add(_building.Id);
        }
        return new MorosityController(_t.Db, scope, new NoEmail());
    }

    private sealed class NoEmail : Condo.Application.Abstractions.IEmailSender
    {
        public Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IReadOnlyList<MorosityUnitDto> Value(ActionResult<IReadOnlyList<MorosityUnitDto>> result) =>
        Assert.IsAssignableFrom<IReadOnlyList<MorosityUnitDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task Sin_corte_devuelve_una_fila_por_unidad_ordenada_por_atraso()
    {
        var rows = Value(await Controller().GetUnits(null, null));

        Assert.Equal(["A", "B"], rows.Select(r => r.UnitCode));
        Assert.Equal(2, rows[0].OverduePeriods);
        Assert.Equal(150_000m, rows[0].TotalBalance);
        Assert.Equal(100, rows[0].DaysOverdue);
        Assert.DoesNotContain(rows, r => r.UnitId == _c.Id);
    }

    [Fact]
    public async Task Con_corte_solo_queda_la_unidad_con_comprobante_viejo()
    {
        var rows = Value(await Controller().GetUnits(_building.Id, Today.AddMonths(-3)));

        var row = Assert.Single(rows);
        Assert.Equal(_a.Id, row.UnitId);
    }

    [Fact]
    public async Task Edificio_no_accesible_es_prohibido()
    {
        var result = await Controller("BuildingManager", accessible: false).GetUnits(_building.Id, null);

        Assert.IsType<ForbidResult>(result.Result);
    }

    public void Dispose() => _t.Dispose();
}
