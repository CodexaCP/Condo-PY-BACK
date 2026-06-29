using Condo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Condo.Application.Abstractions;

public interface ICondoDbContext
{
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
