using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Tests.Finance;

/// <summary>Un edificio con Finanzas habilitado (plan que lo incluye) y el controlador del plan de cuentas armado con el modelo real.</summary>
internal sealed class FinanceEnv : IDisposable
{
    public readonly TestDb T;
    public readonly FakeTenantContext Tenant = new() { Role = "SuperAdmin" };
    public readonly FakeAccessScope Access;
    public readonly Company Company;
    public readonly Building Building;
    public readonly ApplicationUser Admin;
    public readonly ExpensePeriod Period;

    public FinanceEnv(bool sqlServer = false)
    {
        T = new TestDb(sqlServer);
        Access = new FakeAccessScope(Tenant);
        Company = T.AddCompany("Empresa Finanzas");
        Admin = T.AddUser(Company, UserRole.CompanyAdmin);
        Building = T.AddBuilding(Company, "Edificio Finanzas");
        Building.FinanceModuleEnabled = true;

        var plan = new Plan { Name = "Plan con finanzas", Description = "x", BillingCycle = BillingCycle.Monthly, IncludesFinanceModule = true };
        T.Db.Plans.Add(plan);
        T.Db.BuildingPlans.Add(new BuildingPlan
        {
            PlanId = plan.Id, BuildingId = Building.Id, ScopeEntityId = Building.Id,
            StartDate = DateTime.UtcNow.AddDays(-10), EndDate = DateTime.UtcNow.AddDays(300), AssignedById = Admin.Id
        });
        Period = new ExpensePeriod
        {
            CompanyId = Company.Id, BuildingId = Building.Id, Year = 2026, Month = 10, Name = "Octubre 2026",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), DueDate = new DateOnly(2026, 11, 10)
        };
        T.Db.ExpensePeriods.Add(Period);
        T.Db.SaveChanges();
    }

    public FinanceCategoriesController Controller() =>
        new(T.Db, Access, Tenant, new FinanceModuleGate(T.Db), new FinancePlanCopier(T.Db), new FinancePlanService(T.Db));

    public FinancePlanService PlanService() => new(T.Db);

    // "Ayer" segun el reloj del libro (UTC-3): los movimientos de prueba tienen que caer antes de hoy para contar.
    public static DateOnly Yesterday => FinancePeriods.Today().AddDays(-1);

    /// <summary>Deja la configuracion inicial completa (fecha de arranque dos meses atras, un banco) para poder armar reportes.</summary>
    public FinancialAccount Setup()
    {
        T.Db.FinanceSettings.Add(new FinanceSettings
        {
            CompanyId = Company.Id, BuildingId = Building.Id, FinanceStartDate = FinancePeriods.Today().AddMonths(-2), SetupCompleted = true
        });
        var bank = new FinancialAccount { CompanyId = Company.Id, BuildingId = Building.Id, Name = "Banco", Type = FinancialAccountType.Bank };
        T.Db.FinancialAccounts.Add(bank);
        T.Db.SaveChanges();
        return bank;
    }

    /// <summary>Siembra el plan generico en el edificio (como hace el alta del modulo).</summary>
    public List<LedgerCategory> SeedTemplate()
    {
        var entities = FinanceChartTemplate.CreateEntities(Building.Id, Company.Id).ToList();
        T.Db.LedgerCategories.AddRange(entities);
        T.Db.SaveChanges();
        return entities;
    }

    public LedgerCategory Cat(string code) =>
        T.NewContext().LedgerCategories.Single(x => !x.IsDeleted && x.BuildingId == Building.Id && x.Code == code);

    public List<LedgerCategory> AllCats() =>
        T.NewContext().LedgerCategories.Where(x => !x.IsDeleted && x.BuildingId == Building.Id).ToList();

    public BuildingExpense AddExpense(Guid? categoryId, decimal amount = 100_000m, BuildingExpenseCategory category = BuildingExpenseCategory.Other, DateOnly? date = null)
    {
        var expense = new BuildingExpense
        {
            CompanyId = Company.Id, BuildingId = Building.Id, ExpensePeriodId = Period.Id, Category = category,
            SupplierName = "Proveedor", Description = "Gasto de prueba", ExpenseDate = date ?? Yesterday, Amount = amount,
            LedgerCategoryId = categoryId
        };
        T.Db.BuildingExpenses.Add(expense);
        T.Db.SaveChanges();
        return expense;
    }

    public BuildingIncome AddIncome(Guid? categoryId, decimal amount = 50_000m, DateOnly? date = null)
    {
        var income = new BuildingIncome
        {
            CompanyId = Company.Id, BuildingId = Building.Id, ExpensePeriodId = Period.Id, Category = BuildingIncomeCategory.Other,
            Description = "Ingreso de prueba", IncomeDate = date ?? Yesterday, Amount = amount, LedgerCategoryId = categoryId
        };
        T.Db.BuildingIncomes.Add(income);
        T.Db.SaveChanges();
        return income;
    }

    public BudgetLine AddBudget(Guid categoryId, decimal amount = 300_000m)
    {
        var line = new BudgetLine { CompanyId = Company.Id, BuildingId = Building.Id, CategoryId = categoryId, Year = Yesterday.Year, Month = Yesterday.Month, Amount = amount };
        T.Db.BudgetLines.Add(line);
        T.Db.SaveChanges();
        return line;
    }

    public void LoginAs(string role) => Tenant.Role = role;

    public void Dispose() => T.Dispose();

    // ── Lectura de resultados de los controladores ────────────────────────────

    public static T Ok<T>(ActionResult<T> result) where T : class =>
        (result.Result as OkObjectResult)?.Value as T
        ?? result.Value
        ?? throw new InvalidOperationException($"Se esperaba OK y llego {Describe(result.Result)}.");

    public static string BadRequestText<T>(ActionResult<T> result) =>
        (result.Result as BadRequestObjectResult)?.Value as string
        ?? throw new InvalidOperationException($"Se esperaba 400 y llego {Describe(result.Result)}.");

    public static string Describe(IActionResult? result) => result switch
    {
        ObjectResult o => $"{o.StatusCode} {o.Value}",
        null => "nada",
        _ => result.GetType().Name
    };
}
