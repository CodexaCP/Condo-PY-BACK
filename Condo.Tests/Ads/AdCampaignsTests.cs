using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Ads;

// Campañas de publicidad: el SuperAdmin las crea y las edita; cada pedido usa su propio contexto (como en la API real).
public class AdCampaignsTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly Company _company;
    private readonly Building _a;
    private readonly Building _b;
    private readonly ApplicationUser _super;

    public AdCampaignsTests()
    {
        _company = _t.AddCompany();
        _a = _t.AddBuilding(_company, "A");
        _b = _t.AddBuilding(_company, "B");
        _super = _t.AddUser(null, UserRole.SuperAdmin, "Super");
        _tenant.UserId = _super.Id;
        _tenant.Role = "SuperAdmin";
    }

    public void Dispose() => _t.Dispose();

    private AdCampaignsController Api() => new(_t.NewContext(), _tenant, new StubWebHostEnvironment(Path.GetTempPath()));

    private void Enable(Building building)
    {
        using var db = _t.NewContext();
        db.Buildings.Single(x => x.Id == building.Id).AdsEnabled = true;
        db.SaveChanges();
    }

    private AdCampaignCreateRequest NewRequest(params Guid[] buildingIds) => new()
    {
        CompanyId = _company.Id,
        AdvertiserName = "Pizzeria",
        CtaText = "Pedir",
        ImageUrl = "/uploads/ad-banners/x.png",
        Category = "Gastronomia",
        Position = 1,
        StartDate = new DateTime(2026, 10, 1),
        EndDate = new DateTime(2026, 12, 31),
        BuildingIds = [.. buildingIds]
    };

    private static AdCampaignUpdateRequest ToUpdate(AdCampaignDto c) => new()
    {
        AdvertiserName = c.AdvertiserName, Description = c.Description, CtaText = c.CtaText, CtaUrl = c.CtaUrl, ImageUrl = c.ImageUrl,
        Category = c.Category, Position = c.Position, StartDate = c.StartDate, EndDate = c.EndDate, MonthlyAmount = c.MonthlyAmount,
        IsActive = c.IsActive, NotifyBeforeExpiry = c.NotifyBeforeExpiry, BuildingIds = [.. c.BuildingIds]
    };

    private async Task<AdCampaignDto> CreateAsync(params Building[] buildings)
    {
        var result = await Api().Create(NewRequest(buildings.Select(x => x.Id).ToArray()), CancellationToken.None);
        return (AdCampaignDto)((CreatedAtActionResult)result.Result!).Value!;
    }

    [Fact]
    public async Task Editar_la_campana_conservando_los_mismos_edificios_guarda_los_cambios()
    {
        Enable(_a);
        var created = await CreateAsync(_a);

        var request = ToUpdate(created);
        request.AdvertiserName = "Pizzeria Nueva";
        request.Description = "Con descripcion";
        var result = await Api().Update(created.Id, request, CancellationToken.None);

        var dto = (AdCampaignDto)((OkObjectResult)result.Result!).Value!;
        Assert.Equal("Pizzeria Nueva", dto.AdvertiserName);

        using var db = _t.NewContext();
        var saved = db.AdCampaigns.Include(x => x.Buildings).Single(x => x.Id == created.Id);
        Assert.Equal("Pizzeria Nueva", saved.AdvertiserName);
        Assert.Equal("Con descripcion", saved.Description);
        Assert.Equal([_a.Id], saved.Buildings.Select(x => x.BuildingId).ToArray());
    }

    [Fact]
    public async Task Editar_la_campana_cambiando_de_edificios_guarda_los_cambios()
    {
        Enable(_a);
        Enable(_b);
        var created = await CreateAsync(_a);

        var request = ToUpdate(created);
        request.BuildingIds = [_a.Id, _b.Id];
        var result = await Api().Update(created.Id, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        using var db = _t.NewContext();
        Assert.Equal(2, db.AdCampaignBuildings.Count(x => x.AdCampaignId == created.Id));
    }

    [Fact]
    public async Task No_se_puede_asignar_un_edificio_con_la_publicidad_apagada()
    {
        var result = await Api().Create(NewRequest(_a.Id), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Quitar_un_edificio_de_la_campana_lo_saca_del_resultado_y_de_la_base()
    {
        Enable(_a);
        Enable(_b);
        var created = await CreateAsync(_a, _b);

        var request = ToUpdate(created);
        request.BuildingIds = [_b.Id];
        var result = await Api().Update(created.Id, request, CancellationToken.None);

        var dto = (AdCampaignDto)((OkObjectResult)result.Result!).Value!;
        Assert.Equal([_b.Id], dto.BuildingIds.ToArray());
        using var db = _t.NewContext();
        Assert.Equal([_b.Id], db.AdCampaignBuildings.Where(x => x.AdCampaignId == created.Id).Select(x => x.BuildingId).ToArray());
    }

    [Fact]
    public async Task Pausar_y_reactivar_la_campana_guarda_el_estado()
    {
        Enable(_a);
        var created = await CreateAsync(_a);

        var paused = (AdCampaignDto)((OkObjectResult)(await Api().Toggle(created.Id, CancellationToken.None)).Result!).Value!;
        Assert.False(paused.IsActive);
        using var db = _t.NewContext();
        Assert.False(db.AdCampaigns.Single(x => x.Id == created.Id).IsActive);
    }
}
