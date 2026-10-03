using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Tests.Finance;

/// <summary>Validacion del plan de cuentas importado: arbol (padres, niveles, ciclos), tipos, categoria de la liquidacion y funciones.</summary>
public class FinancePlanValidatorTests
{
    private static PlanRowInput Row(int n, string code, string name, string? parent = null, LedgerCategoryType? type = null) =>
        new() { RowNumber = n, Code = code, Name = name, ParentCode = parent, Type = type };

    private static PlanResolution Preview(params PlanRowInput[] rows) =>
        FinancePlanValidator.Resolve(rows, inferParents: true, inferTypes: true, suggestCategories: true, childTypeFollowsParent: false);

    private static PlanResolution Commit(params PlanRowInput[] rows) =>
        FinancePlanValidator.Resolve(rows, inferParents: false, inferTypes: false, suggestCategories: false, childTypeFollowsParent: true);

    private static PlanRowResult Of(PlanResolution r, string code) => r.Rows.Single(x => x.Code == code);

    [Fact]
    public void DeduceElPadrePorElCodigoYElTipoPorLaClase()
    {
        var r = Preview(
            Row(2, "5", "EGRESOS"),
            Row(3, "5.02", "Servicios públicos"),
            Row(4, "5.02.01", "Electricidad (ANDE)"),
            Row(5, "5.02.02", "Agua (ESSAP)"));

        Assert.False(r.HasErrors);
        Assert.Null(Of(r, "5").ParentCode);
        Assert.Equal("5", Of(r, "5.02").ParentCode);
        Assert.Equal("5.02", Of(r, "5.02.01").ParentCode);
        Assert.Equal([1, 2, 3, 3], r.Rows.Select(x => x.Level));
        Assert.All(r.Rows, x => Assert.Equal(LedgerCategoryType.Expense, x.Type));
        Assert.False(Of(r, "5.02").IsLeaf);
        Assert.True(Of(r, "5.02.01").IsLeaf);
        // El tipo de la raiz salio del primer numero: se avisa para que se verifique.
        Assert.Contains(Of(r, "5").Warnings, w => w.Contains("Tipo deducido"));
        Assert.DoesNotContain(Of(r, "5.02.01").Warnings, w => w.Contains("Tipo"));
    }

    [Fact]
    public void ElPadreLoDaElCodigoMasLargoQueLoAntecede()
    {
        var r = Preview(Row(2, "1", "ACTIVO"), Row(3, "1.1", "Disponibilidades"), Row(4, "1.10", "Otros"), Row(5, "1.1.01", "Caja"));
        Assert.Equal("1.1", Of(r, "1.1.01").ParentCode);
        // 1.10 no es hijo de 1.1 pero la regla del prefijo lo toma: por eso el padre se muestra y se puede corregir con la columna.
        Assert.Equal("1.1", Of(r, "1.10").ParentCode);
    }

    [Fact]
    public void PadreExplicitoYPadreInexistente()
    {
        var r = Preview(
            Row(2, "A", "Ingresos", null, LedgerCategoryType.Income),
            Row(3, "B", "Cuota", "A"),
            Row(4, "C", "Otra", "ZZZ"));

        Assert.Equal("A", Of(r, "B").ParentCode);
        Assert.Equal(LedgerCategoryType.Income, Of(r, "B").Type);
        Assert.Contains(Of(r, "C").Errors, e => e.Contains("ZZZ"));
    }

    [Fact]
    public void CodigosRepetidosVaciosYLargos()
    {
        var r = Preview(
            Row(2, "5.01", "Personal", null, LedgerCategoryType.Expense),
            Row(3, "5.01", "Personal otra vez", null, LedgerCategoryType.Expense),
            Row(4, "", "Sin código", null, LedgerCategoryType.Expense),
            Row(5, "5.02", "", null, LedgerCategoryType.Expense),
            Row(6, new string('9', 31), "Largo", null, LedgerCategoryType.Expense));

        Assert.Contains(r.Rows[1].Errors, e => e.Contains("repetido") && e.Contains("fila 2"));
        Assert.Contains(r.Rows[2].Errors, e => e.Contains("Falta el código"));
        Assert.Contains(r.Rows[3].Errors, e => e.Contains("Falta el nombre"));
        Assert.Contains(r.Rows[4].Errors, e => e.Contains("30 caracteres"));
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void DetectaCiclosYNiveles()
    {
        var cycle = Commit(
            Row(2, "A", "A", "B", LedgerCategoryType.Expense),
            Row(3, "B", "B", "A", LedgerCategoryType.Expense));
        Assert.True(cycle.HasErrors);
        Assert.Contains(cycle.Rows.SelectMany(x => x.Errors), e => e.Contains("ciclo"));

        var self = Commit(Row(2, "A", "A", "A", LedgerCategoryType.Expense));
        Assert.Contains(Of(self, "A").Errors, e => e.Contains("propio grupo"));

        // Siete niveles: uno mas que el limite.
        var rows = new List<PlanRowInput> { Row(2, "L1", "n1", null, LedgerCategoryType.Asset) };
        for (var i = 2; i <= 7; i++) rows.Add(Row(i + 1, $"L{i}", $"n{i}", $"L{i - 1}"));
        var deep = Commit(rows.ToArray());
        Assert.Contains(Of(deep, "L7").Errors, e => e.Contains("6 niveles"));
        Assert.DoesNotContain(Of(deep, "L6").Errors, e => e.Contains("niveles"));
    }

    [Fact]
    public void ElTipoDeclaradoDebeCoincidirConElDelGrupo_PeroAlConfirmarSeSigueAlPadre()
    {
        var rows = new[]
        {
            Row(2, "1", "Grupo", null, LedgerCategoryType.Income),
            Row(3, "1.1", "Hija", "1", LedgerCategoryType.Expense)
        };

        var preview = FinancePlanValidator.Resolve(rows, true, true, true, childTypeFollowsParent: false);
        Assert.Contains(Of(preview, "1.1").Errors, e => e.Contains("no coincide"));

        // Al confirmar el usuario solo cambia el tipo de las raices: las hijas siguen a su grupo.
        var commit = FinancePlanValidator.Resolve(rows, false, false, false, childTypeFollowsParent: true);
        Assert.DoesNotContain(Of(commit, "1.1").Errors, e => e.Contains("no coincide"));
        Assert.Equal(LedgerCategoryType.Income, Of(commit, "1.1").Type);
    }

    [Fact]
    public void SinTipoYSinNumeroDeClaseEsError()
    {
        var r = Preview(Row(2, "X", "Cuenta rara"), Row(3, "X.1", "Hija", "X"));
        Assert.Contains(Of(r, "X").Errors, e => e.Contains("No se pudo deducir el tipo"));
    }

    [Fact]
    public void UnaCuentaDeIngresosOEgresosSinGrupoEsError_PeroUnActivoSueltoNo()
    {
        var r = Preview(
            Row(2, "5.99", "Suelta", null, LedgerCategoryType.Expense),
            Row(3, "1.99", "Activo suelto", null, LedgerCategoryType.Asset));

        Assert.Contains(Of(r, "5.99").Errors, e => e.Contains("no tiene grupo"));
        Assert.Empty(Of(r, "1.99").Errors);
    }

    [Fact]
    public void CategoriaDeLaLiquidacion_SugeridaConservadaYLimpiadaEnLosGrupos()
    {
        var r = Preview(
            Row(2, "5", "EGRESOS"),
            Row(3, "5.1", "Servicios"),
            new PlanRowInput { RowNumber = 4, Code = "5.1.1", Name = "Energía eléctrica", ExpenseCategory = BuildingExpenseCategory.Elevator },
            Row(5, "5.1.2", "Consumo de agua ESSAP"),
            Row(6, "5.1.3", "Cosas raras"));

        Assert.Equal(BuildingExpenseCategory.Elevator, Of(r, "5.1.1").ExpenseCategory);   // la del archivo manda
        Assert.False(Of(r, "5.1.1").CategorySuggested);
        Assert.Equal(BuildingExpenseCategory.Essap, Of(r, "5.1.2").ExpenseCategory);       // sugerida por el nombre
        Assert.True(Of(r, "5.1.2").CategorySuggested);
        Assert.Equal(BuildingExpenseCategory.Other, Of(r, "5.1.3").ExpenseCategory);
        Assert.Contains(Of(r, "5.1.3").Warnings, w => w.Contains("Sin coincidencia"));
        Assert.Null(Of(r, "5.1").ExpenseCategory);                                          // un grupo no guarda categoria
        Assert.Null(Of(r, "5").ExpenseCategory);
    }

    [Fact]
    public void LaCategoriaDeAportesAlFondoYElSaldoAcumuladoNoSePermiten()
    {
        var r = Preview(
            Row(2, "5", "E", null, LedgerCategoryType.Expense),
            new PlanRowInput { RowNumber = 3, Code = "5.1", Name = "Aporte", ParentCode = "5", ExpenseCategory = BuildingExpenseCategory.ReserveFund },
            Row(4, "4", "I", null, LedgerCategoryType.Income),
            new PlanRowInput { RowNumber = 5, Code = "4.1", Name = "Saldo", ParentCode = "4", IncomeCategory = BuildingIncomeCategory.AccumulatedBalance },
            new PlanRowInput { RowNumber = 6, Code = "4.2", Name = "Fondo operativo", ParentCode = "4", IncomeCategory = BuildingIncomeCategory.OperationalFund });

        Assert.Contains(Of(r, "5.1").Errors, e => e.Contains("fondo de reserva"));
        Assert.Contains(Of(r, "4.1").Errors, e => e.Contains("saldo acumulado"));
        Assert.Contains(Of(r, "4.2").Errors, e => e.Contains("saldo acumulado"));
    }

    [Fact]
    public void ActivosPasivosYPatrimonioNoLlevanCategoria()
    {
        var r = Preview(
            Row(2, "1", "ACTIVO"),
            new PlanRowInput { RowNumber = 3, Code = "1.1", Name = "Caja", ParentCode = "1", ExpenseCategory = BuildingExpenseCategory.Ande });
        Assert.Null(Of(r, "1.1").ExpenseCategory);
        Assert.Null(Of(r, "1.1").IncomeCategory);
    }

    [Fact]
    public void FuncionesEspeciales_ValidasUnicasYDeSuTipo()
    {
        PlanRowInput WithKey(int n, string code, string parent, string key) =>
            new() { RowNumber = n, Code = code, Name = "C" + code, ParentCode = parent, SystemKey = key };

        var r = Preview(
            Row(2, "4", "I", null, LedgerCategoryType.Income),
            Row(3, "5", "E", null, LedgerCategoryType.Expense),
            WithKey(4, "4.1", "4", "Collection.Ordinary"),
            WithKey(5, "4.2", "4", "Collection.Ordinary"),    // repetida
            WithKey(6, "4.3", "4", "Expense.Ande"),            // tipo equivocado
            WithKey(7, "4.4", "4", "NoExiste"),
            WithKey(8, "5.1", "5", "Expense.Ande"),
            new PlanRowInput { RowNumber = 9, Code = "5.2", Name = "Otra", ParentCode = "5", SystemKey = "Expense.Essap", ExpenseCategory = BuildingExpenseCategory.Other });

        Assert.Empty(Of(r, "4.1").Errors);
        Assert.Contains(Of(r, "4.2").Errors, e => e.Contains("ya la tiene la cuenta 4.1"));
        Assert.Contains(Of(r, "4.3").Errors, e => e.Contains("solo se puede") || e.Contains("es de gastos") || e.Contains("es de egresos") || e.Contains("no se puede asignar"));
        Assert.Contains(Of(r, "4.4").Errors, e => e.Contains("no existe"));
        Assert.Equal(BuildingExpenseCategory.Ande, Of(r, "5.1").ExpenseCategory);          // la funcion fija la categoria
        Assert.Equal(BuildingExpenseCategory.Essap, Of(r, "5.2").ExpenseCategory);         // aunque el archivo diga otra
        Assert.Null(Of(r, "4.1").IncomeCategory);                                           // cobranza: sin categoria
    }

    [Fact]
    public void UnGrupoNoTieneFuncionNiQuedaInactivoConCuentasActivas()
    {
        var r = Commit(
            new PlanRowInput { RowNumber = 2, Code = "5", Name = "E", Type = LedgerCategoryType.Expense, SystemKey = "Expense.Other", IsActive = false },
            new PlanRowInput { RowNumber = 3, Code = "5.1", Name = "Hija", ParentCode = "5", IsActive = true, ExpenseCategory = BuildingExpenseCategory.Other });

        Assert.Contains(Of(r, "5").Errors, e => e.Contains("grupo no puede tener función"));
        Assert.True(Of(r, "5").IsActive);   // tiene una cuenta activa
    }

    [Fact]
    public void ElTipoSeLeeDelTextoDelExcel()
    {
        Assert.Equal(LedgerCategoryType.Asset, FinancePlanValidator.ParseType("Activo"));
        Assert.Equal(LedgerCategoryType.Liability, FinancePlanValidator.ParseType(" PASIVO "));
        Assert.Equal(LedgerCategoryType.Fund, FinancePlanValidator.ParseType("Patrimonio"));
        Assert.Equal(LedgerCategoryType.Fund, FinancePlanValidator.ParseType("Fondos"));
        Assert.Equal(LedgerCategoryType.Income, FinancePlanValidator.ParseType("Ingreso"));
        Assert.Equal(LedgerCategoryType.Expense, FinancePlanValidator.ParseType("Egresos"));
        Assert.Equal(LedgerCategoryType.Expense, FinancePlanValidator.ParseType("Gasto"));
        Assert.Null(FinancePlanValidator.ParseType("cosa"));
    }

    [Fact]
    public void ErroresDeLecturaDelArchivoSeConservan()
    {
        var row = Row(2, "5", "E", null, LedgerCategoryType.Expense);
        row.ParseErrors.Add("Tipo desconocido: «x».");
        row.ParseWarnings.Add("Aviso de lectura");
        var r = Preview(row);
        Assert.Contains(Of(r, "5").Errors, e => e.Contains("Tipo desconocido"));
        Assert.Contains(Of(r, "5").Warnings, w => w.Contains("Aviso de lectura"));
    }

    // ── Sugerencia de categoria ──────────────────────────────────────────────

    [Theory]
    [InlineData("Electricidad (ANDE)", BuildingExpenseCategory.Ande)]
    [InlineData("Consumo de energía eléctrica", BuildingExpenseCategory.Ande)]
    [InlineData("ESSAP agua", BuildingExpenseCategory.Essap)]
    [InlineData("Teléfono e internet", BuildingExpenseCategory.InternetPhone)]
    [InlineData("Mantenimiento de ascensores", BuildingExpenseCategory.Elevator)]
    [InlineData("Seguro integral del edificio", BuildingExpenseCategory.Insurance)]
    [InlineData("Seguridad y vigilancia", BuildingExpenseCategory.Security)]
    [InlineData("Sueldos del personal", BuildingExpenseCategory.Payroll)]
    [InlineData("Aporte patronal IPS", BuildingExpenseCategory.Payroll)]
    [InlineData("Impuesto inmobiliario", BuildingExpenseCategory.Taxes)]
    [InlineData("IVA no recuperable", BuildingExpenseCategory.Taxes)]
    [InlineData("Servicio de limpieza", BuildingExpenseCategory.Cleaning)]
    [InlineData("Honorarios de administración", BuildingExpenseCategory.Administration)]
    [InlineData("Obras de fachada", BuildingExpenseCategory.Extraordinary)]
    [InlineData("Materiales y ferretería", BuildingExpenseCategory.Supplies)]
    [InlineData("Reparaciones de plomería", BuildingExpenseCategory.Maintenance)]
    [InlineData("Gas (GLP granel)", BuildingExpenseCategory.Utilities)]
    public void SugiereLaCategoriaDeGastoPorElNombre(string name, BuildingExpenseCategory expected)
    {
        Assert.Equal(expected, LiquidationCategorySuggester.ForExpense(name, out var matched));
        Assert.True(matched);
    }

    [Theory]
    [InlineData("Gastos varios")]       // "gas" no debe coincidir con "gastos"
    [InlineData("Cosas raras")]
    [InlineData("Diversos")]
    public void SinCoincidenciaQuedaEnOtro(string name)
    {
        Assert.Equal(BuildingExpenseCategory.Other, LiquidationCategorySuggester.ForExpense(name, out var matched));
        Assert.False(matched);
    }

    [Theory]
    [InlineData("Alquiler de espacios comunes", BuildingIncomeCategory.CommonAreaRental)]
    [InlineData("Uso de amenities (SUM, quincho)", BuildingIncomeCategory.CommonAreaRental)]
    [InlineData("Intereses y rentas financieras", BuildingIncomeCategory.Interest)]
    [InlineData("Recupero de siniestros", BuildingIncomeCategory.Other)]
    public void SugiereLaCategoriaDeIngresoPorElNombre(string name, BuildingIncomeCategory expected) =>
        Assert.Equal(expected, LiquidationCategorySuggester.ForIncome(name, out _));
}
