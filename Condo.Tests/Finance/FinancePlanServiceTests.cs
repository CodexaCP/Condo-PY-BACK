using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>Aplicar un plan a un edificio (reemplazar o agregar), contra el modelo real con indices unicos y claves foraneas.</summary>
public class FinancePlanServiceTests : IDisposable
{
    private readonly FinanceEnv _env = new();

    public void Dispose() => _env.Dispose();

    private static PlanRowSpec Spec(string code, string name, string? parent, LedgerCategoryType type, bool active = true, string? key = null,
        BuildingExpenseCategory? expense = null, BuildingIncomeCategory? income = null, string? external = null) =>
        new(code, name, parent, type, external, active, key, expense, income);

    private static List<PlanRowSpec> ClientPlan() =>
    [
        Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
        Spec("5.1", "Servicios", "5", LedgerCategoryType.Expense),
        Spec("5.1.1", "Luz", "5.1", LedgerCategoryType.Expense, expense: BuildingExpenseCategory.Ande, external: "620101"),
        Spec("5.1.2", "Agua", "5.1", LedgerCategoryType.Expense, expense: BuildingExpenseCategory.Essap),
        Spec("4", "INGRESOS", null, LedgerCategoryType.Income),
        Spec("4.1", "Cuotas", "4", LedgerCategoryType.Income),
        Spec("4.1.1", "Alquileres", "4.1", LedgerCategoryType.Income, income: BuildingIncomeCategory.CommonAreaRental)
    ];

    [Fact]
    public async Task Reemplazar_CreaElPlanNuevoDesvinculaGastosYBorraPresupuesto()
    {
        var old = _env.SeedTemplate();
        var ande = old.Single(c => c.Code == "5.02.01");
        var expense = _env.AddExpense(ande.Id, category: BuildingExpenseCategory.Ande);
        var income = _env.AddIncome(old.Single(c => c.Code == "4.3.01").Id);
        _env.AddBudget(ande.Id);

        var service = _env.PlanService();
        var impact = await service.GetImpactAsync(_env.Building.Id, default);
        Assert.Equal(old.Count, impact.Categories);
        Assert.Equal(1, impact.Expenses);
        Assert.Equal(1, impact.Incomes);
        Assert.Equal(1, impact.BudgetLines);
        Assert.True(impact.HasImpact);

        var result = await service.ReplaceAsync(_env.Building.Id, _env.Company.Id, ClientPlan(), default);

        Assert.Equal(7, result.Created);
        Assert.Equal(1, result.UnlinkedExpenses);
        Assert.Equal(1, result.UnlinkedIncomes);
        Assert.Equal(1, result.DeletedBudgetLines);
        Assert.Equal(old.Count, result.RemovedCategories);

        var db = _env.T.NewContext();
        // El plan nuevo reemplaza al anterior (codigos que coinciden, como 4 y 5, no chocan con los borrados).
        var cats = db.LedgerCategories.Where(c => !c.IsDeleted && c.BuildingId == _env.Building.Id).ToList();
        Assert.Equal(7, cats.Count);
        Assert.Equal(old.Count, db.LedgerCategories.Count(c => c.IsDeleted && c.BuildingId == _env.Building.Id));
        Assert.Equal(cats.Single(c => c.Code == "5.1").Id, cats.Single(c => c.Code == "5.1.1").ParentId);
        Assert.Equal("620101", cats.Single(c => c.Code == "5.1.1").ExternalCode);

        // El gasto y el ingreso siguen existiendo, ahora sin rubro; el presupuesto queda borrado.
        Assert.Null(db.BuildingExpenses.Single(e => e.Id == expense.Id).LedgerCategoryId);
        Assert.Null(db.BuildingIncomes.Single(i => i.Id == income.Id).LedgerCategoryId);
        Assert.Equal(1m, db.BuildingExpenses.Count(e => e.Id == expense.Id));
        Assert.Empty(db.BudgetLines.Where(b => !b.IsDeleted && b.BuildingId == _env.Building.Id));
    }

    [Fact]
    public async Task Reemplazar_PorElPlanGenericoDejaTodoElPlanDeTony()
    {
        _env.SeedTemplate();
        await _env.PlanService().ReplaceAsync(_env.Building.Id, _env.Company.Id, FinanceChartTemplate.Specs, default);

        var cats = _env.AllCats();
        Assert.Equal(FinanceChartTemplate.Nodes.Count, cats.Count);
        Assert.Equal(cats.Count, cats.Select(c => c.Code).Distinct().Count());
        Assert.Equal(cats.Count(c => c.SystemKey != null), cats.Where(c => c.SystemKey != null).Select(c => c.SystemKey).Distinct().Count());
        Assert.Equal(FinanceChartTemplate.Specs.Count(s => s.IsActive), cats.Count(c => c.IsActive));
    }

    [Fact]
    public async Task Reemplazar_NoTocaOtrosEdificios()
    {
        var other = _env.T.AddBuilding(_env.Company, "Otro edificio");
        var otherCats = FinanceChartTemplate.CreateEntities(other.Id, _env.Company.Id).ToList();
        _env.T.Db.LedgerCategories.AddRange(otherCats);
        _env.T.Db.SaveChanges();
        _env.SeedTemplate();

        await _env.PlanService().ReplaceAsync(_env.Building.Id, _env.Company.Id, ClientPlan(), default);

        var db = _env.T.NewContext();
        Assert.Equal(otherCats.Count, db.LedgerCategories.Count(c => !c.IsDeleted && c.BuildingId == other.Id));
    }

    [Fact]
    public async Task Reemplazar_EsTodoONada_SiUnaFilaFallaNoSeBorraNada()
    {
        var old = _env.SeedTemplate();
        // Codigo repetido: viola el indice unico y debe deshacer toda la operacion.
        var broken = new List<PlanRowSpec>
        {
            Spec("9", "Uno", null, LedgerCategoryType.Expense),
            Spec("9", "Dos", null, LedgerCategoryType.Expense)
        };

        await Assert.ThrowsAnyAsync<Exception>(() => _env.PlanService().ReplaceAsync(_env.Building.Id, _env.Company.Id, broken, default));

        var db = _env.T.NewContext();
        Assert.Equal(old.Count, db.LedgerCategories.Count(c => !c.IsDeleted && c.BuildingId == _env.Building.Id));
    }

    [Fact]
    public async Task AgregarLoQueFalta_NoTocaLoExistente()
    {
        var old = _env.SeedTemplate();
        var ande = old.Single(c => c.Code == "5.02.01");
        ande.Name = "ANDE (renombrada)";
        _env.T.Db.SaveChanges();

        // Se le saca una cuenta al plan y se vuelve a agregar el generico.
        var missing = old.Single(c => c.Code == "5.03.07");
        missing.IsDeleted = true;
        _env.T.Db.SaveChanges();

        var service = _env.PlanService();
        var result = await service.MergeAsync(_env.Building.Id, _env.Company.Id, FinanceChartTemplate.Specs, updateExisting: false, default);
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal("ANDE (renombrada)", _env.Cat("5.02.01").Name);
        Assert.Equal("Portones automáticos", _env.Cat("5.03.07").Name);
        Assert.Equal(FinanceChartTemplate.Nodes.Count, _env.AllCats().Count);
    }

    [Fact]
    public async Task Actualizar_CambiaNombreCodigoDelContadorYEstado()
    {
        var old = _env.SeedTemplate();
        var specs = FinanceChartTemplate.Specs
            .Select(s => s.Code == "5.04.01" ? s with { Name = "Electricidad y alumbrado", ExternalCode = "6201", IsActive = false } : s)
            .ToList();

        var result = await _env.PlanService().MergeAsync(_env.Building.Id, _env.Company.Id, specs, updateExisting: true, default);
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(1, result.Updated);
        var cat = _env.Cat("5.04.01");
        Assert.Equal("Electricidad y alumbrado", cat.Name);
        Assert.Equal("6201", cat.ExternalCode);
        Assert.False(cat.IsActive);
        Assert.Equal(old.Count, _env.AllCats().Count);
    }

    [Fact]
    public async Task AgregarUnHijoBajoUnaCuentaConMovimientosSeOmite()
    {
        var old = _env.SeedTemplate();
        var ande = old.Single(c => c.Code == "5.02.01");
        _env.AddExpense(ande.Id, category: BuildingExpenseCategory.Ande);

        var specs = new List<PlanRowSpec>
        {
            Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
            Spec("5.02", "Servicios públicos", "5", LedgerCategoryType.Expense),
            Spec("5.02.01", "Electricidad (ANDE)", "5.02", LedgerCategoryType.Expense),
            Spec("5.02.01.1", "Medidor 1", "5.02.01", LedgerCategoryType.Expense, expense: BuildingExpenseCategory.Ande)
        };

        var result = await _env.PlanService().MergeAsync(_env.Building.Id, _env.Company.Id, specs, updateExisting: false, default);
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Messages, m => m.Contains("5.02.01") && m.Contains("grupo"));
        Assert.DoesNotContain(_env.AllCats(), c => c.Code == "5.02.01.1");
    }

    [Fact]
    public async Task AgregarUnaFuncionQueYaTieneOtraCuentaSeAgregaSinLaFuncion()
    {
        _env.SeedTemplate();
        var specs = new List<PlanRowSpec>
        {
            Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
            Spec("5.02", "Servicios públicos", "5", LedgerCategoryType.Expense),
            Spec("5.02.90", "Otra luz", "5.02", LedgerCategoryType.Expense, key: "Expense.Ande", expense: BuildingExpenseCategory.Ande)
        };

        var result = await _env.PlanService().MergeAsync(_env.Building.Id, _env.Company.Id, specs, updateExisting: false, default);
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(1, result.Created);
        Assert.Contains(result.Messages, m => m.Contains("5.02.01"));
        Assert.Null(_env.Cat("5.02.90").SystemKey);
        Assert.Equal("5.02.01", _env.AllCats().Single(c => c.SystemKey == "Expense.Ande").Code);
    }

    [Fact]
    public async Task ElImpactoSinMovimientosNiPresupuestoEsCero()
    {
        _env.SeedTemplate();
        var impact = await _env.PlanService().GetImpactAsync(_env.Building.Id, default);
        Assert.False(impact.HasImpact);
        Assert.Equal(FinanceChartTemplate.Nodes.Count, impact.Categories);
    }
}
