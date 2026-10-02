using Condo.Api.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;

namespace Condo.Tests.Marketplace;

/// <summary>
/// Aislamiento del marketplace: el edificio que manda el cliente nunca se confia; se valida contra la relacion real del
/// usuario (propietario/residente, o personal con alcance), su empresa y que el modulo este habilitado y en el plan.
/// </summary>
public class MarketplaceScopeTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly FakeAccessScope _access;

    private readonly Company _companyX;
    private readonly Building _buildingA;     // empresa X, modulo habilitado
    private readonly Building _buildingB;     // empresa X, otro edificio (tambien habilitado)
    private readonly Unit _unitA;
    private readonly Unit _unitB;
    private readonly ApplicationUser _admin;

    public MarketplaceScopeTests()
    {
        _access = new FakeAccessScope(_tenant);
        _companyX = _t.AddCompany("Empresa X");
        _admin = _t.AddUser(_companyX, UserRole.CompanyAdmin);
        _buildingA = _t.AddBuilding(_companyX, "Edificio A");
        _buildingB = _t.AddBuilding(_companyX, "Edificio B");
        _unitA = _t.AddUnit(_buildingA, "302");
        _unitB = _t.AddUnit(_buildingB, "101");

        foreach (var building in new[] { _buildingA, _buildingB })
        {
            _t.AssignPlan(building, _admin, includesMarketplace: true);
            _t.EnableMarketplace(building);
        }
    }

    public void Dispose() => _t.Dispose();

    private MarketplaceScope Scope()
    {
        var gate = new MarketplaceModuleGate(_t.NewContext());
        return new MarketplaceScope(_t.NewContext(), _tenant, _access, gate);
    }

    private ApplicationUser Login(ApplicationUser user, string role, Company? company = null)
    {
        _tenant.UserId = user.Id;
        _tenant.Role = role;
        _tenant.CompanyId = company?.Id ?? user.CompanyId;
        return user;
    }

    // ── Propietarios y residentes ────────────────────────────────────────────

    [Fact]
    public async Task Un_propietario_accede_al_marketplace_de_su_edificio()
    {
        var owner = Login(_t.AddUser(_companyX), "Owner");
        _t.AddOwner(_unitA, owner);

        var access = await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None);

        Assert.True(access.Allowed);
        Assert.Equal(_buildingA.Id, access.Context!.BuildingId);
        Assert.Equal(_companyX.Id, access.Context.CompanyId);
        Assert.True(access.Context.IsMember);
        Assert.False(access.Context.IsStaff);
    }

    [Fact]
    public async Task Un_residente_vigente_accede_al_marketplace_de_su_edificio()
    {
        var resident = Login(_t.AddUser(_companyX, UserRole.Resident), "Resident");
        _t.AddResident(_unitA, resident);

        Assert.True((await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task Un_residente_que_ya_se_mudo_no_accede()
    {
        var resident = Login(_t.AddUser(_companyX, UserRole.Resident), "Resident");
        _t.AddResident(_unitA, resident, endDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));

        var access = await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None);

        Assert.True(access.IsNotFound);
    }

    [Fact]
    public async Task Un_usuario_de_un_edificio_no_accede_a_otro_edificio_de_la_misma_empresa()
    {
        var owner = Login(_t.AddUser(_companyX), "Owner");
        _t.AddOwner(_unitA, owner);

        var access = await Scope().ResolveBuildingAsync(_buildingB.Id, CancellationToken.None);

        Assert.False(access.Allowed);
        Assert.True(access.IsNotFound);
    }

    [Fact]
    public async Task Un_usuario_de_la_empresa_Y_no_accede_a_un_edificio_de_la_empresa_X()
    {
        var companyY = _t.AddCompany("Empresa Y");
        var buildingY = _t.AddBuilding(companyY);
        var unitY = _t.AddUnit(buildingY);
        _t.AssignPlan(buildingY, _t.AddUser(companyY, UserRole.CompanyAdmin), includesMarketplace: true);
        _t.EnableMarketplace(buildingY);
        var userY = Login(_t.AddUser(companyY), "Owner");
        _t.AddOwner(unitY, userY);

        // Pide el edificio A de la empresa X mandando su id a mano.
        var access = await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None);

        Assert.True(access.IsNotFound);
    }

    [Fact]
    public async Task Aunque_tenga_la_relacion_si_el_token_dice_otra_empresa_no_accede()
    {
        var companyY = _t.AddCompany("Empresa Y");
        var owner = Login(_t.AddUser(_companyX), "Owner", companyY);
        _t.AddOwner(_unitA, owner);

        var access = await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None);

        Assert.True(access.IsNotFound);
    }

    [Fact]
    public async Task Un_usuario_sin_relacion_con_el_edificio_no_accede_y_el_mensaje_no_revela_que_existe()
    {
        Login(_t.AddUser(_companyX), "Owner");

        var existing = await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None);
        var missing = await Scope().ResolveBuildingAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(missing.ErrorCode, existing.ErrorCode);
        Assert.Equal(missing.Message, existing.Message);
    }

    [Fact]
    public async Task Un_propietario_cuya_relacion_fue_dada_de_baja_no_accede()
    {
        var owner = Login(_t.AddUser(_companyX), "Owner");
        var link = _t.AddOwner(_unitA, owner);
        link.IsDeleted = true;
        _t.Db.SaveChanges();

        Assert.True((await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None)).IsNotFound);
    }

    [Fact]
    public async Task Sin_edificio_se_pide_el_edificio()
    {
        Login(_t.AddUser(_companyX), "Owner");

        var access = await Scope().ResolveBuildingAsync(Guid.Empty, CancellationToken.None);

        Assert.Equal(MarketplaceScope.BuildingRequiredCode, access.ErrorCode);
    }

    // ── Modulo habilitado y plan ─────────────────────────────────────────────

    [Fact]
    public async Task Con_el_modulo_apagado_responde_modulo_deshabilitado()
    {
        var owner = Login(_t.AddUser(_companyX), "Owner");
        _t.AddOwner(_unitA, owner);
        _t.EnableMarketplace(_buildingA, enabled: false);

        var access = await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None);

        Assert.False(access.Allowed);
        Assert.Equal(MarketplaceModuleGate.DisabledCode, access.ErrorCode);
    }

    [Fact]
    public async Task Con_el_modulo_encendido_pero_un_plan_que_no_lo_incluye_responde_plan_sin_marketplace()
    {
        var companyZ = _t.AddCompany("Empresa Z");
        var buildingZ = _t.AddBuilding(companyZ);
        var unitZ = _t.AddUnit(buildingZ);
        _t.AssignPlan(buildingZ, _t.AddUser(companyZ, UserRole.CompanyAdmin), includesMarketplace: false);
        _t.EnableMarketplace(buildingZ);
        var owner = Login(_t.AddUser(companyZ), "Owner");
        _t.AddOwner(unitZ, owner);

        var access = await Scope().ResolveBuildingAsync(buildingZ.Id, CancellationToken.None);

        Assert.Equal(MarketplaceModuleGate.PlanNotIncludedCode, access.ErrorCode);
    }

    // ── Personal ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_encargado_accede_solo_a_los_edificios_de_su_alcance()
    {
        var manager = Login(_t.AddUser(_companyX, UserRole.BuildingManager), "BuildingManager");
        _access.Buildings.Add(_buildingA.Id);

        var inScope = await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None);
        var outOfScope = await Scope().ResolveBuildingAsync(_buildingB.Id, CancellationToken.None);

        Assert.True(inScope.Allowed);
        Assert.True(inScope.Context!.IsStaff);
        Assert.False(inScope.Context.IsMember);
        Assert.True(outOfScope.IsNotFound);
    }

    [Fact]
    public async Task El_personal_de_otra_empresa_no_accede_aunque_tenga_el_edificio_en_su_alcance_por_error()
    {
        var companyY = _t.AddCompany("Empresa Y");
        Login(_t.AddUser(companyY, UserRole.BuildingManager), "BuildingManager");
        _access.Buildings.Add(_buildingA.Id);

        Assert.True((await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None)).IsNotFound);
    }

    [Fact]
    public async Task El_SuperAdmin_accede_a_cualquier_edificio_habilitado()
    {
        _tenant.Role = "SuperAdmin";
        _tenant.CompanyId = null;

        Assert.True((await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None)).Allowed);
        Assert.True((await Scope().ResolveBuildingAsync(_buildingB.Id, CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task Un_rol_de_usuario_final_no_gana_acceso_de_personal()
    {
        // Aunque el alcance del personal tuviera el edificio, un Owner solo entra por su relacion real.
        Login(_t.AddUser(_companyX), "Owner");
        _access.Buildings.Add(_buildingA.Id);

        Assert.True((await Scope().ResolveBuildingAsync(_buildingA.Id, CancellationToken.None)).IsNotFound);
    }

    // ── Titularidad: solo el propietario principal publica ───────────────────

    [Fact]
    public async Task Solo_el_propietario_principal_puede_publicar_su_unidad()
    {
        var juan = _t.AddUser(_companyX, name: "Juan");
        _t.AddOwner(_unitA, juan, primary: true);

        var units = await Scope().GetPrimaryOwnedUnitsAsync(juan.Id, _buildingA.Id, CancellationToken.None);

        Assert.Single(units);
        Assert.Equal("302", units[0].Code);
        Assert.True(await Scope().IsPrimaryOwnerOfUnitAsync(juan.Id, _unitA.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Un_residente_que_no_es_propietario_no_puede_publicar_la_unidad()
    {
        var pedro = _t.AddUser(_companyX, UserRole.Resident, "Pedro");
        _t.AddResident(_unitA, pedro);

        Assert.Empty(await Scope().GetPrimaryOwnedUnitsAsync(pedro.Id, _buildingA.Id, CancellationToken.None));
        Assert.False(await Scope().IsPrimaryOwnerOfUnitAsync(pedro.Id, _unitA.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Un_copropietario_que_no_es_el_principal_no_puede_publicar()
    {
        var principal = _t.AddUser(_companyX);
        var copropietario = _t.AddUser(_companyX);
        _t.AddOwner(_unitA, principal, primary: true);
        _t.AddOwner(_unitA, copropietario, primary: false);

        Assert.Empty(await Scope().GetPrimaryOwnedUnitsAsync(copropietario.Id, _buildingA.Id, CancellationToken.None));
        Assert.True(await Scope().IsPrimaryOwnerOfUnitAsync(principal.Id, _unitA.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Si_Pedro_pasa_a_ser_el_propietario_principal_sus_permisos_se_derivan_solos()
    {
        var juan = _t.AddUser(_companyX, name: "Juan");
        var pedro = _t.AddUser(_companyX, UserRole.Resident, "Pedro");
        var juanLink = _t.AddOwner(_unitA, juan, primary: true);

        Assert.Empty(await Scope().GetPrimaryOwnedUnitsAsync(pedro.Id, _buildingA.Id, CancellationToken.None));

        // La unidad cambia de manos: Juan deja de ser el principal y Pedro pasa a serlo.
        juanLink.IsPrimary = false;
        _t.AddOwner(_unitA, pedro, primary: true);

        Assert.Single(await Scope().GetPrimaryOwnedUnitsAsync(pedro.Id, _buildingA.Id, CancellationToken.None));
        Assert.Empty(await Scope().GetPrimaryOwnedUnitsAsync(juan.Id, _buildingA.Id, CancellationToken.None));
    }

    [Fact]
    public async Task El_propietario_principal_no_puede_publicar_una_unidad_de_otro_edificio()
    {
        var juan = _t.AddUser(_companyX);
        _t.AddOwner(_unitA, juan, primary: true);

        // Pide las unidades del edificio B: solo se listan las del edificio indicado.
        Assert.Empty(await Scope().GetPrimaryOwnedUnitsAsync(juan.Id, _buildingB.Id, CancellationToken.None));
        Assert.False(await Scope().IsPrimaryOwnerOfUnitAsync(juan.Id, _unitB.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Los_edificios_del_usuario_combinan_propiedad_y_residencia()
    {
        var user = Login(_t.AddUser(_companyX), "Owner");
        _t.AddOwner(_unitA, user);
        _t.AddResident(_unitB, user);

        var ids = await Scope().GetMemberBuildingIdsAsync(CancellationToken.None);

        Assert.Equal(new HashSet<Guid> { _buildingA.Id, _buildingB.Id }, ids);
    }

    // ── Edificios con el marketplace disponible (acceso en la app) ───────────

    [Fact]
    public async Task El_acceso_lista_solo_los_edificios_del_usuario_con_el_modulo_disponible()
    {
        var user = Login(_t.AddUser(_companyX), "Owner");
        _t.AddOwner(_unitA, user, primary: true);
        _t.AddResident(_unitB, user);
        _t.EnableMarketplace(_buildingB, enabled: false);

        var buildings = await Scope().GetAvailableBuildingsAsync(CancellationToken.None);

        var only = Assert.Single(buildings);
        Assert.Equal(_buildingA.Id, only.BuildingId);
        Assert.Equal("Edificio A", only.BuildingName);
        Assert.True(only.CanPublish);
    }

    [Fact]
    public async Task Un_residente_ve_el_edificio_pero_no_puede_publicar()
    {
        var resident = Login(_t.AddUser(_companyX, UserRole.Resident), "Resident");
        _t.AddResident(_unitA, resident);

        var only = Assert.Single(await Scope().GetAvailableBuildingsAsync(CancellationToken.None));

        Assert.False(only.CanPublish);
    }

    [Fact]
    public async Task Un_copropietario_no_principal_no_puede_publicar()
    {
        Login(_t.AddUser(_companyX), "Owner");
        var user = _t.Db.ApplicationUsers.OrderByDescending(x => x.CreatedAtUtc).First();
        _t.AddOwner(_unitA, user, primary: false);

        Assert.False(Assert.Single(await Scope().GetAvailableBuildingsAsync(CancellationToken.None)).CanPublish);
    }

    [Fact]
    public async Task Sin_relacion_con_ningun_edificio_el_acceso_queda_vacio()
    {
        Login(_t.AddUser(_companyX), "Owner");

        Assert.Empty(await Scope().GetAvailableBuildingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Un_edificio_con_plan_sin_marketplace_no_aparece_en_el_acceso()
    {
        var companyZ = _t.AddCompany("Empresa Z");
        var buildingZ = _t.AddBuilding(companyZ);
        var unitZ = _t.AddUnit(buildingZ);
        _t.AssignPlan(buildingZ, _t.AddUser(companyZ, UserRole.CompanyAdmin), includesMarketplace: false);
        _t.EnableMarketplace(buildingZ);
        var owner = Login(_t.AddUser(companyZ), "Owner");
        _t.AddOwner(unitZ, owner);

        Assert.Empty(await Scope().GetAvailableBuildingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Si_el_token_dice_otra_empresa_el_acceso_queda_vacio()
    {
        var companyY = _t.AddCompany("Empresa Y");
        var owner = Login(_t.AddUser(_companyX), "Owner", companyY);
        _t.AddOwner(_unitA, owner);

        Assert.Empty(await Scope().GetAvailableBuildingsAsync(CancellationToken.None));
    }
}
