using ClosedXML.Excel;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Condo.Tests.People;

// Carga masiva de unidades desde Excel (SuperAdmin): plantilla, lectura, validaciones y guardado todo o nada.
public class UnitImportTests
{
    private static readonly UnitImportService.BuildingRef Central =
        new(Guid.NewGuid(), Guid.NewGuid(), "Edificio Central", "CEN", "Empresa Uno");

    private static byte[] Fill(byte[] template, params string?[][] rows)
    {
        using var input = new MemoryStream(template);
        using var workbook = new XLWorkbook(input);
        var sheet = workbook.Worksheet(UnitImportService.SheetName);
        for (var i = 0; i < rows.Length; i++)
        {
            for (var c = 0; c < rows[i].Length; c++)
            {
                if (rows[i][c] is { } value) sheet.Cell(UnitImportService.FirstDataRow + i, c + 1).Value = value;
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static List<UnitImportService.ValidatedRow> Validate(
        byte[] file, UnitImportService.BuildingRef building, params (Guid, string)[] existing)
    {
        var (rows, error) = UnitImportService.Parse(new MemoryStream(file));
        Assert.Null(error);
        var byLabel = new Dictionary<string, UnitImportService.BuildingRef> { [UnitImportService.NormalizeLabel(building.Label)] = building };
        return UnitImportService.Validate(rows!, byLabel, existing.ToHashSet());
    }

    [Fact]
    public void La_plantilla_se_lee_y_valida_cada_dato_de_la_unidad()
    {
        var template = UnitImportService.BuildTemplate([Central]);
        var file = Fill(template,
            [Central.Label, "101", "1", "0.5", "Si"],
            [Central.Label, "a-02", "PB", "0,25%", "No"],
            [Central.Label, "103", "2", null, null]);

        var rows = Validate(file, Central);

        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal("101", rows[0].Code);
        Assert.Equal(0.5m, rows[0].Coefficient);
        Assert.Equal("A-02", rows[1].Code);
        Assert.Equal(0.0025m, rows[1].Coefficient);
        Assert.False(rows[1].IsActive);
        Assert.Equal(0m, rows[2].Coefficient);
        Assert.True(rows[2].IsActive);
    }

    [Fact]
    public void Cada_fila_con_problemas_dice_por_que()
    {
        var template = UnitImportService.BuildTemplate([Central]);
        var file = Fill(template,
            ["Edificio que no existe", "101", "1", "0.1", "Si"],
            [Central.Label, "", "1", "0.1", "Si"],
            [Central.Label, "10 1", "1", "0.1", "Si"],
            [Central.Label, "102", "", "0.1", "Si"],
            [Central.Label, "103", "1", "abc", "Si"],
            [Central.Label, "104", "1", "1.5", "Si"],
            [Central.Label, "105", "1", "0.1", "quizas"],
            [Central.Label, "106", "1", "0.1", "Si"],
            [Central.Label, "106", "1", "0.1", "Si"],
            [Central.Label, "107", "1", "0.1", "Si"]);

        var rows = Validate(file, Central, (Central.Id, "107"));

        Assert.All(rows.Take(7), r => Assert.NotNull(r.Error));
        Assert.Null(rows[7].Error);
        Assert.Contains("fila", rows[8].Error);
        Assert.Contains("Ya existe", rows[9].Error);
    }

    [Fact]
    public void Un_archivo_que_no_es_la_plantilla_se_rechaza()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Otra");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var (rows, error) = UnitImportService.Parse(new MemoryStream(stream.ToArray()));

        Assert.Null(rows);
        Assert.NotNull(error);
    }

    private sealed class Scenario : IDisposable
    {
        public TestDb T { get; } = new();
        public UnitsController Controller { get; }
        public UnitImportService.BuildingRef Ref { get; }
        public Guid BuildingId { get; }

        public Scenario(string role = "SuperAdmin")
        {
            var company = T.AddCompany("Empresa Uno");
            var building = T.AddBuilding(company, "Edificio Central");
            BuildingId = building.Id;
            Ref = new UnitImportService.BuildingRef(building.Id, company.Id, building.Name, building.Code, company.Name);
            var tenant = new FakeTenantContext { Role = role, CompanyId = company.Id };
            Controller = new UnitsController(T.Db, new FakeAccessScope(tenant));
        }

        public static IFormFile Upload(byte[] bytes) =>
            new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "unidades.xlsx");

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public async Task Sin_confirmar_solo_muestra_la_vista_previa_y_confirmando_crea_todas_las_unidades()
    {
        using var s = new Scenario();
        var template = UnitImportService.BuildTemplate([s.Ref]);
        var file = Fill(template,
            [s.Ref.Label, "101", "1", "0.6", "Si"],
            [s.Ref.Label, "102", "1", "0.4", "No"]);

        var preview = Assert.IsType<OkObjectResult>((await s.Controller.Import(Scenario.Upload(file), false, CancellationToken.None)).Result);
        var previewDto = (UnitImportResultDto)preview.Value!;
        Assert.Equal(2, previewDto.ValidRows);
        Assert.Equal(0, previewDto.Created);
        Assert.Null(previewDto.Buildings.Single().Warning);
        Assert.Equal(0, await s.T.Db.Units.CountAsync());

        var done = Assert.IsType<OkObjectResult>((await s.Controller.Import(Scenario.Upload(file), true, CancellationToken.None)).Result);
        Assert.Equal(2, ((UnitImportResultDto)done.Value!).Created);

        using var check = s.T.NewContext();
        var units = await check.Units.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
        Assert.Equal(["101", "102"], units.Select(u => u.Code).ToArray());
        Assert.False(units[1].IsActive);
        Assert.All(units, u => Assert.Equal(s.BuildingId, u.BuildingId));
    }

    [Fact]
    public async Task Con_un_error_no_se_crea_ninguna_unidad()
    {
        using var s = new Scenario();
        var template = UnitImportService.BuildTemplate([s.Ref]);
        var file = Fill(template,
            [s.Ref.Label, "101", "1", "0.5", "Si"],
            [s.Ref.Label, "", "1", "0.5", "Si"]);

        var result = Assert.IsType<OkObjectResult>((await s.Controller.Import(Scenario.Upload(file), true, CancellationToken.None)).Result);
        var dto = (UnitImportResultDto)result.Value!;

        Assert.Equal(1, dto.ErrorRows);
        Assert.Equal(0, dto.Created);
        Assert.NotNull(dto.Message);
        Assert.Equal(0, await s.T.Db.Units.CountAsync());
    }

    [Fact]
    public async Task Avisa_si_los_coeficientes_del_edificio_no_suman_1()
    {
        using var s = new Scenario();
        var file = Fill(UnitImportService.BuildTemplate([s.Ref]), [s.Ref.Label, "101", "1", "0.3", "Si"]);

        var result = Assert.IsType<OkObjectResult>((await s.Controller.Import(Scenario.Upload(file), false, CancellationToken.None)).Result);

        Assert.NotNull(((UnitImportResultDto)result.Value!).Buildings.Single().Warning);
    }

    [Fact]
    public async Task Solo_el_superadmin_descarga_la_plantilla_e_importa()
    {
        using var s = new Scenario(role: "CompanyAdmin");

        Assert.IsType<ForbidResult>(await s.Controller.DownloadImportTemplate(CancellationToken.None));
        var file = Fill(UnitImportService.BuildTemplate([s.Ref]), [s.Ref.Label, "101", "1", "1", "Si"]);
        Assert.IsType<ForbidResult>((await s.Controller.Import(Scenario.Upload(file), true, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task La_plantilla_trae_los_edificios_para_elegir()
    {
        using var s = new Scenario();

        var result = Assert.IsType<FileContentResult>(await s.Controller.DownloadImportTemplate(CancellationToken.None));

        using var workbook = new XLWorkbook(new MemoryStream(result.FileContents));
        Assert.Equal(s.Ref.Label, workbook.Worksheet(UnitImportService.BuildingsSheetName).Cell(2, 1).GetString());
    }
}
