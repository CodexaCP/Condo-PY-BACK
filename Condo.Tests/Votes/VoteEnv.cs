using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Condo.Tests.Votes;

/// <summary>
/// Edificio con 4 unidades (coeficiente 0,25 cada una), un propietario principal en la primera, y personal: Encargado y Operador con
/// acceso al edificio, Administrador de la empresa. Otro edificio de la misma empresa con su propio Encargado.
/// </summary>
internal sealed class VoteEnv : IDisposable
{
    public TestDb T { get; }
    public Company Company { get; }
    public Building Building { get; }
    public Building OtherBuilding { get; }
    public List<Unit> Units { get; }
    public ApplicationUser Manager { get; }
    public ApplicationUser Operator { get; }
    public ApplicationUser Admin { get; }
    public ApplicationUser OtherManager { get; }
    public ApplicationUser Owner { get; }

    public VoteEnv(bool useSqlServer = false)
    {
        T = new TestDb(useSqlServer);
        Company = T.AddCompany();
        Building = T.AddBuilding(Company);
        OtherBuilding = T.AddBuilding(Company);
        Units = Enumerable.Range(1, 4).Select(i => T.AddUnit(Building, $"U{i}")).ToList();
        foreach (var u in Units) u.Coefficient = 0.25m;
        T.Db.SaveChanges();

        Manager = T.AddUser(Company, UserRole.BuildingManager, "Encargado");
        Operator = T.AddUser(Company, UserRole.CompanyOperator, "Operador");
        Admin = T.AddUser(Company, UserRole.CompanyAdmin, "Admin");
        OtherManager = T.AddUser(Company, UserRole.BuildingManager, "OtroEncargado");
        Owner = T.AddUser(Company, UserRole.Owner, "Propietario");
        T.AddBuildingAccess(Manager, Building);
        T.AddBuildingAccess(Operator, Building);
        T.AddBuildingAccess(OtherManager, OtherBuilding);
        T.AddOwner(Units[0], Owner);
    }

    /// <summary>El servicio visto por un usuario con su rol; el acceso a edificios sale de UserBuildingAccesses (o toda la empresa).</summary>
    public VoteService As(ApplicationUser user, CondoDbContext? db = null)
    {
        db ??= T.Db;
        var tenant = new FakeTenantContext { Role = user.Role.ToString(), UserId = user.Id, CompanyId = user.CompanyId };
        var scope = new FakeAccessScope(tenant);
        if (user.Role == UserRole.CompanyAdmin)
        {
            scope.Buildings.Add(Building.Id);
            scope.Buildings.Add(OtherBuilding.Id);
        }
        foreach (var access in db.UserBuildingAccesses.Where(x => x.ApplicationUserId == user.Id && !x.IsDeleted))
            scope.Buildings.Add(access.BuildingId);
        return new VoteService(db, scope, tenant, new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));
    }

    public VoteUpsertRequest Request(string weight = "ByUnit", string rule = "Simple", params string[] options) => new()
    {
        BuildingId = Building.Id,
        Title = "Pintura de la fachada",
        QuorumPercentage = 50,
        WeightType = weight,
        MajorityRule = rule,
        OptionLabels = options.Length > 0 ? options.ToList() : ["A favor", "En contra"]
    };

    /// <summary>Crea y abre una votacion como Encargado.</summary>
    public async Task<VoteDto> OpenVoteAsync(string weight = "ByUnit", string rule = "Simple")
    {
        var service = As(Manager);
        var created = Ok(await service.CreateAsync(Request(weight, rule), CancellationToken.None));
        return Ok(await service.OpenAsync(created.Id, CancellationToken.None));
    }

    public static T Ok<T>(VoteResult<T> result)
    {
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    public static int Status<T>(VoteResult<T> result)
    {
        Assert.False(result.Ok, "Se esperaba un error.");
        return result.Error!.StatusCode;
    }

    public void Dispose() => T.Dispose();
}
