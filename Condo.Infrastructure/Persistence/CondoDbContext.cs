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
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<UserBuildingAccess> UserBuildingAccesses => Set<UserBuildingAccess>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<BuildingExpense> BuildingExpenses => Set<BuildingExpense>();
    public DbSet<BuildingExpenseCreditNote> BuildingExpenseCreditNotes => Set<BuildingExpenseCreditNote>();
    public DbSet<BuildingExpenseCreditNoteAllocation> BuildingExpenseCreditNoteAllocations => Set<BuildingExpenseCreditNoteAllocation>();
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
    public DbSet<OwnerCreditMovement> OwnerCreditMovements => Set<OwnerCreditMovement>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<AmenityReservation> AmenityReservations => Set<AmenityReservation>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<BuildingPlan> BuildingPlans => Set<BuildingPlan>();
    public DbSet<BuildingPlanPayment> BuildingPlanPayments => Set<BuildingPlanPayment>();
    public DbSet<InvoiceSeries> InvoiceSeries => Set<InvoiceSeries>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceAuditLog> InvoiceAuditLogs => Set<InvoiceAuditLog>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();
    public DbSet<CreditNoteAttachment> CreditNoteAttachments => Set<CreditNoteAttachment>();
    public DbSet<CreditNoteAuditLog> CreditNoteAuditLogs => Set<CreditNoteAuditLog>();
    public DbSet<FinanceSettings> FinanceSettings => Set<FinanceSettings>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<BuildingBankAccount> BuildingBankAccounts => Set<BuildingBankAccount>();
    public DbSet<LedgerCategory> LedgerCategories => Set<LedgerCategory>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<FinanceAuditLog> FinanceAuditLogs => Set<FinanceAuditLog>();
    public DbSet<FinancePeriodClosure> FinancePeriodClosures => Set<FinancePeriodClosure>();
    public DbSet<BuildingNoticeRule> BuildingNoticeRules => Set<BuildingNoticeRule>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<BankReconciliation> BankReconciliations => Set<BankReconciliation>();
    public DbSet<BankReconciledMovement> BankReconciledMovements => Set<BankReconciledMovement>();
    public DbSet<MarketplaceListing> MarketplaceListings => Set<MarketplaceListing>();
    public DbSet<MarketplaceReservation> MarketplaceReservations => Set<MarketplaceReservation>();
    public DbSet<MarketplaceReservationSlot> MarketplaceReservationSlots => Set<MarketplaceReservationSlot>();
    public DbSet<MarketplacePayment> MarketplacePayments => Set<MarketplacePayment>();
    public DbSet<MarketplaceAccountMovement> MarketplaceAccountMovements => Set<MarketplaceAccountMovement>();
    public DbSet<MarketplaceEvent> MarketplaceEvents => Set<MarketplaceEvent>();
    public DbSet<MarketplaceRefund> MarketplaceRefunds => Set<MarketplaceRefund>();
    public DbSet<MarketplaceClaim> MarketplaceClaims => Set<MarketplaceClaim>();
    public DbSet<MarketplaceOwnerDebt> MarketplaceOwnerDebts => Set<MarketplaceOwnerDebt>();
    public DbSet<MarketplaceHandoverNote> MarketplaceHandoverNotes => Set<MarketplaceHandoverNote>();
    public DbSet<AdCampaign> AdCampaigns => Set<AdCampaign>();
    public DbSet<AdCampaignBuilding> AdCampaignBuildings => Set<AdCampaignBuilding>();

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
        modelBuilder.Entity<ApplicationUser>().Property(x => x.SignatureUrl).HasMaxLength(500);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.PersonType).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.LegalName).HasMaxLength(200);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.InvoiceName).HasMaxLength(200);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.InvoiceDocumentType).HasMaxLength(30);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.InvoiceDocument).HasMaxLength(40);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.InvoiceAddress).HasMaxLength(300);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.InvoiceEmail).HasMaxLength(160);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.SecondaryPhone).HasMaxLength(30);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.WhatsAppPhone).HasMaxLength(30);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.Nationality).HasMaxLength(60);
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

        // ── PasswordResetToken ────────────────────────────────────────────────
        modelBuilder.Entity<PasswordResetToken>().Property(x => x.TokenHash).HasMaxLength(128);
        modelBuilder.Entity<PasswordResetToken>().Property(x => x.RequestedFromIp).HasMaxLength(64);
        modelBuilder.Entity<PasswordResetToken>().HasIndex(x => x.TokenHash);
        modelBuilder.Entity<PasswordResetToken>().HasIndex(x => new { x.ApplicationUserId, x.CreatedAtUtc });
        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(x => x.ApplicationUser)
            .WithMany()
            .HasForeignKey(x => x.ApplicationUserId)
            .OnDelete(DeleteBehavior.Cascade);

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
        modelBuilder.Entity<Building>().Property(x => x.MarketplaceCommissionPercent).HasColumnType("decimal(5,2)").HasDefaultValue(10m);
        modelBuilder.Entity<Building>().Property(x => x.MarketplaceTransferInfo).HasMaxLength(1000);
        modelBuilder.Entity<Building>().Property(x => x.IncomeTreatment).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<Building>().Property(x => x.ReserveFundPercentage).HasColumnType("decimal(5,2)");
        modelBuilder.Entity<Building>().Property(x => x.ExtraordinaryPercentage).HasColumnType("decimal(5,2)");
        modelBuilder.Entity<Building>().Property(x => x.LateFeeFrequency).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<Building>().Property(x => x.LateFeeCapPercentage).HasColumnType("decimal(7,2)");
        modelBuilder.Entity<Building>().Property(x => x.LateFeeMinAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Building>().Property(x => x.LateFeeAppliesToReserve).HasDefaultValue(true).ValueGeneratedNever();
        modelBuilder.Entity<Building>().Property(x => x.LateFeeAppliesToExtraordinary).HasDefaultValue(true).ValueGeneratedNever();
        modelBuilder.Entity<Building>().Property(x => x.LateFeeAppliesToIndividual).HasDefaultValue(true).ValueGeneratedNever();
        modelBuilder.Entity<Building>().Property(x => x.ReserveUsePolicy).HasConversion<string>().HasMaxLength(40).HasDefaultValue(ReserveUsePolicy.FreeUse).ValueGeneratedNever();
        modelBuilder.Entity<FinanceSettings>().Property(x => x.BudgetWarnPercent).HasDefaultValue(10).ValueGeneratedNever();
        modelBuilder.Entity<Building>().Property(x => x.ReserveUseThreshold).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Building>().Property(x => x.InvoiceTemplateUrl).HasMaxLength(500);
        modelBuilder.Entity<Building>().Property(x => x.InvoiceTemplateFileName).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.CreditNoteTemplateUrl).HasMaxLength(500);
        modelBuilder.Entity<Building>().Property(x => x.CreditNoteTemplateFileName).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.SettlementTemplateUrl).HasMaxLength(500);
        modelBuilder.Entity<Building>().Property(x => x.SettlementTemplateFileName).HasMaxLength(200);

        // Ficha de registro del edificio
        modelBuilder.Entity<Building>().Property(x => x.PropertyType).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<Building>().Property(x => x.Department).HasMaxLength(100);
        modelBuilder.Entity<Building>().Property(x => x.City).HasMaxLength(100);
        modelBuilder.Entity<Building>().Property(x => x.Neighborhood).HasMaxLength(100);
        modelBuilder.Entity<Building>().Property(x => x.LocationReference).HasMaxLength(300);
        modelBuilder.Entity<Building>().Property(x => x.Latitude).HasColumnType("decimal(9,6)");
        modelBuilder.Entity<Building>().Property(x => x.Longitude).HasColumnType("decimal(9,6)");
        modelBuilder.Entity<Building>().Property(x => x.LogoUrl).HasMaxLength(500);
        modelBuilder.Entity<Building>().Property(x => x.WhatsAppPhone).HasMaxLength(30);
        modelBuilder.Entity<Building>().Property(x => x.OfficeHours).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.FincaNumber).HasMaxLength(50);
        modelBuilder.Entity<Building>().Property(x => x.PadronNumber).HasMaxLength(50);
        modelBuilder.Entity<Building>().Property(x => x.CadastralAccount).HasMaxLength(50);
        modelBuilder.Entity<Building>().Property(x => x.LegalEntityNumber).HasMaxLength(50);
        modelBuilder.Entity<Building>().Property(x => x.BylawsUrl).HasMaxLength(500);
        modelBuilder.Entity<Building>().Property(x => x.BylawsFileName).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.AdministratorName).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.AdministratorPhone).HasMaxLength(30);
        modelBuilder.Entity<Building>().Property(x => x.EmergencyContactName).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.EmergencyContactPhone).HasMaxLength(30);
        modelBuilder.Entity<Building>().Property(x => x.Ruc).HasMaxLength(20);
        modelBuilder.Entity<Building>().Property(x => x.LegalName).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.TaxpayerType).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<Building>().Property(x => x.VatRegime).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<Building>().Property(x => x.EconomicActivity).HasMaxLength(200);
        modelBuilder.Entity<Building>().Property(x => x.FiscalAddress).HasMaxLength(300);
        modelBuilder.Entity<Building>().Property(x => x.InvoiceEmail).HasMaxLength(160);
        modelBuilder.Entity<Building>().Property(x => x.PaymentInstructions).HasMaxLength(1000);
        modelBuilder.Entity<Building>().Property(x => x.TimeZoneId).HasMaxLength(60);

        modelBuilder.Entity<BuildingBankAccount>().Property(x => x.BankName).HasMaxLength(120);
        modelBuilder.Entity<BuildingBankAccount>().Property(x => x.AccountType).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<BuildingBankAccount>().Property(x => x.AccountNumber).HasMaxLength(40);
        modelBuilder.Entity<BuildingBankAccount>().Property(x => x.HolderName).HasMaxLength(200);
        modelBuilder.Entity<BuildingBankAccount>().Property(x => x.HolderDocument).HasMaxLength(30);
        modelBuilder.Entity<BuildingBankAccount>().Property(x => x.Alias).HasMaxLength(60);
        modelBuilder.Entity<BuildingBankAccount>().HasIndex(x => x.BuildingId);
        modelBuilder.Entity<BuildingBankAccount>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingBankAccount>()
            .HasOne(x => x.Building).WithMany(x => x.BankAccounts)
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
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
        modelBuilder.Entity<Building>()
            .HasOne(x => x.PresidentUser)
            .WithMany()
            .HasForeignKey(x => x.PresidentUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // ── ExpensePeriod ──────────────────────────────────────────────────────
        // Filtrado por IsDeleted = 0: sin esto, un periodo borrado (soft-delete) deja su combinacion
        // edificio+anio+mes bloqueada para siempre en el indice fisico, aunque para la app ya no exista.
        modelBuilder.Entity<ExpensePeriod>().HasIndex(x => new { x.BuildingId, x.Year, x.Month })
            .IsUnique().HasFilter("[IsDeleted] = 0");
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
        modelBuilder.Entity<BuildingExpense>()
            .HasOne(x => x.LedgerCategory).WithMany()
            .HasForeignKey(x => x.LedgerCategoryId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpense>().HasIndex(x => x.LedgerCategoryId);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.OriginalAmount).HasColumnType("decimal(18,2)");

        // ── Nota de credito del proveedor sobre un gasto ──────────────────────
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.SupplierName).HasMaxLength(200);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.SupplierKey).HasMaxLength(200);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.NumeroKey).HasMaxLength(50);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.TimbradoKey).HasMaxLength(20);
        // Una misma nota (proveedor + timbrado + numero) no puede estar aplicada dos veces en la empresa. Anulada se puede volver a registrar.
        modelBuilder.Entity<BuildingExpenseCreditNote>()
            .HasIndex(x => new { x.CompanyId, x.SupplierKey, x.TimbradoKey, x.NumeroKey })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 'Applied'");
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.Numero).HasMaxLength(50);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.Timbrado).HasMaxLength(20);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.Reason).HasMaxLength(500);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.DocumentUrl).HasMaxLength(500);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.VoidReason).HasMaxLength(500);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<BuildingExpenseCreditNote>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<BuildingExpenseCreditNote>().HasIndex(x => new { x.BuildingExpenseId, x.Status });
        modelBuilder.Entity<BuildingExpenseCreditNote>().HasIndex(x => new { x.BuildingId, x.ExpensePeriodId });
        modelBuilder.Entity<BuildingExpenseCreditNote>()
            .HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpenseCreditNote>()
            .HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpenseCreditNote>()
            .HasOne(x => x.BuildingExpense).WithMany(x => x.CreditNotes).HasForeignKey(x => x.BuildingExpenseId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpenseCreditNote>()
            .HasOne(x => x.ExpensePeriod).WithMany().HasForeignKey(x => x.ExpensePeriodId).OnDelete(DeleteBehavior.Restrict);

        // Reparto por unidad de una nota de credito de proveedor sobre un periodo publicado.
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>().HasIndex(x => x.CreditNoteId);
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>().HasIndex(x => x.UnitId);
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>().HasIndex(x => x.OwnerCreditMovementId);
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>()
            .HasOne(x => x.CreditNote).WithMany(x => x.Allocations).HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>()
            .HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>()
            .HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>()
            .HasOne(x => x.OwnerCreditMovement).WithMany().HasForeignKey(x => x.OwnerCreditMovementId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpenseCreditNoteAllocation>()
            .HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

        // ── RecurringBuildingExpense ───────────────────────────────────────────
        modelBuilder.Entity<RecurringBuildingExpense>()
            .HasOne(x => x.LedgerCategory).WithMany()
            .HasForeignKey(x => x.LedgerCategoryId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RecurringBuildingExpense>().HasIndex(x => x.LedgerCategoryId);
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
        modelBuilder.Entity<BuildingIncome>()
            .HasOne(x => x.LedgerCategory).WithMany()
            .HasForeignKey(x => x.LedgerCategoryId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingIncome>().HasIndex(x => x.LedgerCategoryId);

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
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.RejectedByUser)
            .WithMany()
            .HasForeignKey(x => x.RejectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.PresidentRejectionReason).HasMaxLength(500);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.PresidentApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.PresidentApprovedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.PresidentRejectedByUser)
            .WithMany()
            .HasForeignKey(x => x.PresidentRejectedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExpenseSettlement>().Property(x => x.UnpublishReason).HasMaxLength(500);
        modelBuilder.Entity<ExpenseSettlement>()
            .HasOne(x => x.UnpublishedByUser)
            .WithMany()
            .HasForeignKey(x => x.UnpublishedByUserId)
            .IsRequired(false)
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
        modelBuilder.Entity<ExpenseCharge>().HasIndex(x => x.SourceCreditNoteId);
        modelBuilder.Entity<ExpenseCharge>()
            .HasOne(x => x.SourceCreditNote)
            .WithMany()
            .HasForeignKey(x => x.SourceCreditNoteId)
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
        modelBuilder.Entity<Resident>().Property(x => x.Relationship).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<Resident>().Property(x => x.EmergencyContactName).HasMaxLength(200);
        modelBuilder.Entity<Resident>().Property(x => x.EmergencyContactPhone).HasMaxLength(30);
        modelBuilder.Entity<Resident>().Property(x => x.Nationality).HasMaxLength(60);
        modelBuilder.Entity<Resident>().Property(x => x.LeaseUrl).HasMaxLength(500);
        modelBuilder.Entity<Resident>().Property(x => x.LeaseFileName).HasMaxLength(200);
        modelBuilder.Entity<Resident>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Residents)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Resident>()
            .HasOne(x => x.ApplicationUser)
            .WithMany()
            .HasForeignKey(x => x.ApplicationUserId)
            .OnDelete(DeleteBehavior.SetNull);

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
        modelBuilder.Entity<UnitOwner>().Property(x => x.OwnershipPercentage).HasColumnType("decimal(5,2)");
        modelBuilder.Entity<UnitOwner>().Property(x => x.TransferReason).HasMaxLength(200);
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
        modelBuilder.Entity<OwnerPayment>().Property(x => x.Channel).HasConversion<string>().HasMaxLength(10).HasDefaultValue(OwnerPaymentChannel.App).HasSentinel((OwnerPaymentChannel)0);
        modelBuilder.Entity<OwnerPayment>().Property(x => x.Method).HasConversion<string>().HasMaxLength(30).HasDefaultValue(PaymentMethod.BankTransfer).HasSentinel((PaymentMethod)0);
        modelBuilder.Entity<OwnerPayment>().Property(x => x.ExternalReference).HasMaxLength(100);
        modelBuilder.Entity<OwnerPayment>().Property(x => x.Notes).HasMaxLength(500);
        // La referencia (PAY-año-n) se numera por empresa, asi que es unica por empresa (antes era global y la
        // segunda empresa chocaba con la primera).
        modelBuilder.Entity<OwnerPayment>().HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique().HasFilter("[IsDeleted] = 0");
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

        // ── OwnerCreditMovement ────────────────────────────────────────────────
        modelBuilder.Entity<OwnerCreditMovement>().Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<OwnerCreditMovement>().Property(x => x.ApplyMode).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<OwnerCreditMovement>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<OwnerCreditMovement>().Property(x => x.RemainingAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<OwnerCreditMovement>().Property(x => x.SourceReference).HasMaxLength(100);
        modelBuilder.Entity<OwnerCreditMovement>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<OwnerCreditMovement>().HasIndex(x => new { x.CompanyId, x.OwnerId, x.CreatedAtUtc });
        modelBuilder.Entity<OwnerCreditMovement>().HasIndex(x => x.CreditNoteId);
        modelBuilder.Entity<OwnerCreditMovement>()
            .HasOne(x => x.CreditNote).WithMany()
            .HasForeignKey(x => x.CreditNoteId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

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

        // ── DeviceToken ────────────────────────────────────────────────────────
        modelBuilder.Entity<DeviceToken>().Property(x => x.Token).HasMaxLength(300);
        modelBuilder.Entity<DeviceToken>().Property(x => x.Platform).HasMaxLength(20);
        modelBuilder.Entity<DeviceToken>().HasIndex(x => x.Token).IsUnique();
        modelBuilder.Entity<DeviceToken>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

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

        // ── InvoiceSeries (timbrado) ───────────────────────────────────────────
        modelBuilder.Entity<InvoiceSeries>().Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<InvoiceSeries>().Property(x => x.Ruc).HasMaxLength(20);
        modelBuilder.Entity<InvoiceSeries>().Property(x => x.RazonSocial).HasMaxLength(200);
        modelBuilder.Entity<InvoiceSeries>().Property(x => x.Establecimiento).HasMaxLength(3);
        modelBuilder.Entity<InvoiceSeries>().Property(x => x.PuntoExpedicion).HasMaxLength(3);
        modelBuilder.Entity<InvoiceSeries>().Property(x => x.NumeroTimbrado).HasMaxLength(20);
        // Un mismo timbrado (Establecimiento+PuntoExpedicion+NumeroTimbrado) autoriza rangos
        // separados por tipo de documento — factura y NC son filas distintas con el mismo timbrado.
        modelBuilder.Entity<InvoiceSeries>()
            .HasIndex(x => new { x.CompanyId, x.Establecimiento, x.PuntoExpedicion, x.NumeroTimbrado, x.DocumentType })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<InvoiceSeries>().HasIndex(x => new { x.BuildingId, x.Activo });
        modelBuilder.Entity<InvoiceSeries>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InvoiceSeries>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        // ── Invoice (factura) ──────────────────────────────────────────────────
        modelBuilder.Entity<Invoice>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<Invoice>().Property(x => x.NumeroFormateado).HasMaxLength(50);
        modelBuilder.Entity<Invoice>().Property(x => x.MontoTotal).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<Invoice>().Property(x => x.DetalleSnapshotJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<Invoice>().Property(x => x.MotivoAnulacion).HasMaxLength(500);
        modelBuilder.Entity<Invoice>().Property(x => x.ClientName).HasMaxLength(200);
        modelBuilder.Entity<Invoice>().Property(x => x.ClientDocumentType).HasMaxLength(30);
        modelBuilder.Entity<Invoice>().Property(x => x.ClientDocument).HasMaxLength(40);
        modelBuilder.Entity<Invoice>().Property(x => x.ClientAddress).HasMaxLength(300);
        modelBuilder.Entity<Invoice>().Property(x => x.ClientEmail).HasMaxLength(160);
        modelBuilder.Entity<Invoice>().HasIndex(x => new { x.InvoiceSeriesId, x.Numero }).IsUnique()
            .HasFilter("[Numero] IS NOT NULL");
        modelBuilder.Entity<Invoice>().HasIndex(x => x.PaymentId);
        modelBuilder.Entity<Invoice>().HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
        modelBuilder.Entity<Invoice>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasOne(x => x.Unit).WithMany()
            .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasOne(x => x.Payment).WithMany()
            .HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>().HasIndex(x => x.OwnerPaymentId);
        modelBuilder.Entity<Invoice>()
            .HasOne(x => x.OwnerPayment).WithMany()
            .HasForeignKey(x => x.OwnerPaymentId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasOne(x => x.Series).WithMany(x => x.Invoices)
            .HasForeignKey(x => x.InvoiceSeriesId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasOne(x => x.ReemplazadaPorInvoice).WithMany()
            .HasForeignKey(x => x.ReemplazadaPorInvoiceId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // ── InvoiceAuditLog ────────────────────────────────────────────────────
        modelBuilder.Entity<InvoiceAuditLog>().Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<InvoiceAuditLog>().Property(x => x.DatosAntesJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<InvoiceAuditLog>().Property(x => x.DatosDespuesJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<InvoiceAuditLog>().Property(x => x.Detalle).HasMaxLength(500);
        modelBuilder.Entity<InvoiceAuditLog>().HasIndex(x => new { x.InvoiceId, x.TimestampUtc });
        modelBuilder.Entity<InvoiceAuditLog>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InvoiceAuditLog>()
            .HasOne(x => x.Invoice).WithMany()
            .HasForeignKey(x => x.InvoiceId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InvoiceAuditLog>()
            .HasOne(x => x.InvoiceSeries).WithMany()
            .HasForeignKey(x => x.InvoiceSeriesId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // ── CreditNote (nota de credito interna) ──────────────────────────────
        modelBuilder.Entity<CreditNote>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<CreditNote>().Property(x => x.Motivo).HasMaxLength(500);
        modelBuilder.Entity<CreditNote>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<CreditNote>().Property(x => x.RejectionReason).HasMaxLength(500);
        modelBuilder.Entity<CreditNote>().Property(x => x.VoidReason).HasMaxLength(500);
        modelBuilder.Entity<CreditNote>().Property(x => x.FiscalDocumentType).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<CreditNote>().Property(x => x.FiscalNumero).HasMaxLength(50);
        modelBuilder.Entity<CreditNote>().Property(x => x.FiscalTimbrado).HasMaxLength(50);
        modelBuilder.Entity<CreditNote>().Property(x => x.FiscalCdc).HasMaxLength(100);
        modelBuilder.Entity<CreditNote>().Property(x => x.FiscalEstado).HasMaxLength(100);
        modelBuilder.Entity<CreditNote>().Property(x => x.FiscalObservaciones).HasMaxLength(1000);
        modelBuilder.Entity<CreditNote>().HasIndex(x => x.InvoiceId);
        modelBuilder.Entity<CreditNote>().HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
        modelBuilder.Entity<CreditNote>().HasIndex(x => new { x.InvoiceSeriesId, x.Numero }).IsUnique()
            .HasFilter("[Numero] IS NOT NULL");
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.Unit).WithMany()
            .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.Invoice).WithMany()
            .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.ApprovedByUser).WithMany()
            .HasForeignKey(x => x.ApprovedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.RejectedByUser).WithMany()
            .HasForeignKey(x => x.RejectedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNote>()
            .HasOne(x => x.Series).WithMany(x => x.CreditNotes)
            .HasForeignKey(x => x.InvoiceSeriesId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // ── CreditNoteLine ─────────────────────────────────────────────────────
        modelBuilder.Entity<CreditNoteLine>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<CreditNoteLine>().Property(x => x.Concept).HasMaxLength(300);
        modelBuilder.Entity<CreditNoteLine>().HasIndex(x => x.ExpenseChargeId);
        modelBuilder.Entity<CreditNoteLine>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNoteLine>()
            .HasOne(x => x.CreditNote).WithMany(x => x.Lines)
            .HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CreditNoteLine>()
            .HasOne(x => x.ExpenseCharge).WithMany()
            .HasForeignKey(x => x.ExpenseChargeId).OnDelete(DeleteBehavior.Restrict);

        // ── CreditNoteAttachment ───────────────────────────────────────────────
        modelBuilder.Entity<CreditNoteAttachment>().Property(x => x.Url).HasMaxLength(500);
        modelBuilder.Entity<CreditNoteAttachment>().Property(x => x.FileName).HasMaxLength(260);
        modelBuilder.Entity<CreditNoteAttachment>().Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<CreditNoteAttachment>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNoteAttachment>()
            .HasOne(x => x.CreditNote).WithMany(x => x.Attachments)
            .HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CreditNoteAttachment>()
            .HasOne(x => x.UploadedByUser).WithMany()
            .HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);

        // ── CreditNoteAuditLog ─────────────────────────────────────────────────
        modelBuilder.Entity<CreditNoteAuditLog>().Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<CreditNoteAuditLog>().Property(x => x.DatosAntesJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<CreditNoteAuditLog>().Property(x => x.DatosDespuesJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<CreditNoteAuditLog>().Property(x => x.Detalle).HasMaxLength(500);
        modelBuilder.Entity<CreditNoteAuditLog>().HasIndex(x => new { x.CreditNoteId, x.TimestampUtc });
        modelBuilder.Entity<CreditNoteAuditLog>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CreditNoteAuditLog>()
            .HasOne(x => x.CreditNote).WithMany()
            .HasForeignKey(x => x.CreditNoteId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // ── Finanzas del edificio ──────────────────────────────────────────────
        modelBuilder.Entity<FinanceSettings>().HasIndex(x => x.BuildingId).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<FinanceSettings>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FinanceSettings>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FinancialAccount>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<FinancialAccount>().Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<FinancialAccount>().Property(x => x.OpeningBalance).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<FinancialAccount>().HasIndex(x => new { x.BuildingId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        // Una sola caja y un solo fondo de reserva por edificio (los bancos pueden ser varios).
        modelBuilder.Entity<FinancialAccount>().HasIndex(x => new { x.BuildingId, x.Type }).IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Type] <> 'Bank'");
        modelBuilder.Entity<FinancialAccount>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FinancialAccount>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LedgerCategory>().Property(x => x.Code).HasMaxLength(30);
        modelBuilder.Entity<LedgerCategory>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<LedgerCategory>().Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<LedgerCategory>().Property(x => x.ExternalCode).HasMaxLength(50);
        modelBuilder.Entity<LedgerCategory>().Property(x => x.SystemKey).HasMaxLength(60);
        modelBuilder.Entity<LedgerCategory>().Property(x => x.ExpenseCategory).HasConversion<string>().HasMaxLength(50);
        modelBuilder.Entity<LedgerCategory>().Property(x => x.IncomeCategory).HasConversion<string>().HasMaxLength(50);
        modelBuilder.Entity<LedgerCategory>().Ignore(x => x.RubroKey);
        modelBuilder.Entity<LedgerCategory>().HasIndex(x => new { x.BuildingId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<LedgerCategory>().HasIndex(x => new { x.BuildingId, x.SystemKey }).IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [SystemKey] IS NOT NULL");
        modelBuilder.Entity<LedgerCategory>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<LedgerCategory>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<LedgerCategory>()
            .HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BudgetLine>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BudgetLine>().HasIndex(x => new { x.BuildingId, x.CategoryId, x.Year, x.Month }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<BudgetLine>().HasIndex(x => new { x.BuildingId, x.Year, x.Month });
        modelBuilder.Entity<BudgetLine>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetLine>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetLine>()
            .HasOne(x => x.Category).WithMany()
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // ── Centro de configuracion del edificio: historial de cambios ─────────
        modelBuilder.Entity<FinanceAuditLog>().Property(x => x.Section).HasMaxLength(40);
        modelBuilder.Entity<FinanceAuditLog>().Property(x => x.Action).HasMaxLength(40);
        modelBuilder.Entity<FinanceAuditLog>().Property(x => x.Summary).HasMaxLength(500);
        modelBuilder.Entity<FinanceAuditLog>().Property(x => x.EntityType).HasMaxLength(60);
        modelBuilder.Entity<FinanceAuditLog>().Property(x => x.UserEmail).HasMaxLength(256);
        modelBuilder.Entity<FinanceAuditLog>().Property(x => x.UserRole).HasMaxLength(40);
        modelBuilder.Entity<FinanceAuditLog>().HasIndex(x => new { x.BuildingId, x.CreatedAtUtc });
        modelBuilder.Entity<FinanceAuditLog>().HasIndex(x => new { x.BuildingId, x.Section, x.CreatedAtUtc });
        modelBuilder.Entity<FinanceAuditLog>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FinanceAuditLog>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        // ── Finanzas: conciliacion bancaria manual ─────────────────────────────
        modelBuilder.Entity<BankReconciliation>().Property(x => x.StatementBalance).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BankReconciliation>().Property(x => x.ReconciledBalanceAtCompletion).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BankReconciliation>().Property(x => x.Notes).HasMaxLength(500);
        modelBuilder.Entity<BankReconciliation>().Property(x => x.ReopenReason).HasMaxLength(500);
        modelBuilder.Entity<BankReconciliation>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<BankReconciliation>().HasIndex(x => new { x.BuildingId, x.AccountId, x.StatementDate });
        // A lo sumo una conciliacion abierta por cuenta.
        modelBuilder.Entity<BankReconciliation>().HasIndex(x => x.AccountId).IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 'Open'");
        modelBuilder.Entity<BankReconciliation>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BankReconciliation>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BankReconciliation>()
            .HasOne(x => x.Account).WithMany()
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BankReconciledMovement>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<BankReconciledMovement>().Property(x => x.Description).HasMaxLength(300);
        // Un movimiento del libro no se concilia dos veces (entre las marcas vigentes).
        modelBuilder.Entity<BankReconciledMovement>().HasIndex(x => new { x.BuildingId, x.AccountId, x.SourceType, x.SourceId }).IsUnique()
            .HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<BankReconciledMovement>().HasIndex(x => x.ReconciliationId);
        modelBuilder.Entity<BankReconciledMovement>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BankReconciledMovement>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BankReconciledMovement>()
            .HasOne(x => x.Account).WithMany()
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BankReconciledMovement>()
            .HasOne(x => x.Reconciliation).WithMany(x => x.Movements)
            .HasForeignKey(x => x.ReconciliationId).OnDelete(DeleteBehavior.Restrict);

        // ── Centro de configuracion: proveedores, cuentas por pagar e IVA ──────
        modelBuilder.Entity<Supplier>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Supplier>().Property(x => x.Ruc).HasMaxLength(20);
        modelBuilder.Entity<Supplier>().Property(x => x.Phone).HasMaxLength(40);
        modelBuilder.Entity<Supplier>().Property(x => x.Email).HasMaxLength(160);
        modelBuilder.Entity<Supplier>().Property(x => x.Address).HasMaxLength(300);
        // El RUC es unico por empresa cuando se informa; el nombre se busca mucho.
        modelBuilder.Entity<Supplier>().HasIndex(x => new { x.CompanyId, x.Ruc }).IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Ruc] IS NOT NULL");
        modelBuilder.Entity<Supplier>().HasIndex(x => new { x.CompanyId, x.Name });
        modelBuilder.Entity<Supplier>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BuildingExpense>().Property(x => x.InvoiceNumber).HasMaxLength(50);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.InvoiceTimbrado).HasMaxLength(20);
        modelBuilder.Entity<BuildingExpense>().Property(x => x.VatRate).HasColumnType("decimal(5,2)");
        modelBuilder.Entity<BuildingExpense>().HasIndex(x => new { x.BuildingId, x.DueDate });
        modelBuilder.Entity<BuildingExpense>()
            .HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingExpense>()
            .HasOne(x => x.PaidFromAccount).WithMany()
            .HasForeignKey(x => x.PaidFromAccountId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LedgerCategory>().Property(x => x.VatTreatment).HasConversion<string>().HasMaxLength(20);

        // ── Centro de configuracion: mora por unidad y reglas de avisos ────────
        modelBuilder.Entity<Unit>().Property(x => x.LateFeeExemptReason).HasMaxLength(300);

        modelBuilder.Entity<BuildingNoticeRule>().Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        // Una regla por tipo de aviso y edificio.
        modelBuilder.Entity<BuildingNoticeRule>().HasIndex(x => new { x.BuildingId, x.Kind }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<BuildingNoticeRule>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BuildingNoticeRule>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        // ── Centro de configuracion: cierre de periodo ─────────────────────────
        modelBuilder.Entity<FinancePeriodClosure>().Ignore(x => x.IsActive);
        modelBuilder.Entity<FinancePeriodClosure>().Property(x => x.ReopenReason).HasMaxLength(500);
        // Un solo cierre vigente (sin reabrir) por edificio y mes; los reabiertos quedan como historial.
        modelBuilder.Entity<FinancePeriodClosure>().HasIndex(x => new { x.BuildingId, x.Year, x.Month }).IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ReopenedAtUtc] IS NULL");
        modelBuilder.Entity<FinancePeriodClosure>().HasIndex(x => new { x.BuildingId, x.Year, x.Month, x.ReopenedAtUtc });
        modelBuilder.Entity<FinancePeriodClosure>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FinancePeriodClosure>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        MarketplaceModelConfiguration.Configure(modelBuilder, Database.IsSqlServer());

        // ── AdCampaign (publicidad) ────────────────────────────────────────────
        modelBuilder.Entity<AdCampaign>().Property(x => x.AdvertiserName).HasMaxLength(200);
        modelBuilder.Entity<AdCampaign>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<AdCampaign>().Property(x => x.CtaText).HasMaxLength(60);
        modelBuilder.Entity<AdCampaign>().Property(x => x.CtaUrl).HasMaxLength(500);
        modelBuilder.Entity<AdCampaign>().Property(x => x.ImageUrl).HasMaxLength(500);
        modelBuilder.Entity<AdCampaign>().Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<AdCampaign>().Property(x => x.MonthlyAmount).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<AdCampaign>().HasIndex(x => new { x.CompanyId, x.IsActive, x.StartDate, x.EndDate });
        modelBuilder.Entity<AdCampaign>()
            .HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AdCampaign>()
            .HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AdCampaignBuilding>()
            .HasIndex(x => new { x.AdCampaignId, x.BuildingId }).IsUnique();
        modelBuilder.Entity<AdCampaignBuilding>()
            .HasOne(x => x.AdCampaign).WithMany(x => x.Buildings)
            .HasForeignKey(x => x.AdCampaignId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AdCampaignBuilding>()
            .HasOne(x => x.Building).WithMany()
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);

        SeedCatalog(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChangesForSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PrepareChangesForSave();
        return base.SaveChangesAsync(cancellationToken);
    }

    // Sellos de fecha y reglas previas al guardado: valen igual para el guardado sincrono y el asincrono.
    private void PrepareChangesForSave()
    {
        var utcNow = DateTime.UtcNow;

        // La auditoria del marketplace es de solo insercion: nunca se modifica ni se borra un evento ya registrado.
        if (ChangeTracker.Entries<MarketplaceEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Los eventos de auditoría del marketplace no se pueden modificar ni eliminar.");
        }

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
