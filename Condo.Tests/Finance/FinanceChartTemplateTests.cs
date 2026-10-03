using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Tests.Finance;

/// <summary>El plan generico de CondoPY (el plan de cuentas de Tony): estructura, categorias de la liquidacion, funciones y activacion por defecto.</summary>
public class FinanceChartTemplateTests
{
    private static readonly IReadOnlyList<FinanceChartTemplate.Node> Nodes = FinanceChartTemplate.Nodes;

    [Fact]
    public void TieneLasCincoClasesYLasCuentasDelPlan()
    {
        Assert.Equal(5, Nodes.Count(n => n.ParentCode is null));
        Assert.Equal(["1", "2", "3", "4", "5"], Nodes.Where(n => n.ParentCode is null).Select(n => n.Code).OrderBy(c => c));
        Assert.True(Nodes.Count > 190, $"El plan tiene {Nodes.Count} nodos.");

        string Name(string code) => Nodes.Single(n => n.Code == code).Name;
        Assert.Equal("ACTIVO", Name("1"));
        Assert.Equal("Caja administración", Name("1.1.01"));
        Assert.Equal("(-) Previsión para incobrables", Name("1.3.09"));
        Assert.Equal("Préstamos", Name("2.5.05"));
        Assert.Equal("Resultado del período", Name("3.2.02"));
        Assert.Equal("Diferencia de cambio positiva", Name("4.3.08"));
        Assert.Equal("Gestiones MTESS / IPS (planillas laborales)", Name("5.07.08"));
        Assert.Equal("Gastos varios", Name("5.15.03"));
    }

    [Fact]
    public void LasCifrasQueUsaLaGuiaDePruebas()
    {
        // La guia de pruebas manuales cita estos numeros: si el plan cambia, hay que actualizar la guia.
        var parents = Nodes.Where(n => n.ParentCode is not null).Select(n => n.ParentCode!).ToHashSet();
        bool Leaf(FinanceChartTemplate.Node n) => !parents.Contains(n.Code);

        Assert.Equal(197, Nodes.Count);
        Assert.Equal(30, Nodes.Count(n => n.ParentCode is not null && parents.Contains(n.Code)));   // grupos
        Assert.Equal(162, Nodes.Count(Leaf));                                                        // cuentas finales
        Assert.Equal(54, Nodes.Count(n => n.Core));
        Assert.Equal(34, Nodes.Count(n => n.Type == LedgerCategoryType.Asset && Leaf(n)));
        Assert.Equal(22, Nodes.Count(n => n.Type == LedgerCategoryType.Liability && Leaf(n)));
        Assert.Equal(6, Nodes.Count(n => n.Type == LedgerCategoryType.Fund && Leaf(n)));
        Assert.Equal(15, Nodes.Count(n => n.Type == LedgerCategoryType.Income && Leaf(n)));
        Assert.Equal(85, Nodes.Count(n => n.Type == LedgerCategoryType.Expense && Leaf(n)));

        var active = FinanceChartTemplate.Specs.Where(s => s.IsActive).ToList();
        Assert.Equal(81, active.Count);
        Assert.Equal(28, active.Count(s => s.Type == LedgerCategoryType.Expense && !parents.Contains(s.Code)));
        Assert.Equal(10, active.Count(s => s.Type == LedgerCategoryType.Income && !parents.Contains(s.Code)));
    }

    [Fact]
    public void LasClasesDefinenElTipo()
    {
        foreach (var n in Nodes)
        {
            var expected = n.Code[0] switch
            {
                '1' => LedgerCategoryType.Asset,
                '2' => LedgerCategoryType.Liability,
                '3' => LedgerCategoryType.Fund,
                '4' => LedgerCategoryType.Income,
                _ => LedgerCategoryType.Expense
            };
            Assert.True(n.Type == expected, $"{n.Code} debería ser {expected} y es {n.Type}.");
        }
    }

    [Fact]
    public void CodigosUnicosYPadresCoherentes()
    {
        Assert.Equal(Nodes.Count, Nodes.Select(n => n.Code).Distinct().Count());
        var codes = Nodes.Select(n => n.Code).ToHashSet();
        foreach (var n in Nodes.Where(n => n.ParentCode is not null))
        {
            Assert.Contains(n.ParentCode!, codes);
            Assert.Equal(n.Code[..n.Code.LastIndexOf('.')], n.ParentCode);
        }

        Assert.All(Nodes, n => Assert.True(n.Code.Split('.').Length <= 3));
    }

    [Fact]
    public void LasCuentasFinalesDeGastosEIngresosTienenCategoriaDeLiquidacion()
    {
        var parents = Nodes.Where(n => n.ParentCode is not null).Select(n => n.ParentCode!).ToHashSet();
        foreach (var n in Nodes)
        {
            var isLeaf = !parents.Contains(n.Code);
            if (!isLeaf)
            {
                Assert.Null(n.ExpenseCategory);
                Assert.Null(n.IncomeCategory);
                continue;
            }

            if (n.Type == LedgerCategoryType.Expense)
            {
                Assert.True(n.ExpenseCategory.HasValue, $"{n.Code} {n.Name} no tiene categoría de gasto.");
                Assert.NotEqual(BuildingExpenseCategory.ReserveFund, n.ExpenseCategory);
            }
            else if (n.Type == LedgerCategoryType.Income)
            {
                var collection = n.SystemKey?.StartsWith("Collection.", StringComparison.Ordinal) == true;
                Assert.Equal(!collection, n.IncomeCategory.HasValue);
            }
            else
            {
                Assert.Null(n.ExpenseCategory);
                Assert.Null(n.IncomeCategory);
            }
        }
    }

    [Theory]
    [InlineData("5.01.01", BuildingExpenseCategory.Payroll)]
    [InlineData("5.01.10", BuildingExpenseCategory.Payroll)]
    [InlineData("5.02.01", BuildingExpenseCategory.Ande)]
    [InlineData("5.02.02", BuildingExpenseCategory.Essap)]
    [InlineData("5.02.03", BuildingExpenseCategory.Utilities)]
    [InlineData("5.02.04", BuildingExpenseCategory.InternetPhone)]
    [InlineData("5.03.01", BuildingExpenseCategory.Elevator)]
    [InlineData("5.03.02", BuildingExpenseCategory.Maintenance)]
    [InlineData("5.03.03", BuildingExpenseCategory.Cleaning)]
    [InlineData("5.03.06", BuildingExpenseCategory.Security)]
    [InlineData("5.03.11", BuildingExpenseCategory.Security)]
    [InlineData("5.03.14", BuildingExpenseCategory.Administration)]
    [InlineData("5.04.09", BuildingExpenseCategory.Elevator)]
    [InlineData("5.04.11", BuildingExpenseCategory.Maintenance)]
    [InlineData("5.05.01", BuildingExpenseCategory.Cleaning)]
    [InlineData("5.05.02", BuildingExpenseCategory.Supplies)]
    [InlineData("5.06.02", BuildingExpenseCategory.Security)]
    [InlineData("5.07.06", BuildingExpenseCategory.Administration)]
    [InlineData("5.08.03", BuildingExpenseCategory.Insurance)]
    [InlineData("5.09.05", BuildingExpenseCategory.Taxes)]
    [InlineData("5.10.01", BuildingExpenseCategory.Administration)]
    [InlineData("5.11.01", BuildingExpenseCategory.Other)]
    [InlineData("5.12.03", BuildingExpenseCategory.Supplies)]
    [InlineData("5.12.04", BuildingExpenseCategory.Payroll)]
    [InlineData("5.13.03", BuildingExpenseCategory.Extraordinary)]
    [InlineData("5.14.01", BuildingExpenseCategory.Other)]
    [InlineData("5.15.03", BuildingExpenseCategory.Other)]
    public void CategoriaDeLaLiquidacionDeCadaCuentaDeGastos(string code, BuildingExpenseCategory expected) =>
        Assert.Equal(expected, Nodes.Single(n => n.Code == code).ExpenseCategory);

    [Theory]
    [InlineData("4.2.02", BuildingIncomeCategory.Other)]
    [InlineData("4.2.03", BuildingIncomeCategory.Other)]
    [InlineData("4.3.01", BuildingIncomeCategory.CommonAreaRental)]
    [InlineData("4.3.02", BuildingIncomeCategory.CommonAreaRental)]
    [InlineData("4.3.03", BuildingIncomeCategory.CommonAreaRental)]
    [InlineData("4.3.04", BuildingIncomeCategory.Interest)]
    [InlineData("4.3.05", BuildingIncomeCategory.Other)]
    [InlineData("4.3.08", BuildingIncomeCategory.Other)]
    public void CategoriaDeLaLiquidacionDeCadaCuentaDeIngresos(string code, BuildingIncomeCategory expected) =>
        Assert.Equal(expected, Nodes.Single(n => n.Code == code).IncomeCategory);

    [Fact]
    public void LasFuncionesEspecialesSonValidasYUnicas()
    {
        var withKey = Nodes.Where(n => n.SystemKey is not null).ToList();
        Assert.Equal(withKey.Count, withKey.Select(n => n.SystemKey).Distinct().Count());

        foreach (var n in withKey)
        {
            var role = FinanceChartTemplate.FindRole(n.SystemKey);
            Assert.NotNull(role);
            Assert.Equal(role!.Type, n.Type);
            if (role.ExpenseCategory.HasValue) Assert.Equal(role.ExpenseCategory, n.ExpenseCategory);
            if (role.IncomeCategory.HasValue) Assert.Equal(role.IncomeCategory, n.IncomeCategory);
        }

        // Las cinco cuentas de cobranza de expensas y la cuenta por defecto de cada categoria de gasto.
        string[] collection = ["Collection.Ordinary", "Collection.Extraordinary", "Collection.IndividualAdjustment", "Collection.LateFee", "Collection.ReserveFund"];
        Assert.All(collection, key => Assert.Contains(withKey, n => n.SystemKey == key));
        foreach (var c in Enum.GetValues<BuildingExpenseCategory>().Where(c => c != BuildingExpenseCategory.ReserveFund))
        {
            Assert.Contains(withKey, n => n.SystemKey == FinanceChartTemplate.ExpenseKey(c));
        }

        Assert.Equal("4.1.01", withKey.Single(n => n.SystemKey == "Collection.Ordinary").Code);
        Assert.Equal("4.2.01", withKey.Single(n => n.SystemKey == "Collection.LateFee").Code);
        Assert.Equal("5.02.01", withKey.Single(n => n.SystemKey == "Expense.Ande").Code);
    }

    [Fact]
    public void LasCuentasDeCobranzaNoRecibenGastosNiIngresosCargadosAMano()
    {
        var entities = FinanceChartTemplate.CreateEntities(Guid.NewGuid(), Guid.NewGuid());
        var collection = entities.Where(e => e.SystemKey?.StartsWith("Collection.", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(5, collection.Count);
        Assert.All(collection, c => Assert.False(FinanceChartTemplate.CanReceiveMovements(c)));
    }

    [Fact]
    public void ActivacionPorDefecto_UnaCuentaActivaTieneSuGrupoActivo()
    {
        var specs = FinanceChartTemplate.Specs.ToDictionary(s => s.Code);
        foreach (var s in specs.Values.Where(s => s.IsActive && s.ParentCode is not null))
        {
            Assert.True(specs[s.ParentCode!].IsActive, $"{s.Code} está activa pero su grupo {s.ParentCode} no.");
        }

        Assert.True(specs["5.02.01"].IsActive);
        Assert.True(specs["4.1.01"].IsActive);
        Assert.False(specs["5.03.07"].IsActive);
        Assert.False(specs["1.2"].IsActive);
        Assert.False(specs["1.2.01"].IsActive);
        Assert.True(specs["5.02"].IsActive);
        Assert.True(specs["5"].IsActive);
    }

    [Fact]
    public void SoloLasHojasActivasDeIngresosYGastosSePuedenElegir()
    {
        var entities = FinanceChartTemplate.CreateEntities(Guid.NewGuid(), Guid.NewGuid()).ToList();
        var byId = entities.ToDictionary(e => e.Id);
        string Code(LedgerCategory c) => c.Code;

        var expenses = FinanceChartTemplate.AssignableCategories(entities, LedgerCategoryType.Expense);
        Assert.All(expenses, e => Assert.Equal(LedgerCategoryType.Expense, e.Type));
        Assert.Contains("5.02.01", expenses.Select(Code));
        Assert.DoesNotContain("5.03.07", expenses.Select(Code));       // no recomendada: nace inactiva
        Assert.DoesNotContain("5.02", expenses.Select(Code));          // un grupo no se elige
        Assert.Equal(expenses.Select(Code).OrderBy(c => c, StringComparer.Ordinal), expenses.Select(Code));

        var incomes = FinanceChartTemplate.AssignableCategories(entities, LedgerCategoryType.Income);
        Assert.Contains("4.3.01", incomes.Select(Code));
        Assert.Contains("4.2.02", incomes.Select(Code));
        Assert.DoesNotContain("4.1.01", incomes.Select(Code));         // cobranza de expensas
        Assert.DoesNotContain("4.2.01", incomes.Select(Code));         // mora cobrada

        // Activo, pasivo y patrimonio son de referencia.
        Assert.Empty(FinanceChartTemplate.AssignableCategories(entities, LedgerCategoryType.Asset));
        Assert.Empty(FinanceChartTemplate.AssignableCategories(entities, LedgerCategoryType.Liability));
        Assert.Empty(FinanceChartTemplate.AssignableCategories(entities, LedgerCategoryType.Fund));
        Assert.NotEmpty(byId);
    }

    [Fact]
    public void CreateEntities_EnlazaCadaCuentaConSuGrupo()
    {
        var entities = FinanceChartTemplate.CreateEntities(Guid.NewGuid(), Guid.NewGuid()).ToDictionary(e => e.Code);
        Assert.Equal(Nodes.Count, entities.Count);
        Assert.Null(entities["5"].ParentId);
        Assert.Equal(entities["5"].Id, entities["5.02"].ParentId);
        Assert.Equal(entities["5.02"].Id, entities["5.02.01"].ParentId);
        Assert.Equal(LedgerCategoryType.Asset, entities["1.1.03"].Type);
        Assert.Equal(BuildingExpenseCategory.Ande, entities["5.02.01"].ExpenseCategory);
    }

    [Fact]
    public void NombresDeRespaldoParaLasFuncionesSinCuenta()
    {
        Assert.Equal("ANDE (sin rubro)", FinanceChartTemplate.FallbackName("Expense.Ande"));
        Assert.Equal("Cobro de expensas ordinarias", FinanceChartTemplate.FallbackName("Collection.Ordinary"));
        Assert.Equal("Fondo de reserva (sin rubro)", FinanceChartTemplate.FallbackName("Expense.ReserveFund"));
        Assert.Equal("Rubro.abc", FinanceChartTemplate.FallbackName("Rubro.abc"));
    }
}
