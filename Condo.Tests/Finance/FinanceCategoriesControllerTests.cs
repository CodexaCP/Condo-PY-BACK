using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>Reglas del plan de cuentas en la API: niveles libres, funciones especiales, plan generico, importacion de Excel y permisos.</summary>
public class FinanceCategoriesControllerTests : IDisposable
{
    private readonly FinanceEnv _env = new();
    private readonly FinanceCategoriesController _api;

    public FinanceCategoriesControllerTests()
    {
        _api = _env.Controller();
    }

    public void Dispose() => _env.Dispose();

    private LedgerCategoryUpsertRequest Req(string code, string name, Guid? parentId = null, LedgerCategoryType type = LedgerCategoryType.Expense) =>
        new() { BuildingId = _env.Building.Id, Code = code, Name = name, ParentId = parentId, Type = type };

    private async Task<LedgerCategoryDto> Create(LedgerCategoryUpsertRequest request) => FinanceEnv.Ok(await _api.Create(request, default));

    private async Task<IReadOnlyList<LedgerCategoryDto>> List() => FinanceEnv.Ok(await _api.GetAll(_env.Building.Id, default));

    // ── Niveles ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PermiteVariosNiveles_ClaseGrupoSubgrupoYCuenta()
    {
        var clase = await Create(Req("5", "EGRESOS"));
        var grupo = await Create(Req("5.01", "Personal", clase.Id));
        var subgrupo = await Create(Req("5.01.A", "Planta", grupo.Id));
        var cuenta = await Create(Req("5.01.A.1", "Sueldos", subgrupo.Id));

        Assert.Equal(LedgerCategoryType.Expense, cuenta.Type);                 // hereda el tipo del padre
        Assert.Equal(BuildingExpenseCategory.Other, cuenta.ExpenseCategory);   // cuenta final: categoria por defecto
        var all = await List();
        Assert.Null(all.Single(c => c.Code == "5.01.A").ExpenseCategory);       // un grupo no guarda categoria
        Assert.True(all.Single(c => c.Code == "5.01.A").HasChildren);
    }

    [Fact]
    public async Task ElLimiteEsDeSeisNiveles()
    {
        LedgerCategoryDto? parent = null;
        for (var i = 1; i <= 6; i++)
        {
            parent = await Create(Req($"N{i}", $"Nivel {i}", parent?.Id));
        }

        var result = await _api.Create(Req("N7", "Nivel 7", parent!.Id), default);
        Assert.Contains("6 niveles", FinanceEnv.BadRequestText(result));
    }

    [Fact]
    public async Task NoSePuedeMoverUnGrupoDentroDeSuPropioSubgrupo()
    {
        var a = await Create(Req("A", "A"));
        var b = await Create(Req("A.1", "B", a.Id));
        var c = await Create(Req("A.1.1", "C", b.Id));

        var move = Req("A", "A", c.Id);
        var result = await _api.Update(a.Id, move, default);
        Assert.Contains("propios subgrupos", FinanceEnv.BadRequestText(result));
    }

    [Fact]
    public async Task UnaCuentaConMovimientosNoPuedePasarASerGrupo()
    {
        _env.SeedTemplate();
        var ande = _env.Cat("5.02.01");
        _env.AddExpense(ande.Id, category: BuildingExpenseCategory.Ande);

        var result = await _api.Create(Req("5.02.01.1", "Medidor", ande.Id), default);
        // La cuenta ANDE tiene ademas una funcion especial: primero cae esa regla.
        Assert.Contains("función especial", FinanceEnv.BadRequestText(result));

        var other = _env.Cat("5.04.01");   // activa y sin funcion
        _env.AddExpense(other.Id, category: BuildingExpenseCategory.Maintenance);
        var result2 = await _api.Create(Req("5.04.01.1", "Tablero", other.Id), default);
        Assert.Contains("gastos o ingresos cargados", FinanceEnv.BadRequestText(result2));
    }

    [Fact]
    public async Task UnGrupoConSubcuentasNoCambiaDeTipo()
    {
        var clase = await Create(Req("5", "EGRESOS"));
        var grupo = await Create(Req("5.1", "Servicios", clase.Id));
        await Create(Req("5.1.1", "Luz", grupo.Id));

        var result = await _api.Update(clase.Id, Req("5", "EGRESOS", null, LedgerCategoryType.Income), default);
        Assert.Contains("subcuentas", FinanceEnv.BadRequestText(result));
    }

    [Fact]
    public async Task ElCodigoNoSeRepiteEnElEdificio()
    {
        await Create(Req("5", "EGRESOS"));
        var dup = await _api.Create(Req("5", "Otra cosa"), default);
        Assert.IsType<ConflictObjectResult>(dup.Result);
    }

    [Fact]
    public async Task LasCuentasDeActivoPasivoYPatrimonioSePuedenCrearYNoLlevanCategoria()
    {
        var activo = await Create(Req("1", "ACTIVO", null, LedgerCategoryType.Asset));
        var caja = await Create(Req("1.1", "Caja", activo.Id));
        Assert.Equal(LedgerCategoryType.Asset, caja.Type);
        Assert.Null(caja.ExpenseCategory);
        Assert.Null(caja.IncomeCategory);
    }

    // ── Funciones especiales ────────────────────────────────────────────────

    [Fact]
    public async Task AsignarUnaFuncionLaPasaDeUnaCuentaAOtra_YLaCuentaAnteriorConservaSuCategoria()
    {
        _env.SeedTemplate();
        var electricidad = _env.Cat("5.04.01");
        var request = Req("5.04.01", "Electricidad", electricidad.ParentId, LedgerCategoryType.Expense);
        request.SystemKey = "Expense.Ande";

        var updated = FinanceEnv.Ok(await _api.Update(electricidad.Id, request, default));
        Assert.Equal("Expense.Ande", updated.SystemKey);
        Assert.Equal(BuildingExpenseCategory.Ande, updated.ExpenseCategory);   // la funcion fija la categoria

        var previous = _env.Cat("5.02.01");
        Assert.Null(previous.SystemKey);                                       // ya no la tiene
        Assert.Equal(BuildingExpenseCategory.Ande, previous.ExpenseCategory);  // pero su categoria no cambio
        Assert.Equal(1, _env.AllCats().Count(c => c.SystemKey == "Expense.Ande"));
    }

    [Fact]
    public async Task QuitarLaFuncionDeCobranzaNoSePermite_PeroLaDeUnaCategoriaSi()
    {
        _env.SeedTemplate();
        var cobranza = _env.Cat("4.1.01");
        var clear = Req("4.1.01", "Expensas ordinarias", cobranza.ParentId, LedgerCategoryType.Income);
        clear.SystemKey = "";
        Assert.Contains("cobranza", FinanceEnv.BadRequestText(await _api.Update(cobranza.Id, clear, default)));

        var ande = _env.Cat("5.02.01");
        var clearAnde = Req("5.02.01", "Electricidad (ANDE)", ande.ParentId, LedgerCategoryType.Expense);
        clearAnde.SystemKey = "";
        var updated = FinanceEnv.Ok(await _api.Update(ande.Id, clearAnde, default));
        Assert.Null(updated.SystemKey);
        Assert.Equal(BuildingExpenseCategory.Ande, updated.ExpenseCategory);
    }

    [Fact]
    public async Task SinDatoDeFuncionSeConservaLaQueTenia()
    {
        _env.SeedTemplate();
        var ande = _env.Cat("5.02.01");
        var rename = Req("5.02.01", "Energía eléctrica", ande.ParentId, LedgerCategoryType.Expense);   // SystemKey nulo = sin cambio
        var updated = FinanceEnv.Ok(await _api.Update(ande.Id, rename, default));
        Assert.Equal("Expense.Ande", updated.SystemKey);
        Assert.Equal("Energía eléctrica", updated.Name);
    }

    [Fact]
    public async Task LaFuncionDebeSerDelTipoDeLaCuenta()
    {
        _env.SeedTemplate();
        var gasto = _env.Cat("5.04.01");
        var request = Req("5.04.01", "Electricidad", gasto.ParentId, LedgerCategoryType.Expense);
        request.SystemKey = "Collection.Ordinary";
        Assert.Contains("solo se puede asignar", FinanceEnv.BadRequestText(await _api.Update(gasto.Id, request, default)));

        request.SystemKey = "Inexistente";
        Assert.Contains("no existe", FinanceEnv.BadRequestText(await _api.Update(gasto.Id, request, default)));
    }

    [Fact]
    public async Task UnaFuncionNoPasaAUnaCuentaConMovimientosDeOtraCategoria()
    {
        _env.SeedTemplate();
        var cuenta = _env.Cat("5.04.01");   // categoria Mantenimiento
        _env.AddExpense(cuenta.Id, category: BuildingExpenseCategory.Maintenance);
        var request = Req("5.04.01", "Electricidad", cuenta.ParentId, LedgerCategoryType.Expense);
        request.SystemKey = "Expense.Ande";   // cambiaria su categoria de la liquidacion

        Assert.Contains("misma categoría", FinanceEnv.BadRequestText(await _api.Update(cuenta.Id, request, default)));
        Assert.Null(_env.Cat("5.04.01").SystemKey);
        Assert.Equal("Expense.Ande", _env.Cat("5.02.01").SystemKey);   // la otra cuenta no perdio la funcion
    }

    [Fact]
    public async Task UnaCuentaConFuncionNoSeEliminaNiSeMueve()
    {
        _env.SeedTemplate();
        var ande = _env.Cat("5.02.01");
        Assert.Contains("función especial", Assert.IsType<BadRequestObjectResult>(await _api.Delete(ande.Id, default)).Value as string);

        var move = Req("5.02.01", "Electricidad (ANDE)", _env.Cat("5.03").Id, LedgerCategoryType.Expense);
        Assert.Contains("no se pueden mover", FinanceEnv.BadRequestText(await _api.Update(ande.Id, move, default)));
    }

    [Fact]
    public async Task UnaCuentaSinFuncionSePuedeEliminarSiNoTieneMovimientosNiPresupuesto()
    {
        _env.SeedTemplate();
        var free = _env.Cat("5.03.07");
        Assert.IsType<NoContentResult>(await _api.Delete(free.Id, default));

        var used = _env.Cat("5.03.08");
        _env.AddExpense(used.Id, category: BuildingExpenseCategory.Maintenance);
        Assert.Contains("gastos o ingresos", Assert.IsType<BadRequestObjectResult>(await _api.Delete(used.Id, default)).Value as string);

        var budgeted = _env.Cat("5.03.09");
        _env.AddBudget(budgeted.Id);
        Assert.Contains("presupuesto", Assert.IsType<BadRequestObjectResult>(await _api.Delete(budgeted.Id, default)).Value as string);
    }

    // ── Activar y desactivar en bloque ──────────────────────────────────────

    [Fact]
    public async Task DesactivarUnGrupoDesactivaTodoLoQueContiene_YActivarUnaCuentaActivaSusGrupos()
    {
        _env.SeedTemplate();
        var grupo = _env.Cat("5.02");
        await _api.BulkActive(new LedgerCategoryBulkActiveRequest { BuildingId = _env.Building.Id, Ids = [grupo.Id], IsActive = false }, default);

        Assert.False(_env.Cat("5.02").IsActive);
        Assert.False(_env.Cat("5.02.01").IsActive);
        Assert.False(_env.Cat("5.02.05").IsActive);
        Assert.True(_env.Cat("5.03").IsActive);

        var cuenta = _env.Cat("5.02.03");
        await _api.BulkActive(new LedgerCategoryBulkActiveRequest { BuildingId = _env.Building.Id, Ids = [cuenta.Id], IsActive = true }, default);
        Assert.True(_env.Cat("5.02.03").IsActive);
        Assert.True(_env.Cat("5.02").IsActive);   // el grupo se activa con su cuenta
        Assert.True(_env.Cat("5").IsActive);
        Assert.False(_env.Cat("5.02.01").IsActive);
    }

    [Fact]
    public async Task ActivarEnBloqueConUnaCuentaDeOtroEdificioEsError()
    {
        _env.SeedTemplate();
        var otherBuilding = _env.T.AddBuilding(_env.Company, "Otro");
        var foreign = FinanceChartTemplate.CreateEntities(otherBuilding.Id, _env.Company.Id).First();
        _env.T.Db.LedgerCategories.Add(foreign);
        _env.T.Db.SaveChanges();

        var result = await _api.BulkActive(new LedgerCategoryBulkActiveRequest { BuildingId = _env.Building.Id, Ids = [foreign.Id], IsActive = false }, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.True(_env.T.NewContext().LedgerCategories.Single(c => c.Id == foreign.Id).IsActive);
    }

    // ── Plan generico ───────────────────────────────────────────────────────

    [Fact]
    public async Task AplicarElPlanGenerico_ReemplazarExigeConfirmarSiSePierdeAlgo()
    {
        _env.SeedTemplate();
        var ande = _env.Cat("5.02.01");
        _env.AddExpense(ande.Id, category: BuildingExpenseCategory.Ande);
        _env.AddBudget(ande.Id);

        var impact = FinanceEnv.Ok(await _api.ReplaceImpact(_env.Building.Id, default));
        Assert.Equal(1, impact.Expenses);
        Assert.Equal(1, impact.BudgetLines);

        var withoutConfirm = await _api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace }, default);
        Assert.Contains("Confirmá", FinanceEnv.BadRequestText(withoutConfirm));
        Assert.Equal(ande.Id, _env.Cat("5.02.01").Id);   // no se toco nada

        var confirmed = FinanceEnv.Ok(await _api.ApplyTemplate(
            new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace, ConfirmReplace = true }, default));
        Assert.Equal(FinanceChartTemplate.Nodes.Count, confirmed.Created);
        Assert.Equal(1, confirmed.UnlinkedExpenses);
        Assert.NotEqual(ande.Id, _env.Cat("5.02.01").Id);
    }

    [Fact]
    public async Task AplicarElPlanGenericoSinNadaQuePerderNoPideConfirmacion()
    {
        _env.SeedTemplate();
        var result = FinanceEnv.Ok(await _api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace }, default));
        Assert.Equal(FinanceChartTemplate.Nodes.Count, result.Created);
    }

    [Fact]
    public async Task AgregarLoQueFaltaDelGenericoSobreUnPlanCompletoNoCreaNada()
    {
        _env.SeedTemplate();
        var result = FinanceEnv.Ok(await _api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.AddMissing }, default));
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Skipped);
    }

    // ── Importacion de Excel ────────────────────────────────────────────────

    private static IFormFile Workbook(params string[][] rows)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Plan de cuentas");
        string[] headers = ["Código", "Nombre", "Código padre", "Tipo", "Código del contador", "Categoría en la liquidación", "Activo"];
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "plan.xlsx");
    }

    [Fact]
    public async Task ImportarElPlanDelCliente_VistaPreviaYConfirmacion()
    {
        _env.SeedTemplate();
        var ande = _env.Cat("5.02.01");
        _env.AddExpense(ande.Id, category: BuildingExpenseCategory.Ande);

        var file = Workbook(
            ["4", "INGRESOS", "", "Ingreso", "", "", ""],
            ["4.1", "Cuotas", "", "", "", "", ""],
            ["4.1.1", "Alquiler del quincho", "", "", "410", "", ""],
            ["5", "EGRESOS", "", "", "", "", ""],
            ["5.1", "Servicios", "", "", "", "", ""],
            ["5.1.1", "Energía eléctrica ANDE", "", "", "620101", "", ""],
            ["5.1.2", "Agua", "", "", "", "ESSAP", ""],
            ["5.1.3", "Cosas raras", "", "", "", "", "No"]);

        var preview = FinanceEnv.Ok(await _api.ImportPreview(file, _env.Building.Id, default));

        Assert.False(preview.HasErrors);
        Assert.Equal(8, preview.Rows.Count);
        Assert.Equal(1, preview.Impact.Expenses);
        var luz = preview.Rows.Single(r => r.Code == "5.1.1");
        Assert.Equal("5.1", luz.ParentCode);
        Assert.Equal(BuildingExpenseCategory.Ande, luz.ExpenseCategory);   // sugerida por el nombre
        Assert.True(luz.CategorySuggested);
        Assert.Equal(BuildingExpenseCategory.Essap, preview.Rows.Single(r => r.Code == "5.1.2").ExpenseCategory);
        Assert.False(preview.Rows.Single(r => r.Code == "5.1.2").CategorySuggested);   // venia en el archivo
        Assert.False(preview.Rows.Single(r => r.Code == "5.1.3").IsActive);
        Assert.Equal(BuildingIncomeCategory.CommonAreaRental, preview.Rows.Single(r => r.Code == "4.1.1").IncomeCategory);
        Assert.Equal(LedgerCategoryType.Expense, preview.Rows.Single(r => r.Code == "5.1.2").Type);

        // La vista previa no guarda nada.
        Assert.Equal(FinanceChartTemplate.Nodes.Count, _env.AllCats().Count);

        // El usuario corrige una categoria y confirma (sin confirmar el reemplazo no pasa: hay un gasto con rubro).
        luz.ExpenseCategory = BuildingExpenseCategory.Utilities;
        var commit = new LedgerPlanImportCommitRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace, Rows = preview.Rows };
        Assert.Contains("Confirmá", FinanceEnv.BadRequestText(await _api.ImportCommit(commit, default)));

        commit.ConfirmReplace = true;
        var result = FinanceEnv.Ok(await _api.ImportCommit(commit, default));
        Assert.Equal(8, result.Created);
        Assert.Equal(1, result.UnlinkedExpenses);

        var cats = _env.AllCats();
        Assert.Equal(8, cats.Count);
        Assert.Equal(BuildingExpenseCategory.Utilities, cats.Single(c => c.Code == "5.1.1").ExpenseCategory);
        Assert.Equal("620101", cats.Single(c => c.Code == "5.1.1").ExternalCode);
        Assert.Equal(cats.Single(c => c.Code == "5.1").Id, cats.Single(c => c.Code == "5.1.1").ParentId);
        Assert.False(cats.Single(c => c.Code == "5.1.3").IsActive);

        // Los gastos del edificio siguen: solo quedaron sin rubro.
        var db = _env.T.NewContext();
        Assert.Equal(1, db.BuildingExpenses.Count(e => e.BuildingId == _env.Building.Id && e.LedgerCategoryId == null));
    }

    [Fact]
    public async Task ImportarConErroresNoDejaConfirmar()
    {
        var file = Workbook(
            ["5", "EGRESOS", "", "Egreso", "", "", ""],
            ["5", "Repetida", "", "", "", "", ""],
            ["5.1", "", "", "", "", "", ""],
            ["9.1", "Con padre que no existe", "ZZ", "", "", "", ""],
            ["7", "Tipo raro", "", "Cosa", "", "", ""]);

        var preview = FinanceEnv.Ok(await _api.ImportPreview(file, _env.Building.Id, default));
        Assert.True(preview.HasErrors);
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 3).Errors, e => e.Contains("repetido"));
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 4).Errors, e => e.Contains("Falta el nombre"));
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 5).Errors, e => e.Contains("ZZ"));
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 6).Errors, e => e.Contains("Tipo desconocido"));

        var commit = new LedgerPlanImportCommitRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace, ConfirmReplace = true, Rows = preview.Rows };
        Assert.Contains("errores", FinanceEnv.BadRequestText(await _api.ImportCommit(commit, default)));
        Assert.Empty(_env.AllCats());
    }

    [Fact]
    public async Task ImportarEnModoActualizarAgregaLoNuevoYActualizaLoExistente()
    {
        _env.SeedTemplate();
        var file = Workbook(
            ["5", "EGRESOS", "", "", "", "", ""],
            ["5.02", "Servicios públicos", "", "", "", "", ""],
            ["5.02.01", "Electricidad del edificio", "", "", "9001", "", ""],
            ["5.02.50", "Agua mineral", "", "", "", "Servicios", ""]);

        var preview = FinanceEnv.Ok(await _api.ImportPreview(file, _env.Building.Id, default));
        Assert.False(preview.HasErrors);

        var commit = new LedgerPlanImportCommitRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Update, Rows = preview.Rows };
        var result = FinanceEnv.Ok(await _api.ImportCommit(commit, default));

        Assert.Equal(1, result.Created);
        Assert.True(result.Updated >= 1);
        Assert.Equal("Electricidad del edificio", _env.Cat("5.02.01").Name);
        Assert.Equal("9001", _env.Cat("5.02.01").ExternalCode);
        Assert.Equal("Expense.Ande", _env.Cat("5.02.01").SystemKey);   // conserva su funcion
        Assert.Equal("Agua mineral", _env.Cat("5.02.50").Name);
        Assert.Equal(FinanceChartTemplate.Nodes.Count + 1, _env.AllCats().Count);
    }

    [Fact]
    public async Task ElServidorRevalidaAlConfirmar_NoConfiaEnLoQueManda()
    {
        // Filas armadas a mano (como si alguien llamara a la API sin pasar por la vista previa), con un ciclo y un tipo faltante.
        var commit = new LedgerPlanImportCommitRequest
        {
            BuildingId = _env.Building.Id,
            Mode = LedgerPlanApplyMode.Replace,
            ConfirmReplace = true,
            Rows =
            [
                new LedgerPlanImportRowDto { RowNumber = 2, Code = "A", Name = "A", ParentCode = "B" },
                new LedgerPlanImportRowDto { RowNumber = 3, Code = "B", Name = "B", ParentCode = "A" }
            ]
        };

        Assert.Contains("errores", FinanceEnv.BadRequestText(await _api.ImportCommit(commit, default)));
        Assert.Empty(_env.AllCats());
    }

    [Fact]
    public async Task ElArchivoDebeSerXlsxYNoSerVacio()
    {
        var notExcel = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "plan.txt");
        Assert.Contains(".xlsx", FinanceEnv.BadRequestText(await _api.ImportPreview(notExcel, _env.Building.Id, default)));

        var broken = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "plan.xlsx");
        Assert.Contains("Excel", FinanceEnv.BadRequestText(await _api.ImportPreview(broken, _env.Building.Id, default)));

        var empty = new FormFile(new MemoryStream(), 0, 0, "file", "plan.xlsx");
        Assert.Contains("ningún archivo", FinanceEnv.BadRequestText(await _api.ImportPreview(empty, _env.Building.Id, default)));
    }

    [Fact]
    public async Task LaPlantillaDeExcelSeDescargaYSeLeeSinErrores()
    {
        var download = await _api.DownloadImportTemplate(_env.Building.Id, default);
        var file = Assert.IsType<FileContentResult>(download);
        Assert.Contains("spreadsheetml", file.ContentType);

        var stream = new MemoryStream(file.FileContents);
        var (rows, error) = FinancePlanExcel.Parse(stream);
        Assert.Null(error);
        Assert.NotNull(rows);
        Assert.True(rows!.Count >= 8);

        var preview = FinanceEnv.Ok(await _api.ImportPreview(new FormFile(new MemoryStream(file.FileContents), 0, file.FileContents.Length, "file", "plantilla.xlsx"), _env.Building.Id, default));
        Assert.False(preview.HasErrors);
    }

    // ── Permisos ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SoloElSuperAdminModificaElPlan_LosDemasLoVenEnSoloLectura()
    {
        _env.SeedTemplate();
        _env.LoginAs("CompanyAdmin");
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Access.Buildings.Add(_env.Building.Id);

        // Lectura: permitida.
        Assert.NotEmpty(await List());

        // Escritura: 403 en todas las operaciones del plan.
        static void AssertForbidden(IActionResult? result) => Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);

        AssertForbidden((await _api.Create(Req("9", "Nueva"), default)).Result);
        AssertForbidden((await _api.Update(_env.Cat("5.02.01").Id, Req("5.02.01", "x", _env.Cat("5.02.01").ParentId), default)).Result);
        AssertForbidden(await _api.Delete(_env.Cat("5.03.07").Id, default));
        AssertForbidden((await _api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.AddMissing }, default)).Result);
        AssertForbidden((await _api.ReplaceImpact(_env.Building.Id, default)).Result);
        AssertForbidden((await _api.BulkActive(new LedgerCategoryBulkActiveRequest { BuildingId = _env.Building.Id, Ids = [_env.Cat("5.03.07").Id], IsActive = false }, default)).Result);
        AssertForbidden((await _api.ImportCommit(new LedgerPlanImportCommitRequest { BuildingId = _env.Building.Id, Rows = [new() { Code = "1", Name = "x", Type = LedgerCategoryType.Asset }] }, default)).Result);
        AssertForbidden((await _api.ImportPreview(Workbook(["1", "x", "", "Activo", "", "", ""]), _env.Building.Id, default)).Result);
        AssertForbidden(await _api.DownloadImportTemplate(_env.Building.Id, default));

        Assert.Equal(FinanceChartTemplate.Nodes.Count, _env.AllCats().Count);
        Assert.True(_env.Cat("5.03.07").IsActive == false && !_env.Cat("5.03.07").IsDeleted);
    }

    [Fact]
    public async Task SinAccesoAlEdificioNoSeVeNiSeToca()
    {
        _env.SeedTemplate();
        _env.LoginAs("BuildingManager");   // sin el edificio asignado
        var result = await _api.GetAll(_env.Building.Id, default);
        Assert.IsType<ForbidResult>(result.Result);

        var write = await _api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.Replace, ConfirmReplace = true }, default);
        Assert.IsType<ForbidResult>(write.Result);
        Assert.Equal(FinanceChartTemplate.Nodes.Count, _env.AllCats().Count);
    }

    [Fact]
    public async Task ConElModuloApagadoNoSePuedeConfigurar()
    {
        _env.Building.FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();

        var result = await _api.ApplyTemplate(new LedgerPlanApplyRequest { BuildingId = _env.Building.Id, Mode = LedgerPlanApplyMode.AddMissing }, default);
        Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task LaListaDelPlanIncluyeElNivelYLasFuncionesParaLaPantalla()
    {
        _env.SeedTemplate();
        var all = await List();
        Assert.Equal(FinanceChartTemplate.Nodes.Count, all.Count);
        var ande = all.Single(c => c.Code == "5.02.01");
        Assert.True(ande.IsTemplate);
        Assert.Equal("Expense.Ande", ande.SystemKey);
        Assert.Equal(BuildingExpenseCategory.Ande, ande.ExpenseCategory);
        Assert.False(all.Single(c => c.Code == "5.02").IsTemplate);
        Assert.True(all.Single(c => c.Code == "5.02").HasChildren);
        Assert.Null(all.Single(c => c.Code == "5.02").ExpenseCategory);
        Assert.Equal(LedgerCategoryType.Asset, all.Single(c => c.Code == "1.1.01").Type);
    }
}
