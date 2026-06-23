using Condo.Application.Abstractions;
using Condo.Domain.Common;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Infrastructure.Persistence;

public class CondoDbContext(DbContextOptions<CondoDbContext> options) : DbContext(options), ICondoDbContext
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Condominium> Condominiums => Set<Condominium>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<UserBuildingAccess> UserBuildingAccesses => Set<UserBuildingAccess>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<BuildingExpense> BuildingExpenses => Set<BuildingExpense>();
    public DbSet<BuildingIncome> BuildingIncomes => Set<BuildingIncome>();
    public DbSet<ExpenseCharge> ExpenseCharges => Set<ExpenseCharge>();
    public DbSet<ExpenseSettlement> ExpenseSettlements => Set<ExpenseSettlement>();
    public DbSet<ExpensePeriod> ExpensePeriods => Set<ExpensePeriod>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<UnitResident> UnitResidents => Set<UnitResident>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>().HasIndex(x => x.Slug).IsUnique().HasFilter("[IsDeleted] = 0");

        modelBuilder.Entity<ApplicationUser>()
            .HasIndex(x => new { x.CompanyId, x.Email })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<ApplicationUser>()
            .HasIndex(x => new { x.CompanyId, x.Username })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Username] != ''");
        modelBuilder.Entity<ApplicationUser>().Property(x => x.Role).HasConversion<string>();
        modelBuilder.Entity<ApplicationUser>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApplicationUser>()
            .HasOne(x => x.Condominium)
            .WithMany()
            .HasForeignKey(x => x.CondominiumId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Condominium>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("[CompanyId] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<Condominium>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Condominiums)
            .HasForeignKey(x => x.CompanyId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Building>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("[CompanyId] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<Building>().HasIndex(x => new { x.CondominiumId, x.Code }).IsUnique().HasFilter("[CondominiumId] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<Building>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Buildings)
            .HasForeignKey(x => x.CompanyId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Building>()
            .HasOne(x => x.Condominium)
            .WithMany(x => x.Buildings)
            .HasForeignKey(x => x.CondominiumId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExpensePeriod>().HasIndex(x => new { x.BuildingId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<ExpensePeriod>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<ExpensePeriod>()
            .HasOne(x => x.Company)
            .WithMany(x => x.ExpensePeriods)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpensePeriod>()
            .HasOne(x => x.Building)
            .WithMany(x => x.ExpensePeriods)
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BuildingExpense>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BuildingExpense>().Property(x => x.Category).HasConversion<string>();
        modelBuilder.Entity<BuildingExpense>().Property(x => x.DistributionType).HasConversion<string>();
        modelBuilder.Entity<BuildingExpense>().HasIndex(x => new { x.BuildingId, x.ExpensePeriodId, x.ExpenseDate });
        modelBuilder.Entity<BuildingExpense>()
            .HasOne(x => x.Company)
            .WithMany(x => x.BuildingExpenses)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpense>()
            .HasOne(x => x.Building)
            .WithMany(x => x.BuildingExpenses)
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpense>()
            .HasOne(x => x.ExpensePeriod)
            .WithMany(x => x.BuildingExpenses)
            .HasForeignKey(x => x.ExpensePeriodId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpense>()
            .HasOne(x => x.TargetUnit)
            .WithMany(x => x.BuildingExpenses)
            .HasForeignKey(x => x.TargetUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BuildingIncome>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BuildingIncome>().Property(x => x.Category).HasConversion<string>();
        modelBuilder.Entity<BuildingIncome>().HasIndex(x => new { x.BuildingId, x.ExpensePeriodId, x.IncomeDate });
        modelBuilder.Entity<BuildingIncome>()
            .HasOne(x => x.Company)
            .WithMany(x => x.BuildingIncomes)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingIncome>()
            .HasOne(x => x.Building)
            .WithMany(x => x.BuildingIncomes)
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingIncome>()
            .HasOne(x => x.ExpensePeriod)
            .WithMany(x => x.BuildingIncomes)
            .HasForeignKey(x => x.ExpensePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.TotalBuildingExpenses).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.TotalBuildingIncomes).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.ReserveFundAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.ExtraordinaryAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.NetCommonAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<ExpenseSettlement>().HasIndex(x => x.ExpensePeriodId).IsUnique();
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.Company)
            .WithMany(x => x.ExpenseSettlements)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.Building)
            .WithMany(x => x.ExpenseSettlements)
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.ExpensePeriod)
            .WithMany(x => x.ExpenseSettlements)
            .HasForeignKey(x => x.ExpensePeriodId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.GeneratedByUser)
            .WithMany()
            .HasForeignKey(x => x.GeneratedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.ApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.PublishedByUser)
            .WithMany()
            .HasForeignKey(x => x.PublishedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExpenseCharge>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseCharge>().Property(x => x.ChargeType).HasConversion<string>();
        modelBuilder.Entity<ExpenseCharge>()
            .HasOne(x => x.Company)
            .WithMany(x => x.ExpenseCharges)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseCharge>()
            .HasOne(x => x.ExpensePeriod)
            .WithMany(x => x.Charges)
            .HasForeignKey(x => x.ExpensePeriodId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseCharge>()
            .HasOne(x => x.Unit)
            .WithMany(x => x.ExpenseCharges)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseCharge>()
            .HasOne(x => x.SourceBuildingExpense)
            .WithMany(x => x.ExpenseCharges)
            .HasForeignKey(x => x.SourceBuildingExpenseId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseCharge>()
            .HasOne(x => x.SourceSettlement)
            .WithMany(x => x.ExpenseCharges)
            .HasForeignKey(x => x.SourceSettlementId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Payment>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Payment>().Property(x => x.Method).HasConversion<string>();
        modelBuilder.Entity<Payment>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Payment>()
            .HasOne(x => x.ExpensePeriod)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.ExpensePeriodId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Payment>()
            .HasOne(x => x.Unit)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserBuildingAccess>().HasIndex(x => new { x.ApplicationUserId, x.BuildingId }).IsUnique();
        modelBuilder.Entity<UserBuildingAccess>()
            .HasOne(x => x.ApplicationUser)
            .WithMany(x => x.BuildingAccesses)
            .HasForeignKey(x => x.ApplicationUserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserBuildingAccess>()
            .HasOne(x => x.Building)
            .WithMany(x => x.UserAccesses)
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Unit>().HasIndex(x => new { x.BuildingId, x.Code }).IsUnique();
        modelBuilder.Entity<Unit>().Property(x => x.Coefficient).HasColumnType("decimal(18,6)");
        modelBuilder.Entity<Unit>()
            .HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Unit>()
            .HasOne(x => x.Building)
            .WithMany(x => x.Units)
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Resident>().HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique();
        modelBuilder.Entity<Resident>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Residents)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UnitResident>().HasIndex(x => new { x.UnitId, x.ResidentId, x.StartDate }).IsUnique();
        modelBuilder.Entity<UnitResident>()
            .HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UnitResident>()
            .HasOne(x => x.Unit)
            .WithMany(x => x.UnitResidents)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UnitResident>()
            .HasOne(x => x.Resident)
            .WithMany(x => x.UnitResidents)
            .HasForeignKey(x => x.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        SeedCatalog(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = utcNow;
                entry.Entity.UpdatedAtUtc = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = utcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    private static void SeedCatalog(ModelBuilder modelBuilder)
    {
        var superAdminId = Guid.Parse("B781A1AA-6F3D-46D3-8F78-72E7A0000001");
        var createdAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<ApplicationUser>().HasData(new ApplicationUser
        {
            Id = superAdminId,
            CompanyId = null,
            CondominiumId = null,
            FirstName = "Super",
            LastName = "Admin",
            FullName = "Super Admin",
            Username = "superadmin",
            Email = "superadmin@codexa.local",
            PasswordHash = "Condo*2026",
            Role = UserRole.SuperAdmin,
            IsActive = true,
            MustChangePassword = true,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        });
    }
}
