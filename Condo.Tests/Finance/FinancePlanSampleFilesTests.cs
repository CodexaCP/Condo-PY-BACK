using Condo.Api.Services;
using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Tests.Finance;

/// <summary>
/// Los Excel de ejemplo de <c>docs/ejemplos</c> (los que usa la guia de pruebas manuales) se leen exactamente como dice la guia. Si cambia la
/// logica de importacion y estos archivos dejan de dar lo documentado, esta prueba avisa antes de que alguien pruebe a mano con datos viejos.
/// </summary>
public class FinancePlanSampleFilesTests
{
    private static string? Find(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "ejemplos", file);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static PlanResolution Load(string file)
    {
        var path = Find(file) ?? throw new FileNotFoundException($"No se encontró docs/ejemplos/{file}.");
        using var stream = File.OpenRead(path);
        var (rows, error) = FinancePlanExcel.Parse(stream);
        Assert.Null(error);
        return FinancePlanValidator.Resolve(rows!, inferParents: true, inferTypes: true, suggestCategories: true, childTypeFollowsParent: false);
    }

    [Fact]
    public void ElPlanDeEjemploDelClienteSeLeeSinErroresYSugiereLoQueDiceLaGuia()
    {
        var r = Load("plan-cuentas-cliente-ejemplo.xlsx");
        PlanRowResult Of(string code) => r.Rows.Single(x => x.Code == code);

        Assert.False(r.HasErrors);
        Assert.Equal(33, r.Rows.Count);
        Assert.Equal(19, r.Rows.Count(x => x.IsLeaf));
        Assert.Equal(4, Of("1.1.1.01").Level);

        // Clases: algunas con el tipo declarado, otras deducidas del primer numero (con aviso).
        Assert.Equal(LedgerCategoryType.Asset, Of("1").Type);
        Assert.Equal(LedgerCategoryType.Liability, Of("2").Type);
        Assert.Equal(LedgerCategoryType.Fund, Of("3").Type);
        Assert.Equal(LedgerCategoryType.Income, Of("4").Type);
        Assert.Equal(LedgerCategoryType.Expense, Of("5").Type);
        Assert.Contains(Of("2").Warnings, w => w.Contains("Tipo deducido"));
        Assert.DoesNotContain(Of("1").Warnings, w => w.Contains("Tipo deducido"));
        Assert.Equal("1.1.1", Of("1.1.1.01").ParentCode);

        // Cobranza de expensas: funcion sugerida por el nombre.
        Assert.Equal("Collection.Ordinary", Of("4.1.1").SystemKey);
        Assert.Equal("Collection.Extraordinary", Of("4.1.2").SystemKey);
        Assert.Null(Of("4.1.1").IncomeCategory);

        // Otros ingresos.
        Assert.Equal(BuildingIncomeCategory.CommonAreaRental, Of("4.2.1").IncomeCategory);
        Assert.False(Of("4.2.1").CategorySuggested);   // venia en el archivo
        Assert.Equal(BuildingIncomeCategory.Interest, Of("4.2.2").IncomeCategory);
        Assert.True(Of("4.2.2").CategorySuggested);

        // Gastos: categoria sugerida por el nombre.
        Assert.Equal(BuildingExpenseCategory.Ande, Of("5.1.1").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Essap, Of("5.1.2").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.InternetPhone, Of("5.1.3").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Payroll, Of("5.2.1").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Payroll, Of("5.2.2").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Elevator, Of("5.3.1").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Maintenance, Of("5.3.2").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Cleaning, Of("5.3.3").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Administration, Of("5.4.1").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Insurance, Of("5.4.2").ExpenseCategory);
        Assert.Equal(BuildingExpenseCategory.Other, Of("5.4.3").ExpenseCategory);
        Assert.Contains(Of("5.4.3").Warnings, w => w.Contains("Sin coincidencia"));
        Assert.False(Of("5.4.3").IsActive);                       // la fila trae Activo = No

        Assert.Equal("5110101", Of("5.1.1").ExternalCode);
        Assert.Null(Of("5.1").ExpenseCategory);                    // un grupo no guarda categoria
    }

    [Fact]
    public void ElPlanConErroresDaLosErroresDocumentados()
    {
        var r = Load("plan-cuentas-cliente-con-errores.xlsx");
        List<string> Errors(int row) => r.Rows.Single(x => x.RowNumber == row).Errors;

        Assert.True(r.HasErrors);
        Assert.Empty(Errors(2));
        Assert.Empty(Errors(3));
        Assert.Empty(Errors(4));
        Assert.Contains(Errors(5), e => e.Contains("repetido") && e.Contains("fila 4"));
        Assert.Contains(Errors(6), e => e.Contains("Falta el nombre"));
        Assert.Contains(Errors(7), e => e.Contains("ZZZ"));
        Assert.Contains(Errors(8), e => e.Contains("Tipo desconocido"));
        Assert.Contains(Errors(9), e => e.Contains("No se pudo deducir el tipo"));
        Assert.Contains(Errors(10), e => e.Contains("no tiene grupo"));
    }
}
