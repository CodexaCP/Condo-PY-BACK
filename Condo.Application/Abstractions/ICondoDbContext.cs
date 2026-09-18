using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Condo.Application.Abstractions;

public interface ICondoDbContext
{
    DatabaseFacade Database { get; }
    DbSet<Company> Companies { get; }
    DbSet<Condominium> Condominiums { get; }
    DbSet<ApplicationUser> ApplicationUsers { get; }
    DbSet<UserBuildingAccess> UserBuildingAccesses { get; }
    DbSet<Building> Buildings { get; }
    DbSet<BuildingExpense> BuildingExpenses { get; }
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
    DbSet<Notification> Notifications { get; }
    DbSet<Amenity> Amenities { get; }
    DbSet<AmenityReservation> AmenityReservations { get; }
    DbSet<Plan> Plans { get; }
    DbSet<BuildingPlan> BuildingPlans { get; }
    DbSet<BuildingPlanPayment> BuildingPlanPayments { get; }
    DbSet<InvoiceSeries> InvoiceSeries { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceAuditLog> InvoiceAuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
