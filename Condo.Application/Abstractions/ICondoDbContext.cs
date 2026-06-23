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
    DbSet<BuildingIncome> BuildingIncomes { get; }
    DbSet<ExpenseCharge> ExpenseCharges { get; }
    DbSet<ExpenseSettlement> ExpenseSettlements { get; }
    DbSet<ExpensePeriod> ExpensePeriods { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Unit> Units { get; }
    DbSet<Resident> Residents { get; }
    DbSet<UnitResident> UnitResidents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
