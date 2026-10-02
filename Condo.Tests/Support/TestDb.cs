using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Support;

/// <summary>
/// Base SQLite en memoria con el modelo REAL de CondoDbContext. A diferencia de EF InMemory, SQLite hace cumplir los
/// indices unicos filtrados y las claves foraneas, que son justo las garantias que el marketplace pone en la base.
/// </summary>
internal sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CondoDbContext> _options;
    private int _seq;

    public CondoDbContext Db { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CondoDbContext>().UseSqlite(_connection).Options;
        Db = new SqliteCondoDbContext(_options);
        Db.Database.EnsureCreated();
    }

    // Otro contexto sobre la misma base: sirve para comprobar que algo quedo realmente guardado (sin cache de EF).
    public CondoDbContext NewContext() => new SqliteCondoDbContext(_options);

    private int Next() => ++_seq;

    public Company AddCompany(string? name = null)
    {
        var n = Next();
        var company = new Company { Name = name ?? $"Empresa {n}", Slug = $"empresa-{n}-{Guid.NewGuid():N}" };
        Db.Companies.Add(company);
        Db.SaveChanges();
        return company;
    }

    public Building AddBuilding(Company company, string? name = null)
    {
        var n = Next();
        var building = new Building { CompanyId = company.Id, Name = name ?? $"Edificio {n}", Code = $"E{n}" };
        Db.Buildings.Add(building);
        Db.SaveChanges();
        return building;
    }

    public Unit AddUnit(Building building, string? code = null)
    {
        var n = Next();
        var unit = new Unit
        {
            CompanyId = building.CompanyId!.Value,
            BuildingId = building.Id,
            Code = code ?? $"U{n}",
            Floor = "1",
            Coefficient = 1m
        };
        Db.Units.Add(unit);
        Db.SaveChanges();
        return unit;
    }

    public ApplicationUser AddUser(Company? company, UserRole role = UserRole.Owner, string? name = null)
    {
        var n = Next();
        var user = new ApplicationUser
        {
            CompanyId = company?.Id,
            FirstName = name ?? $"Usuario{n}",
            LastName = "Prueba",
            FullName = $"{name ?? $"Usuario{n}"} Prueba",
            Username = $"user{n}-{Guid.NewGuid():N}",
            Email = $"user{n}-{Guid.NewGuid():N}@test.local",
            Role = role
        };
        Db.ApplicationUsers.Add(user);
        Db.SaveChanges();
        return user;
    }

    public UnitOwner AddOwner(Unit unit, ApplicationUser user, bool primary = true)
    {
        var link = new UnitOwner { CompanyId = unit.CompanyId, UnitId = unit.Id, OwnerId = user.Id, IsPrimary = primary };
        Db.UnitOwners.Add(link);
        Db.SaveChanges();
        return link;
    }

    public UnitResident AddResident(Unit unit, ApplicationUser user, DateOnly? endDate = null)
    {
        var resident = new Resident
        {
            CompanyId = unit.CompanyId,
            FullName = user.FullName,
            DocumentNumber = $"D{Next()}",
            Email = user.Email,
            PhoneNumber = "0",
            ApplicationUserId = user.Id
        };
        Db.Residents.Add(resident);
        var link = new UnitResident { CompanyId = unit.CompanyId, UnitId = unit.Id, ResidentId = resident.Id, EndDate = endDate };
        Db.UnitResidents.Add(link);
        Db.SaveChanges();
        return link;
    }

    /// <summary>Asigna al edificio un plan vigente; con includesMarketplace el modulo puede habilitarse.</summary>
    public void AssignPlan(Building building, ApplicationUser assignedBy, bool includesMarketplace)
    {
        var plan = new Plan
        {
            Name = $"Plan {Next()}",
            Description = "Plan de prueba",
            BillingCycle = BillingCycle.Monthly,
            IncludesMarketplace = includesMarketplace
        };
        Db.Plans.Add(plan);
        Db.BuildingPlans.Add(new BuildingPlan
        {
            PlanId = plan.Id,
            BuildingId = building.Id,
            ScopeEntityId = building.Id,
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(300),
            AssignedById = assignedBy.Id
        });
        Db.SaveChanges();
    }

    public void EnableMarketplace(Building building, bool enabled = true)
    {
        building.MarketplaceEnabled = enabled;
        Db.SaveChanges();
    }

    public MarketplaceListing AddListing(Building building, Unit unit, ApplicationUser owner, decimal hourlyPrice = 20_000m)
    {
        var start = new DateTime(2030, 1, 1, 19, 0, 0, DateTimeKind.Utc);
        var listing = new MarketplaceListing
        {
            CompanyId = building.CompanyId!.Value,
            BuildingId = building.Id,
            UnitId = unit.Id,
            OwnerId = owner.Id,
            Title = "Cochera 12",
            WindowStartUtc = start,
            WindowEndUtc = start.AddHours(10),
            HourlyPrice = hourlyPrice
        };
        Db.MarketplaceListings.Add(listing);
        Db.SaveChanges();
        return listing;
    }

    public MarketplaceReservation NewReservation(
        MarketplaceListing listing, ApplicationUser buyer, string? reference = null,
        MarketplaceReservationStatus status = MarketplaceReservationStatus.PendingPayment)
    {
        var n = Next();
        return new MarketplaceReservation
        {
            CompanyId = listing.CompanyId,
            ListingId = listing.Id,
            BuildingId = listing.BuildingId,
            UnitId = listing.UnitId,
            BuyerUserId = buyer.Id,
            OwnerId = listing.OwnerId,
            Reference = reference ?? $"MP-{n:D8}",
            StartsAtUtc = listing.WindowStartUtc,
            EndsAtUtc = listing.WindowStartUtc.AddHours(3),
            Hours = 3,
            HourlyPrice = 20_000m,
            BaseAmount = 60_000m,
            CommissionPercent = 10m,
            CommissionAmount = 6_000m,
            TotalAmount = 66_000m,
            OwnerNetAmount = 60_000m,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };
    }

    public MarketplaceReservationSlot NewSlot(MarketplaceReservation reservation, DateTime slotStartUtc) => new()
    {
        CompanyId = reservation.CompanyId,
        ReservationId = reservation.Id,
        ListingId = reservation.ListingId,
        SlotStartUtc = slotStartUtc
    };

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
