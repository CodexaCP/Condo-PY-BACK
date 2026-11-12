using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Condo.Tests.VoiceCommands;

public class VoiceCommandsControllerTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly Company _company;
    private readonly Building _building;
    private readonly FakeAccessScope _scope;

    public VoiceCommandsControllerTests()
    {
        _company = _t.AddCompany();
        _building = _t.AddBuilding(_company);
        _scope = new FakeAccessScope(new FakeTenantContext { Role = "BuildingManager" });
        _scope.Buildings.Add(_building.Id);
    }

    private ExpensePeriod AddPeriod(Building building, int year, int month, bool deleted = false)
    {
        var period = new ExpensePeriod
        {
            CompanyId = _company.Id, BuildingId = building.Id, Year = year, Month = month, Name = $"{year}-{month:00}",
            StartDate = new DateOnly(year, month, 1), EndDate = new DateOnly(year, month, 28), DueDate = new DateOnly(year, month, 10),
            Status = ExpensePeriodStatus.Published, IsDeleted = deleted
        };
        _t.Db.ExpensePeriods.Add(period);
        _t.Db.SaveChanges();
        return period;
    }

    private VoiceCommandsController Controller() => new(_t.Db, _scope);

    private async Task<VoiceCommandInterpretationDto> Interpret(string text, Guid? buildingId)
    {
        var result = await Controller().Interpret(text, buildingId, CancellationToken.None);
        return Assert.IsType<VoiceCommandInterpretationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task Periodo_vacio_propone_el_mes_siguiente_al_ultimo_no_borrado()
    {
        AddPeriod(_building, 2026, 9);
        var oct = AddPeriod(_building, 2026, 10);
        AddPeriod(_building, 2026, 11, deleted: true);

        var dto = await Interpret("crear período vacío", _building.Id);

        Assert.True(dto.RequiresConfirmation);
        Assert.Null(dto.Message);
        Assert.Equal(oct.Id, dto.SourcePeriodId);
        Assert.Equal("2026-11", dto.TargetPeriodName);
        Assert.NotNull(dto.ProposedPeriod);
        Assert.Equal(2026, dto.ProposedPeriod!.Year);
        Assert.Equal(11, dto.ProposedPeriod.Month);
        Assert.Equal(new DateOnly(2026, 11, 1), dto.ProposedPeriod.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 28), dto.ProposedPeriod.EndDate);
        Assert.Equal(new DateOnly(2026, 11, 10), dto.ProposedPeriod.DueDate);
    }

    [Fact]
    public async Task Diciembre_pasa_a_enero_del_anio_siguiente()
    {
        AddPeriod(_building, 2026, 12);

        var dto = await Interpret("crear período vacío", _building.Id);

        Assert.Equal("2027-01", dto.TargetPeriodName);
        Assert.Equal(2027, dto.ProposedPeriod!.Year);
        Assert.Equal(1, dto.ProposedPeriod.Month);
    }

    [Fact]
    public async Task Clonar_apunta_al_ultimo_periodo_y_no_propone_cuerpo()
    {
        var oct = AddPeriod(_building, 2026, 10);

        var dto = await Interpret("crear período igual al anterior", _building.Id);

        Assert.True(dto.RequiresConfirmation);
        Assert.Equal(oct.Id, dto.SourcePeriodId);
        Assert.Null(dto.ProposedPeriod);
    }

    [Fact]
    public async Task Periodo_sin_edificio_pide_elegir_uno()
    {
        var dto = await Interpret("crear período vacío", null);

        Assert.StartsWith("Elegí un edificio", dto.Message);
        Assert.False(dto.RequiresConfirmation);
    }

    [Fact]
    public async Task Edificio_sin_periodos_avisa()
    {
        var dto = await Interpret("crear período vacío", _building.Id);

        Assert.Contains("todavía no tiene períodos", dto.Message);
    }

    [Fact]
    public async Task Edificio_no_accesible_es_prohibido()
    {
        var other = _t.AddBuilding(_company);

        var result = await Controller().Interpret("crear período vacío", other.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Texto_vacio_es_solicitud_invalida()
    {
        var result = await Controller().Interpret("   ", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Morosos_con_edificio_resume_antiguedad_y_nombre()
    {
        var dto = await Interpret("morosos con 3 meses de atraso", _building.Id);

        Assert.Contains("3 meses o más", dto.Summary);
        Assert.Contains(_building.Name, dto.Summary);
        Assert.False(dto.RequiresConfirmation);
    }

    public void Dispose() => _t.Dispose();
}
