using Condo.Api.Services;
using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Tests.Finance;

/// <summary>Copiar el plan de cuentas de un edificio a otro, ahora con cualquier cantidad de niveles.</summary>
public class FinancePlanCopierTests : IDisposable
{
    private readonly FinanceEnv _env = new();

    public void Dispose() => _env.Dispose();

    private static PlanRowSpec Spec(string code, string name, string? parent, LedgerCategoryType type,
        BuildingExpenseCategory? expense = null, string? key = null, string? external = null, bool active = true) =>
        new(code, name, parent, type, external, active, key, expense, null);

    private static List<PlanRowSpec> DeepPlan() =>
    [
        Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
        Spec("5.1", "Servicios", "5", LedgerCategoryType.Expense),
        Spec("5.1.A", "Energía", "5.1", LedgerCategoryType.Expense),
        Spec("5.1.A.1", "ANDE edificio", "5.1.A", LedgerCategoryType.Expense, BuildingExpenseCategory.Ande, key: "Expense.Ande", external: "620101"),
        Spec("5.1.A.2", "ANDE cochera", "5.1.A", LedgerCategoryType.Expense, BuildingExpenseCategory.Ande, active: false),
        Spec("1", "ACTIVO", null, LedgerCategoryType.Asset),
        Spec("1.1", "Caja", "1", LedgerCategoryType.Asset)
    ];

    private async Task<Condo.Domain.Entities.Building> SourceWithDeepPlan()
    {
        var source = _env.T.AddBuilding(_env.Company, "Origen");
        await _env.PlanService().ReplaceAsync(source.Id, _env.Company.Id, DeepPlan(), default);
        return source;
    }

    [Fact]
    public async Task CopiaTodosLosNivelesConSuFuncionYCodigoDelContador()
    {
        var source = await SourceWithDeepPlan();
        var copier = new FinancePlanCopier(_env.T.Db);

        var result = await copier.CopyAsync(source.Id, _env.Building.Id, _env.Company.Id, default);
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(7, result.Created);
        Assert.Equal(0, result.Skipped);
        var cats = _env.AllCats();
        var ande = cats.Single(c => c.Code == "5.1.A.1");
        Assert.Equal(cats.Single(c => c.Code == "5.1.A").Id, ande.ParentId);
        Assert.Equal(cats.Single(c => c.Code == "5.1").Id, cats.Single(c => c.Code == "5.1.A").ParentId);
        Assert.Equal("Expense.Ande", ande.SystemKey);
        Assert.Equal("620101", ande.ExternalCode);
        Assert.False(cats.Single(c => c.Code == "5.1.A.2").IsActive);
        Assert.Equal(LedgerCategoryType.Asset, cats.Single(c => c.Code == "1.1").Type);
    }

    [Fact]
    public async Task CopiarDosVecesNoDuplicaNada()
    {
        var source = await SourceWithDeepPlan();
        var copier = new FinancePlanCopier(_env.T.Db);
        await copier.CopyAsync(source.Id, _env.Building.Id, _env.Company.Id, default);
        await _env.T.Db.SaveChangesAsync();

        var second = await new FinancePlanCopier(_env.T.NewContext()).CopyAsync(source.Id, _env.Building.Id, _env.Company.Id, default);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(7, _env.AllCats().Count);
    }

    [Fact]
    public async Task ActualizaNombreYEstadoDeLasCuentasQueYaExisten()
    {
        var source = await SourceWithDeepPlan();
        await _env.PlanService().ReplaceAsync(_env.Building.Id, _env.Company.Id,
        [
            Spec("5", "EGRESOS", null, LedgerCategoryType.Expense),
            Spec("5.1", "Servicios", "5", LedgerCategoryType.Expense),
            Spec("5.1.A", "Energía vieja", "5.1", LedgerCategoryType.Expense),
            Spec("5.1.A.2", "Otro nombre", "5.1.A", LedgerCategoryType.Expense, BuildingExpenseCategory.Other)
        ], default);

        var db = _env.T.NewContext();
        var result = await new FinancePlanCopier(db).CopyAsync(source.Id, _env.Building.Id, _env.Company.Id, default);
        await db.SaveChangesAsync();

        Assert.Equal("Energía", _env.Cat("5.1.A").Name);
        Assert.Equal("ANDE cochera", _env.Cat("5.1.A.2").Name);
        Assert.False(_env.Cat("5.1.A.2").IsActive);
        Assert.Equal(BuildingExpenseCategory.Ande, _env.Cat("5.1.A.2").ExpenseCategory);
        Assert.True(result.Updated >= 2);
    }

    [Fact]
    public async Task NoCopiaUnaCuentaSiElCodigoYaLoUsaOtraDeOtroTipo()
    {
        var source = await SourceWithDeepPlan();
        await _env.PlanService().ReplaceAsync(_env.Building.Id, _env.Company.Id,
        [
            Spec("1", "INGRESOS", null, LedgerCategoryType.Income),     // el codigo 1 es de otro tipo en el destino
            Spec("1.1", "Cuotas", "1", LedgerCategoryType.Income)
        ], default);

        var db = _env.T.NewContext();
        var result = await new FinancePlanCopier(db).CopyAsync(source.Id, _env.Building.Id, _env.Company.Id, default);
        await db.SaveChangesAsync();

        Assert.True(result.Skipped >= 2);
        Assert.Contains(result.Messages, m => m.Contains("otro tipo"));
        Assert.Equal(LedgerCategoryType.Income, _env.Cat("1").Type);
        Assert.Equal("Cuotas", _env.Cat("1.1").Name);
    }

    [Fact]
    public async Task ElOrigenNoSeTocaYLosOtrosEdificiosNoRecibenNada()
    {
        var source = await SourceWithDeepPlan();
        var third = _env.T.AddBuilding(_env.Company, "Tercero");
        var copier = new FinancePlanCopier(_env.T.Db);
        await copier.CopyAsync(source.Id, _env.Building.Id, _env.Company.Id, default);
        await _env.T.Db.SaveChangesAsync();

        var db = _env.T.NewContext();
        Assert.Equal(7, db.LedgerCategories.Count(c => !c.IsDeleted && c.BuildingId == source.Id));
        Assert.Equal(0, db.LedgerCategories.Count(c => !c.IsDeleted && c.BuildingId == third.Id));
    }
}
