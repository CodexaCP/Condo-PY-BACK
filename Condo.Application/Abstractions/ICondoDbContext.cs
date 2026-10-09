using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Condo.Application.Abstractions;

public interface ICondoDbContext
{
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    DbSet<Company> Companies { get; }
    DbSet<Condominium> Condominiums { get; }
    DbSet<ApplicationUser> ApplicationUsers { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<UserBuildingAccess> UserBuildingAccesses { get; }
    DbSet<Building> Buildings { get; }
    DbSet<BuildingExpense> BuildingExpenses { get; }
    DbSet<BuildingExpenseCreditNote> BuildingExpenseCreditNotes { get; }
    DbSet<BuildingExpenseCreditNoteAllocation> BuildingExpenseCreditNoteAllocations { get; }
    DbSet<RecurringBuildingExpense> RecurringBuildingExpenses { get; }
    DbSet<BuildingIncome> BuildingIncomes { get; }
    DbSet<ExpenseCharge> ExpenseCharges { get; }
    DbSet<ExpenseSettlement> ExpenseSettlements { get; }
    DbSet<ExpensePeriod> ExpensePeriods { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<Unit> Units { get; }
    DbSet<Resident> Residents { get; }
    DbSet<UnitResident> UnitResidents { get; }
    DbSet<UnitOwner> UnitOwners { get; }
    DbSet<Claim> Claims { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<Vote> Votes { get; }
    DbSet<VoteOption> VoteOptions { get; }
    DbSet<VoteCast> VoteCasts { get; }
    DbSet<OwnerPayment> OwnerPayments { get; }
    DbSet<OwnerPaymentUnit> OwnerPaymentUnits { get; }
    DbSet<OwnerCredit> OwnerCredits { get; }
    DbSet<OwnerCreditMovement> OwnerCreditMovements { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<Amenity> Amenities { get; }
    DbSet<AmenityReservation> AmenityReservations { get; }
    DbSet<Plan> Plans { get; }
    DbSet<BuildingPlan> BuildingPlans { get; }
    DbSet<BuildingPlanPayment> BuildingPlanPayments { get; }
    DbSet<InvoiceSeries> InvoiceSeries { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceAuditLog> InvoiceAuditLogs { get; }
    DbSet<CreditNote> CreditNotes { get; }
    DbSet<CreditNoteLine> CreditNoteLines { get; }
    DbSet<CreditNoteAttachment> CreditNoteAttachments { get; }
    DbSet<CreditNoteAuditLog> CreditNoteAuditLogs { get; }
    DbSet<FinanceSettings> FinanceSettings { get; }
    DbSet<FinancialAccount> FinancialAccounts { get; }
    DbSet<BuildingBankAccount> BuildingBankAccounts { get; }
    DbSet<LedgerCategory> LedgerCategories { get; }
    DbSet<BudgetLine> BudgetLines { get; }
    DbSet<FinanceAuditLog> FinanceAuditLogs { get; }
    DbSet<FinancePeriodClosure> FinancePeriodClosures { get; }
    DbSet<BuildingNoticeRule> BuildingNoticeRules { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<BankReconciliation> BankReconciliations { get; }
    DbSet<BankReconciledMovement> BankReconciledMovements { get; }
    DbSet<MarketplaceListing> MarketplaceListings { get; }
    DbSet<MarketplaceReservation> MarketplaceReservations { get; }
    DbSet<MarketplaceReservationSlot> MarketplaceReservationSlots { get; }
    DbSet<MarketplacePayment> MarketplacePayments { get; }
    DbSet<MarketplaceAccountMovement> MarketplaceAccountMovements { get; }
    DbSet<MarketplaceEvent> MarketplaceEvents { get; }
    DbSet<MarketplaceRefund> MarketplaceRefunds { get; }
    DbSet<MarketplaceClaim> MarketplaceClaims { get; }
    DbSet<MarketplaceOwnerDebt> MarketplaceOwnerDebts { get; }
    DbSet<MarketplaceHandoverNote> MarketplaceHandoverNotes { get; }
    DbSet<AdCampaign> AdCampaigns { get; }
    DbSet<AdCampaignBuilding> AdCampaignBuildings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
