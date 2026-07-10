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
    public DbSet<RecurringBuildingExpense> RecurringBuildingExpenses => Set<RecurringBuildingExpense>();
    public DbSet<BuildingIncome> BuildingIncomes => Set<BuildingIncome>();
    public DbSet<ExpenseCharge> ExpenseCharges => Set<ExpenseCharge>();
    public DbSet<ExpenseSettlement> ExpenseSettlements => Set<ExpenseSettlement>();
    public DbSet<ExpensePeriod> ExpensePeriods => Set<ExpensePeriod>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<UnitResident> UnitResidents => Set<UnitResident>();
    public DbSet<UnitOwner> UnitOwners => Set<UnitOwner>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<VoteOption> VoteOptions => Set<VoteOption>();
    public DbSet<VoteCast> VoteCasts => Set<VoteCast>();
    public DbSet<OwnerPayment> OwnerPayments => Set<OwnerPayment>();
    public DbSet<OwnerPaymentUnit> OwnerPaymentUnits => Set<OwnerPaymentUnit>();
    public DbSet<OwnerCredit> OwnerCredits => Set<OwnerCredit>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<AmenityReservation> AmenityReservations => Set<AmenityReservation>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<BuildingPlan> BuildingPlans => Set<BuildingPlan>();
    public DbSet<BuildingPlanPayment> BuildingPlanPayments => Set<BuildingPlanPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Company ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Company>().HasIndex(x => x.Slug).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<Company>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Company>().Property(x => x.Slug).HasMaxLength(100);
        modelBuilder.Entity<Company>().Property(x => x.Description).HasMaxLength(1000);
        modelBuilder.Entity<Company>().Property(x => x.ContactPhonePrefix).HasMaxLength(10);
        modelBuilder.Entity<Company>().Property(x => x.ContactPhone).HasMaxLength(30);
        modelBuilder.Entity<Company>().Property(x => x.ContactEmail).HasMaxLength(160);

        // ── ApplicationUser ────────────────────────────────────────────────────
        modelBuilder.Entity<ApplicationUser>()
            .HasIndex(x => new { x.CompanyId, x.Email })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [CompanyId] IS NOT NULL");
        modelBuilder.Entity<ApplicationUser>()
            .HasIndex(x => new { x.CompanyId, x.Username })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [CompanyId] IS NOT NULL AND [Username] != ''");
        // Global uniqueness for platform users (SuperAdmin, no company)
        modelBuilder.Entity<ApplicationUser>()
            .HasIndex(x => x.Email)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [CompanyId] IS NULL");
        modelBuilder.Entity<ApplicationUser>()
            .HasIndex(x => x.Username)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [CompanyId] IS NULL AND [Username] != ''");
        modelBuilder.Entity<ApplicationUser>().Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.FirstName).HasMaxLength(80);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.LastName).HasMaxLength(100);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.FullName).HasMaxLength(200);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.Username).HasMaxLength(60);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.Email).HasMaxLength(160);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.PasswordHash).HasMaxLength(256);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.PhonePrefix).HasMaxLength(10);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.Phone).HasMaxLength(30);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.Address).HasMaxLength(300);
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

        // ── Condominium ────────────────────────────────────────────────────────
        modelBuilder.Entity<Condominium>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("[CompanyId] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<Condominium>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Condominium>().Property(x => x.Code).HasMaxLength(20);
        modelBuilder.Entity<Condominium>().Property(x => x.Address).HasMaxLength(300);
        modelBuilder.Entity<Condominium>().Property(x => x.Description).HasMaxLength(1000);
        modelBuilder.Entity<Condominium>().Property(x => x.ContactPhonePrefix).HasMaxLength(10);
        modelBuilder.Entity<Condominium>().Property(x => x.ContactPhone).HasMaxLength(30);
        modelBuilder.Entity<Condominium>().Property(x => x.ContactEmail).HasMaxLength(160);
        modelBuilder.Entity<Condominium>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Condominiums)
            .HasForeignKey(x => x.CompanyId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Building ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Building>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("[CompanyId] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<Building>().HasIndex(x => new { x.CondominiumId, x.Code }).IsUnique().HasFilter("[CondominiumId] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<Building>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.Code).HasMaxLength(20);
        modelBuilder.Entity<Building>().Property(x => x.Address).HasMaxLength(300);
        modelBuilder.Entity<Building>().Property(x => x.Description).HasMaxLength(1000);
        modelBuilder.Entity<Building>().Property(x => x.ContactPhonePrefix).HasMaxLength(10);
        modelBuilder.Entity<Building>().Property(x => x.ContactPhone).HasMaxLength(30);
        modelBuilder.Entity<Building>().Property(x => x.ContactEmail).HasMaxLength(160);
        modelBuilder.Entity<Building>().Property(x => x.LateFeeRatePercentage).HasColumnType("decimal(5,2)");
        modelBuilder.Entity<Building>().Property(x => x.LateFeeFrequency).HasConversion<string>().HasMaxLength(20);
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

        // ── ExpensePeriod ──────────────────────────────────────────────────────
        modelBuilder.Entity<ExpensePeriod>().HasIndex(x => new { x.BuildingId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<ExpensePeriod>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<ExpensePeriod>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<ExpensePeriod>().Property(x => x.Notes).HasMaxLength(1000);
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

        // ── BuildingExpense ────────────────────────────────────────────────────
        modelBuilder.Entity<BuildingExpense>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BuildingExpense>().Property(x => x.Category).HasConversion<string>().HasMaxLength(50);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.DistributionType).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.SupplierName).HasMaxLength(200);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.Notes).HasMaxLength(1000);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.ReceiptFileName).HasMaxLength(500);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.ReceiptStoredName).HasMaxLength(500);
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

        // ── RecurringBuildingExpense ───────────────────────────────────────────
        modelBuilder.Entity<RecurringBuildingExpense>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<RecurringBuildingExpense>().Property(x => x.Category).HasConversion<string>().HasMaxLength(50);
        modelBuilder.Entity<RecurringBuildingExpense>().Property(x => x.DistributionType).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<RecurringBuildingExpense>().Property(x => x.SupplierName).HasMaxLength(200);
        modelBuilder.Entity<RecurringBuildingExpense>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<RecurringBuildingExpense>().Property(x => x.Notes).HasMaxLength(1000);
        modelBuilder.Entity<RecurringBuildingExpense>()
            .HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RecurringBuildingExpense>()
            .HasOne(x => x.Building)
            .WithMany()
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RecurringBuildingExpense>()
            .HasOne(x => x.TargetUnit)
            .WithMany()
            .HasForeignKey(x => x.TargetUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── BuildingIncome ─────────────────────────────────────────────────────
        modelBuilder.Entity<BuildingIncome>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BuildingIncome>().Property(x => x.Category).HasConversion<string>().HasMaxLength(50);
        modelBuilder.Entity<BuildingIncome>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<BuildingIncome>().Property(x => x.Notes).HasMaxLength(1000);
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

        // ── ExpenseSettlement ──────────────────────────────────────────────────
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.TotalBuildingExpenses).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.TotalBuildingIncomes).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.ReserveFundAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.ExtraordinaryAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.NetCommonAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
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

        // ── ExpenseCharge ──────────────────────────────────────────────────────
        modelBuilder.Entity<ExpenseCharge>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<ExpenseCharge>().Property(x => x.ChargeType).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<ExpenseCharge>().Property(x => x.Concept).HasMaxLength(300);
        modelBuilder.Entity<ExpenseCharge>().Property(x => x.Notes).HasMaxLength(1000);
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
        modelBuilder.Entity<ExpenseCharge>()
            .HasOne(x => x.ReversalOfCharge)
            .WithMany()
            .HasForeignKey(x => x.ReversalOfChargeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Payment ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Payment>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Payment>().Property(x => x.Method).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<Payment>().Property(x => x.Reference).HasMaxLength(200);
        modelBuilder.Entity<Payment>().Property(x => x.Notes).HasMaxLength(1000);
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

        modelBuilder.Entity<PaymentAllocation>().Property(x => x.AllocatedAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<PaymentAllocation>()
            .HasOne(x => x.Payment)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PaymentAllocation>()
            .HasOne(x => x.Charge)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.ExpenseChargeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PaymentAllocation>()
            .HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
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

        // ── Unit ───────────────────────────────────────────────────────────────
        modelBuilder.Entity<Unit>().HasIndex(x => new { x.BuildingId, x.Code }).IsUnique();
        modelBuilder.Entity<Unit>().Property(x => x.Coefficient).HasColumnType("decimal(18,6)");
        modelBuilder.Entity<Unit>().Property(x => x.Code).HasMaxLength(20);
        modelBuilder.Entity<Unit>().Property(x => x.Floor).HasMaxLength(20);
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

        // ── Resident ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Resident>().HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique();
        modelBuilder.Entity<Resident>().Property(x => x.FullName).HasMaxLength(200);
        modelBuilder.Entity<Resident>().Property(x => x.DocumentNumber).HasMaxLength(30);
        modelBuilder.Entity<Resident>().Property(x => x.Email).HasMaxLength(160);
        modelBuilder.Entity<Resident>().Property(x => x.PhoneNumber).HasMaxLength(30);
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

        modelBuilder.Entity<UnitOwner>().HasIndex(x => new { x.UnitId, x.OwnerId }).IsUnique();
        modelBuilder.Entity<UnitOwner>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UnitOwner>()
            .HasOne(x => x.Unit).WithMany()
            .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UnitOwner>()
            .HasOne(x => x.Owner).WithMany()
            .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);

        // ── Announcement ───────────────────────────────────────────────────────
        modelBuilder.Entity<Claim>().HasIndex(x => new { x.CreatedByUserId, x.CreatedAtUtc });
        modelBuilder.Entity<Claim>().HasIndex(x => new { x.BuildingId, x.Status, x.CreatedAtUtc });
        modelBuilder.Entity<Claim>().Property(x => x.Category).HasMaxLength(30);
        modelBuilder.Entity<Claim>().Property(x => x.Description).HasMaxLength(2000);
        modelBuilder.Entity<Claim>().Property(x => x.Status).HasMaxLength(30);
        modelBuilder.Entity<Claim>()
            .HasOne(x => x.Condominium)
            .WithMany()
            .HasForeignKey(x => x.CondominiumId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Claim>()
            .HasOne(x => x.Building)
            .WithMany()
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Claim>()
            .HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Claim>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Claim>()
            .HasOne(x => x.ResolvedByUser)
            .WithMany()
            .HasForeignKey(x => x.ResolvedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Announcement>().HasIndex(x => new { x.BuildingId, x.CreatedAtUtc });
        modelBuilder.Entity<Announcement>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<Announcement>().Property(x => x.Body).HasMaxLength(5000);
        modelBuilder.Entity<Announcement>().Property(x => x.Category).HasMaxLength(50);
        modelBuilder.Entity<Announcement>()
            .HasOne(x => x.Building)
            .WithMany()
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Announcement>()
            .HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Vote / VoteOption ──────────────────────────────────────────────────
        modelBuilder.Entity<Vote>().Property(x => x.QuorumPercentage).HasColumnType("decimal(5,2)");
        modelBuilder.Entity<Vote>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<Vote>().Property(x => x.Description).HasMaxLength(1000);
        modelBuilder.Entity<Vote>().Property(x => x.WeightType).HasMaxLength(20);
        modelBuilder.Entity<Vote>().Property(x => x.Status).HasMaxLength(20);
        modelBuilder.Entity<VoteOption>().Property(x => x.Label).HasMaxLength(200);
        modelBuilder.Entity<Vote>().HasIndex(x => new { x.BuildingId, x.CreatedAtUtc });
        modelBuilder.Entity<Vote>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Vote>()
            .HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VoteOption>()
            .HasOne(x => x.Vote).WithMany(x => x.Options)
            .HasForeignKey(x => x.VoteId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VoteCast>().Property(x => x.CoefficientWeight).HasColumnType("decimal(18,6)");
        modelBuilder.Entity<VoteCast>().HasIndex(x => new { x.VoteId, x.UnitId }).IsUnique();
        modelBuilder.Entity<VoteCast>()
            .HasOne(x => x.Vote).WithMany(x => x.Casts)
            .HasForeignKey(x => x.VoteId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<VoteCast>()
            .HasOne(x => x.Option).WithMany(x => x.Casts)
            .HasForeignKey(x => x.VoteOptionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<VoteCast>()
            .HasOne(x => x.Unit).WithMany()
            .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<VoteCast>()
            .HasOne(x => x.CastBy).WithMany()
            .HasForeignKey(x => x.CastByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // ── OwnerPayment ───────────────────────────────────────────────────────
        modelBuilder.Entity<OwnerPayment>().Property(x => x.DeclaredAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<OwnerPayment>().Property(x => x.ReviewedAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<OwnerPayment>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<OwnerPayment>().Property(x => x.Reference).HasMaxLength(50);
        modelBuilder.Entity<OwnerPayment>().Property(x => x.ComprobanteUrl).HasMaxLength(500);
        modelBuilder.Entity<OwnerPayment>().Property(x => x.RejectionReason).HasMaxLength(500);
        modelBuilder.Entity<OwnerPayment>().HasIndex(x => x.Reference).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<OwnerPayment>().HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
        modelBuilder.Entity<OwnerPayment>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OwnerPayment>()
            .HasOne(x => x.Owner).WithMany()
            .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OwnerPayment>()
            .HasOne(x => x.ReviewedByUser).WithMany()
            .HasForeignKey(x => x.ReviewedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // ── OwnerPaymentUnit ───────────────────────────────────────────────────
        modelBuilder.Entity<OwnerPaymentUnit>().Property(x => x.AllocatedAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<OwnerPaymentUnit>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OwnerPaymentUnit>()
            .HasOne(x => x.OwnerPayment).WithMany(x => x.Units)
            .HasForeignKey(x => x.OwnerPaymentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OwnerPaymentUnit>()
            .HasOne(x => x.Unit).WithMany()
            .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);

        // ── OwnerCredit ────────────────────────────────────────────────────────
        modelBuilder.Entity<OwnerCredit>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<OwnerCredit>().HasIndex(x => new { x.CompanyId, x.OwnerId }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<OwnerCredit>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OwnerCredit>()
            .HasOne(x => x.Owner).WithMany()
            .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);

        // ── Notification ───────────────────────────────────────────────────────
        modelBuilder.Entity<Notification>().Property(x => x.Type).HasConversion<string>().HasMaxLength(50);
        modelBuilder.Entity<Notification>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<Notification>().Property(x => x.Body).HasMaxLength(1000);
        modelBuilder.Entity<Notification>().Property(x => x.EntityType).HasMaxLength(50);
        modelBuilder.Entity<Notification>().HasIndex(x => new { x.RecipientId, x.IsRead, x.CreatedAtUtc });
        modelBuilder.Entity<Notification>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Notification>()
            .HasOne(x => x.Recipient).WithMany()
            .HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);

        // ── Amenity ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Amenity>().Property(x => x.Name).HasMaxLength(150);
        modelBuilder.Entity<Amenity>().Property(x => x.Description).HasMaxLength(1000);
        modelBuilder.Entity<Amenity>().Property(x => x.ReservationPrice).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Amenity>().HasIndex(x => new { x.BuildingId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<Amenity>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Amenity>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AmenityReservation>().Property(x => x.Price).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<AmenityReservation>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<AmenityReservation>().Property(x => x.Notes).HasMaxLength(500);
        modelBuilder.Entity<AmenityReservation>().Property(x => x.ComprobanteUrl).HasMaxLength(500);
        modelBuilder.Entity<AmenityReservation>().Property(x => x.RejectionReason).HasMaxLength(500);
        modelBuilder.Entity<AmenityReservation>().HasIndex(x => new { x.AmenityId, x.StartsAt, x.EndsAt });
        modelBuilder.Entity<AmenityReservation>().HasIndex(x => new { x.ReservedByUserId, x.CreatedAtUtc });
        modelBuilder.Entity<AmenityReservation>()
            .HasOne(x => x.Amenity).WithMany(x => x.Reservations)
            .HasForeignKey(x => x.AmenityId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AmenityReservation>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AmenityReservation>()
            .HasOne(x => x.ReservedByUser).WithMany()
            .HasForeignKey(x => x.ReservedByUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AmenityReservation>()
            .HasOne(x => x.ReviewedByUser).WithMany()
            .HasForeignKey(x => x.ReviewedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AmenityReservation>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

        // ── Plan ───────────────────────────────────────────────────────────────
        modelBuilder.Entity<Plan>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Plan>().Property(x => x.Description).HasMaxLength(1000);
        modelBuilder.Entity<Plan>().Property(x => x.Price).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Plan>().Property(x => x.BillingCycle).HasConversion<string>().HasMaxLength(20);
        // Only one default plan allowed at a time
        modelBuilder.Entity<Plan>().HasIndex(x => x.IsDefault).HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0").IsUnique();

        // ── BuildingPlan ───────────────────────────────────────────────────────
        modelBuilder.Entity<BuildingPlan>().Property(x => x.AssignmentScope).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<BuildingPlan>().HasIndex(x => new { x.BuildingId, x.IsArchived })
            .HasFilter("[IsArchived] = 0 AND [IsDeleted] = 0");
        modelBuilder.Entity<BuildingPlan>().HasIndex(x => new { x.ScopeEntityId, x.AssignmentScope });
        modelBuilder.Entity<BuildingPlan>()
            .HasOne(x => x.Plan).WithMany(x => x.BuildingPlans)
            .HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingPlan>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingPlan>()
            .HasOne(x => x.AssignedBy).WithMany()
            .HasForeignKey(x => x.AssignedById).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingPlan>()
            .HasOne(x => x.PaidBy).WithMany()
            .HasForeignKey(x => x.PaidById).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // ── BuildingPlanPayment ────────────────────────────────────────────────
        modelBuilder.Entity<BuildingPlanPayment>().Property(x => x.DeclaredAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BuildingPlanPayment>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<BuildingPlanPayment>().Property(x => x.AssignmentScope).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<BuildingPlanPayment>().Property(x => x.ComprobanteUrl).HasMaxLength(500);
        modelBuilder.Entity<BuildingPlanPayment>().Property(x => x.Reference).HasMaxLength(50);
        modelBuilder.Entity<BuildingPlanPayment>().Property(x => x.RejectionReason).HasMaxLength(500);
        modelBuilder.Entity<BuildingPlanPayment>().HasIndex(x => new { x.ScopeEntityId, x.Status });
        modelBuilder.Entity<BuildingPlanPayment>()
            .HasOne(x => x.BuildingPlan).WithMany(x => x.Payments)
            .HasForeignKey(x => x.BuildingPlanId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingPlanPayment>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingPlanPayment>()
            .HasOne(x => x.SubmittedBy).WithMany()
            .HasForeignKey(x => x.SubmittedById).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingPlanPayment>()
            .HasOne(x => x.ReviewedBy).WithMany()
            .HasForeignKey(x => x.ReviewedById).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<Plan>().HasData(new Plan
        {
            Id = Guid.Parse("A0000000-0000-0000-0000-000000000001"),
            Name = "Plan Gratuito 45 días",
            Description = "Plan de prueba gratuito incluido al registrar un nuevo edificio en la plataforma.",
            IsDefault = true,
            Price = 0m,
            BillingCycle = BillingCycle.Monthly,
            GracePeriodDays = 5,
            IsActive = true,
            IsAssigned = false,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        });

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
            PasswordHash = "$2a$11$Y7ysfENQB6qvGrf5rxRPuer0C8iSxyLeVCbqBJw2jADBZb/wVqbrO",
            Role = UserRole.SuperAdmin,
            IsActive = true,
            MustChangePassword = true,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        });
    }
}
