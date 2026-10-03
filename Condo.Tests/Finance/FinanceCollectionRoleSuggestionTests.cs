using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Tests.Finance;

/// <summary>Sugerencia de la funcion de cobranza por el nombre de una cuenta de ingresos.</summary>
public class FinanceCollectionRoleSuggestionTests
{
    [Theory]
    [InlineData("Expensas ordinarias", "Collection.Ordinary")]
    [InlineData("Cuotas de expensas comunes", "Collection.Ordinary")]
    [InlineData("Expensas extraordinarias", "Collection.Extraordinary")]
    [InlineData("Aporte extraordinario", "Collection.Extraordinary")]
    [InlineData("Intereses por mora", "Collection.LateFee")]
    [InlineData("Recargo punitorio", "Collection.LateFee")]
    [InlineData("Aportes al fondo de reserva", "Collection.ReserveFund")]
    [InlineData("Cargos particulares a unidades", "Collection.IndividualAdjustment")]
    [InlineData("Cargos individuales", "Collection.IndividualAdjustment")]
    public void ReconoceLasCuentasDeCobranza(string name, string expected) =>
        Assert.Equal(expected, LiquidationCategorySuggester.CollectionRoleFor(name));

    [Theory]
    [InlineData("Alquiler del salón")]
    [InlineData("Intereses ganados")]
    [InlineData("Ingresos varios")]
    [InlineData("Venta de rezagos")]
    public void NoConfundeOtrosIngresosConCobranza(string name) =>
        Assert.Null(LiquidationCategorySuggester.CollectionRoleFor(name));

    [Fact]
    public void LaFuncionSugeridaSoloSeAsignaUnaVezYSoloSiElArchivoNoTraeCategoria()
    {
        var rows = new[]
        {
            new PlanRowInput { RowNumber = 2, Code = "4", Name = "INGRESOS", Type = LedgerCategoryType.Income },
            new PlanRowInput { RowNumber = 3, Code = "4.1", Name = "Expensas ordinarias" },
            new PlanRowInput { RowNumber = 4, Code = "4.2", Name = "Expensas ordinarias (atrasadas)" },
            new PlanRowInput { RowNumber = 5, Code = "4.3", Name = "Expensas con categoría explícita", IncomeCategory = BuildingIncomeCategory.Other }
        };

        var r = FinancePlanValidator.Resolve(rows, true, true, true, false);
        Assert.False(r.HasErrors);
        Assert.Equal("Collection.Ordinary", r.Rows.Single(x => x.Code == "4.1").SystemKey);
        Assert.Contains(r.Rows.Single(x => x.Code == "4.1").Warnings, w => w.Contains("Función sugerida"));
        Assert.Null(r.Rows.Single(x => x.Code == "4.2").SystemKey);                       // la funcion ya la tiene 4.1
        Assert.Equal(BuildingIncomeCategory.Other, r.Rows.Single(x => x.Code == "4.2").IncomeCategory);
        Assert.Null(r.Rows.Single(x => x.Code == "4.3").SystemKey);                       // trae categoria: se respeta

        // Al confirmar (sin sugerencias) no se inventa ninguna funcion.
        var commit = FinancePlanValidator.Resolve(
            [new PlanRowInput { RowNumber = 2, Code = "4", Name = "I", Type = LedgerCategoryType.Income },
             new PlanRowInput { RowNumber = 3, Code = "4.1", Name = "Expensas ordinarias", ParentCode = "4", IncomeCategory = BuildingIncomeCategory.Other }],
            false, false, false, true);
        Assert.Null(commit.Rows.Single(x => x.Code == "4.1").SystemKey);
    }
}
