using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Condo.Tests.Finance;

// Cobranza y Morosidad con datos de varios años y periodos: filtrar por año o tener un periodo borrado con cargos vivos no puede romper
// el reporte (antes devolvia 500 y el navegador lo mostraba como "Failed to fetch").
public class CollectionsAndMorosityReportTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly Company _company;
    private readonly Building _building;
    private readonly Unit _unit;
    private readonly ExpensePeriod _dec2025;
    private readonly ExpensePeriod _jan2026;
    private readonly FakeAccessScope _scope;

    public CollectionsAndMorosityReportTests()
    {
        _company = _t.AddCompany();
        _building = _t.AddBuilding(_company);
        _unit = _t.AddUnit(_building, "101");
        _scope = new FakeAccessScope(new FakeTenantContext { Role = "SuperAdmin" });
        _scope.Buildings.Add(_building.Id);

        _dec2025 = AddPeriod(2025, 12, ExpensePeriodStatus.Published);
        _jan2026 = AddPeriod(2026, 1, ExpensePeriodStatus.Published);
        AddCharge(_dec2025, 100_000m);
        AddCharge(_jan2026, 200_000m);
        AddPayment(_dec2025, 40_000m);
        AddPayment(_jan2026, 50_000m);
    }

    private ExpensePeriod AddPeriod(int year, int month, ExpensePeriodStatus status, bool deleted = false)
    {
        var period = new ExpensePeriod
        {
            CompanyId = _company.Id, BuildingId = _building.Id, Year = year, Month = month, Name = $"{month:00}/{year}",
            StartDate = new DateOnly(year, month, 1), EndDate = new DateOnly(year, month, 28), DueDate = new DateOnly(year, month, 10),
            Status = status, IsDeleted = deleted
        };
        _t.Db.ExpensePeriods.Add(period);
        _t.Db.SaveChanges();
        return period;
    }

    private void AddCharge(ExpensePeriod period, decimal amount)
    {
        _t.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = _company.Id, ExpensePeriodId = period.Id, UnitId = _unit.Id, Concept = "Expensa", Amount = amount
        });
        _t.Db.SaveChanges();
    }

    private void AddPayment(ExpensePeriod period, decimal amount)
    {
        _t.Db.Payments.Add(new Payment
        {
            CompanyId = _company.Id, ExpensePeriodId = period.Id, UnitId = _unit.Id, Amount = amount, PaymentDate = new DateOnly(2026, 1, 5)
        });
        _t.Db.SaveChanges();
    }

    private CollectionsController Collections() => new(_t.Db, _scope);

    private MorosityController Morosity() => new(_t.Db, _scope, new NoEmail());

    private sealed class NoEmail : Condo.Application.Abstractions.IEmailSender
    {
        public Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static T Value<T>(ActionResult<T> result) => Assert.IsType<OkObjectResult>(result.Result).Value is T v ? v : throw new InvalidCastException();

    [Fact]
    public async Task Cobranza_filtrada_por_anio_solo_cuenta_los_periodos_de_ese_anio()
    {
        var report = Value(await Collections().GetReport(null, 2026, null, null, CancellationToken.None));

        var item = Assert.Single(report.Items);
        Assert.Equal(_jan2026.Id, item.ExpensePeriodId);
        Assert.Equal(200_000m, item.TotalChargedAmount);
        Assert.Equal(50_000m, item.TotalCollectedAmount);
        Assert.Equal(150_000m, item.PendingAmount);
        Assert.Equal(200_000m, report.Summary.TotalChargedAmount);   // lo de diciembre 2025 no entra
    }

    [Fact]
    public async Task Cobranza_sin_filtros_muestra_todos_los_periodos()
    {
        var report = Value(await Collections().GetReport(null, null, null, null, CancellationToken.None));

        Assert.Equal(2, report.Items.Count);
        Assert.Equal(300_000m, report.Summary.TotalChargedAmount);
        Assert.Equal(90_000m, report.Summary.TotalCollectedAmount);
    }

    [Fact]
    public async Task Cobranza_por_meses_no_se_rompe_con_cargos_de_otros_meses()
    {
        AddCharge(AddPeriod(2026, 3, ExpensePeriodStatus.Published), 10_000m);

        var report = Value(await Collections().GetReport(null, 2026, 1, 2, CancellationToken.None));

        Assert.Equal([_jan2026.Id], report.Items.Select(i => i.ExpensePeriodId));
    }

    [Fact]
    public async Task Cobranza_ignora_un_periodo_borrado_que_todavia_tiene_cargos()
    {
        var deleted = AddPeriod(2026, 2, ExpensePeriodStatus.Published, deleted: true);
        AddCharge(deleted, 999_000m);
        AddPayment(deleted, 1_000m);

        var report = Value(await Collections().GetReport(null, null, null, null, CancellationToken.None));

        Assert.Equal(2, report.Items.Count);
        Assert.Equal(300_000m, report.Summary.TotalChargedAmount);
    }

    [Fact]
    public async Task Cobranza_con_un_edificio_elegido_y_otro_anio_responde_aunque_no_haya_periodos()
    {
        var report = Value(await Collections().GetReport(_building.Id, 2024, null, null, CancellationToken.None));

        Assert.Empty(report.Items);
        Assert.Equal(0m, report.Summary.TotalChargedAmount);
    }

    [Fact]
    public async Task Morosidad_responde_con_periodos_vencidos_borrados_y_de_varios_anios()
    {
        var deleted = AddPeriod(2026, 2, ExpensePeriodStatus.Published, deleted: true);
        AddCharge(deleted, 50_000m);

        var report = Value(await Morosity().GetReport(null, null, null, null, null));

        Assert.True(report.Items.Count >= 2);
        Assert.Contains(report.Items, i => i.ExpensePeriodId == _dec2025.Id && i.Balance == 60_000m);
        Assert.Contains(report.Items, i => i.ExpensePeriodId == _jan2026.Id && i.Balance == 150_000m);
    }

    [Fact]
    public async Task Morosidad_con_propietario_residente_busqueda_y_antiguedad_responde_para_el_administrador_de_empresa()
    {
        var owner = _t.AddUser(_company, UserRole.Owner, "Ana");
        _t.AddOwner(_unit, owner);
        _t.AddResident(_unit, _t.AddUser(_company, UserRole.Resident, "Luis"));
        var admin = new FakeAccessScope(new FakeTenantContext { Role = "CompanyAdmin", CompanyId = _company.Id });
        admin.Buildings.Add(_building.Id);
        var controller = new MorosityController(_t.Db, admin, new NoEmail());

        var all = Value(await controller.GetReport(null, null, null, null, null));
        Assert.Contains(all.Items, i => i.OwnerName.StartsWith("Ana"));

        var searched = Value(await controller.GetReport(_building.Id, _unit.Id, _jan2026.Id, "ana", "+90"));
        Assert.NotNull(searched.Summary);

        var page = Value(await controller.GetReport(null, null, null, null, null, page: 99, pageSize: 5000));
        Assert.Empty(page.Items);
    }

    public void Dispose() => _t.Dispose();
}
