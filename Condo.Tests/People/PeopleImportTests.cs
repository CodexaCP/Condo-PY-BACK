using ClosedXML.Excel;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Condo.Tests.People;

// Carga masiva de propietarios y de residentes desde Excel: plantilla, lectura, validaciones, reutilizacion de personas que ya existen,
// asignacion a unidades y guardado todo o nada. Las dos cargas son independientes entre si.
public class PeopleImportTests
{
    // ─── Datos y utilidades ──────────────────────────────────────────────────

    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly ImportBuildingRef Central = new(Guid.NewGuid(), CompanyId, "Edificio Central", "CEN");
    private static readonly ImportUnitRef U101 = new(Guid.NewGuid(), Central.Id, CompanyId, "101");
    private static readonly ImportUnitRef U102 = new(Guid.NewGuid(), Central.Id, CompanyId, "102");

    private static byte[] Fill(byte[] template, string sheetName, params string?[][] rows)
    {
        using var input = new MemoryStream(template);
        using var workbook = new XLWorkbook(input);
        var sheet = workbook.Worksheet(sheetName);
        for (var i = 0; i < rows.Length; i++)
        {
            for (var c = 0; c < rows[i].Length; c++)
            {
                if (rows[i][c] is { } value) sheet.Cell(PeopleImportCommon.FirstDataRow + i, c + 1).Value = value;
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static OwnersImportService.Context OwnersContext(
        IEnumerable<OwnersImportService.ExistingPerson>? users = null,
        Dictionary<Guid, IReadOnlyList<OwnersImportService.ExistingUnitOwner>>? unitOwners = null,
        IEnumerable<string>? usernames = null)
    {
        var all = (users ?? []).ToList();
        return new OwnersImportService.Context
        {
            BuildingsByLabel = new Dictionary<string, ImportBuildingRef> { [PeopleImportCommon.NormalizeLabel(Central.Label)] = Central },
            Units = PeopleImportData.ByKey([U101, U102]),
            OwnersByDocument = all.Where(u => u.Role == UserRole.Owner && u.DocumentNumber != null)
                .ToDictionary(u => PeopleImportCommon.NormalizeDocument(u.DocumentNumber)),
            UsersByEmail = all.ToDictionary(u => u.Email.ToLowerInvariant()),
            Usernames = (usernames ?? []).ToHashSet(),
            UnitOwners = unitOwners ?? new Dictionary<Guid, IReadOnlyList<OwnersImportService.ExistingUnitOwner>>()
        };
    }

    private static List<OwnersImportService.ValidatedRow> ValidateOwners(OwnersImportService.Context context, params string?[][] rows)
    {
        var template = OwnersImportService.BuildTemplate([Central], [U101, U102]);
        var file = Fill(template, OwnersImportService.SheetName, rows);
        var (parsed, error) = OwnersImportService.Parse(new MemoryStream(file));
        Assert.Null(error);
        return OwnersImportService.Validate(parsed!, context);
    }

    // Nombre, apellido, tipo doc, documento, correo, telefono, usuario, edificio, unidad, %, principal
    private static string?[] Owner(string first, string doc, string email, string? unit = null, string? pct = null, string? primary = null, string? username = null) =>
        [first, "Gómez", null, doc, email, null, username, unit is null ? null : Central.Label, unit, pct, primary];

    // ─── Propietarios: lectura y reglas ──────────────────────────────────────

    [Fact]
    public void La_plantilla_de_propietarios_se_lee_y_arma_el_usuario_con_el_correo()
    {
        var rows = ValidateOwners(OwnersContext(),
            Owner("Ana", "1234567", "Ana.Gomez@Mail.com", "101", "60", "Si"),
            Owner("Beto", "7654321", "beto@mail.com", "101", "40"));

        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal("ana.gomez", rows[0].Username);
        Assert.Equal("ana.gomez@mail.com", rows[0].Email);
        Assert.Equal("Ana Gómez", rows[0].FullName);
        Assert.True(rows[0].IsPrimary);
        Assert.False(rows[1].IsPrimary);            // la unidad ya tiene principal
        Assert.Equal(40m, rows[1].Percentage);
    }

    [Fact]
    public void Un_propietario_con_varias_unidades_es_una_sola_persona()
    {
        var rows = ValidateOwners(OwnersContext(),
            Owner("Ana", "1.234.567", "ana@mail.com", "101"),
            Owner("Ana", "1234567", "ana@mail.com", "102"));

        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal(rows[0].DocKey, rows[1].DocKey);                 // 1.234.567 = 1234567
        Assert.Equal(2, rows.Select(r => r.Unit!.Id).Distinct().Count());
        Assert.All(rows, r => Assert.True(r.IsPrimary));              // principal de su propia unidad
    }

    [Fact]
    public void Sin_edificio_ni_unidad_solo_se_da_de_alta_a_la_persona()
    {
        var rows = ValidateOwners(OwnersContext(), Owner("Ana", "1234567", "ana@mail.com"));

        Assert.Null(rows[0].Error);
        Assert.Null(rows[0].Unit);
        Assert.False(rows[0].IsPrimary);
    }

    [Fact]
    public void Una_persona_que_ya_existe_se_reutiliza_y_la_unidad_ya_asignada_no_se_duplica()
    {
        var anaId = Guid.NewGuid();
        var ana = new OwnersImportService.ExistingPerson(anaId, "Ana Gómez", "ana@mail.com", "1234567", UserRole.Owner);
        var unitOwners = new Dictionary<Guid, IReadOnlyList<OwnersImportService.ExistingUnitOwner>>
        {
            [U101.Id] = [new OwnersImportService.ExistingUnitOwner(anaId, null, true)]
        };

        var rows = ValidateOwners(OwnersContext([ana], unitOwners),
            Owner("Ana", "1234567", "ana@mail.com", "101"),
            Owner("Ana", "1234567", "ana@mail.com", "102"));

        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal(anaId, rows[0].Existing!.Id);
        Assert.True(rows[0].AlreadyAssigned);
        Assert.False(rows[1].AlreadyAssigned);
    }

    [Fact]
    public void Cada_fila_con_problemas_de_propietarios_dice_por_que()
    {
        var staff = new OwnersImportService.ExistingPerson(Guid.NewGuid(), "Marta Admin", "marta@mail.com", null, UserRole.CompanyAdmin);
        var otherOwner = new OwnersImportService.ExistingPerson(Guid.NewGuid(), "Pedro Gómez", "pedro@mail.com", "9999999", UserRole.Owner);

        var rows = ValidateOwners(OwnersContext([staff, otherOwner], usernames: ["usado"]),
            [null, "Gómez", null, "111", "a@mail.com", null, null, null, null, null, null],                       // 0 sin nombre
            Owner("Ana", "", "ana@mail.com"),                                                                        // 1 sin documento
            Owner("Ana", "111", "sin-arroba"),                                                                       // 2 correo inválido
            ["Ana", "Gómez", "RUC", "80012345-1", "ana2@mail.com", null, null, null, null, null, null],             // 3 RUC con dígito malo
            ["Ana", "Gómez", null, "112", "ana3@mail.com", null, null, Central.Label, null, null, null],            // 4 edificio sin unidad
            Owner("Ana", "113", "ana4@mail.com", "999"),                                                             // 5 unidad inexistente
            Owner("Ana", "114", "marta@mail.com"),                                                                   // 6 correo de personal
            Owner("Ana", "115", "pedro@mail.com"),                                                                   // 7 correo de otro propietario, otro documento
            Owner("Ana", "116", "ana5@mail.com", null, "50"),                                                        // 8 % sin unidad
            Owner("Ana", "117", "ana6@mail.com", "101", "150"),                                                      // 9 % fuera de rango
            Owner("Ana", "118", "ana7@mail.com", "101", null, "quizas"),                                             // 10 principal inválido
            Owner("Ana", "119", "ana8@mail.com", null, null, null, "Con Espacio"),                                   // 11 usuario inválido
            Owner("Ana", "120", "ana9@mail.com", null, null, null, "usado"),                                       // 12 usuario ocupado
            Owner("Pedro", "9999999", "marta@mail.com"));                                                           // 13 documento de un propietario con el correo de otra cuenta

        Assert.Equal(14, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.Error));
        Assert.Contains("nombre", rows[0].Error);
        Assert.Contains("documento", rows[1].Error);
        Assert.Contains("correo", rows[2].Error);
        Assert.Contains("RUC", rows[3].Error);
        Assert.Contains("unidad", rows[4].Error);
        Assert.Contains("999", rows[5].Error);
        Assert.Contains("no es de propietario", rows[6].Error);
        Assert.Contains("otro documento", rows[7].Error);
        Assert.Contains("junto con edificio y unidad", rows[8].Error);
        Assert.Contains("0", rows[9].Error);
        Assert.Contains("Principal", rows[10].Error);
        Assert.Contains("Usuario inválido", rows[11].Error);
        Assert.Contains("ya existe", rows[12].Error);
        Assert.Contains("otra persona", rows[13].Error);
    }

    [Fact]
    public void Las_reglas_de_la_unidad_se_cuentan_con_lo_que_ya_hay_y_con_las_filas_anteriores()
    {
        var rows = ValidateOwners(OwnersContext(),
            Owner("Ana", "111", "a1@mail.com", "101", "60"),
            Owner("Beto", "222", "b1@mail.com", "101", "30"),
            Owner("Carla", "333", "c1@mail.com", "101", "10"),     // tercer propietario
            Owner("Diego", "444", "d1@mail.com", "102", "70", "Si"),
            Owner("Eva", "555", "e1@mail.com", "102", "50"),       // suma 120%
            Owner("Fede", "666", "f1@mail.com", "102", "20", "Si"), // ya hay principal
            Owner("Ana", "111", "a1@mail.com", "101"));            // la misma persona dos veces en la misma unidad

        Assert.Null(rows[0].Error);
        Assert.Null(rows[1].Error);
        Assert.Contains("2 propietarios", rows[2].Error);
        Assert.Null(rows[3].Error);
        Assert.Contains("100%", rows[4].Error);
        Assert.Contains("ya tiene un propietario principal", rows[5].Error);
        Assert.Contains("fila 5", rows[6].Error);                  // fila 5 de Excel = primera de datos
    }

    [Fact]
    public void Un_correo_o_documento_repetido_con_otros_datos_en_el_archivo_es_error()
    {
        var rows = ValidateOwners(OwnersContext(),
            Owner("Ana", "111", "ana@mail.com"),
            Owner("Ana", "111", "otro@mail.com"),      // mismo documento, otro correo
            Owner("Beto", "222", "ana@mail.com"));     // mismo correo, otro documento

        Assert.Null(rows[0].Error);
        Assert.Contains("otro correo", rows[1].Error);
        Assert.Contains("otro documento", rows[2].Error);
    }

    [Fact]
    public void Si_el_usuario_derivado_del_correo_esta_ocupado_se_le_agrega_un_numero()
    {
        var rows = ValidateOwners(OwnersContext(usernames: ["ana"]),
            Owner("Ana", "111", "ana@mail.com"),
            Owner("Ana", "222", "ana@otro.com"));

        Assert.Equal("ana2", rows[0].Username);
        Assert.Equal("ana3", rows[1].Username);
    }

    [Fact]
    public void La_contrasena_inicial_es_Temporal_con_el_anio_en_curso_a_la_hora_de_Paraguay()
    {
        Assert.Equal("Temporal*2026", OwnersImportService.InitialPassword(new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("Temporal*2026", OwnersImportService.InitialPassword(new DateTime(2027, 1, 1, 2, 0, 0, DateTimeKind.Utc)));   // aun 31/12 en Paraguay
        Assert.Equal("Temporal*2027", OwnersImportService.InitialPassword(new DateTime(2027, 1, 1, 4, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Un_archivo_que_no_es_la_plantilla_de_propietarios_se_rechaza()
    {
        var residents = ResidentsImportService.BuildTemplate([Central], [U101]);
        var (rows, error) = OwnersImportService.Parse(new MemoryStream(residents));
        Assert.Null(rows);
        Assert.Contains("plantilla de propietarios", error);

        var (rows2, error2) = ResidentsImportService.Parse(new MemoryStream(OwnersImportService.BuildTemplate([Central], [U101])));
        Assert.Null(rows2);
        Assert.Contains("plantilla de residentes", error2);
    }

    // ─── Residentes: lectura y reglas ────────────────────────────────────────

    private static readonly DateOnly Today = new(2026, 10, 12);

    private static ResidentsImportService.Context ResidentsContext(
        IEnumerable<(string Doc, ResidentsImportService.ExistingResident Resident)>? residents = null,
        Dictionary<Guid, IReadOnlyList<ResidentsImportService.ExistingAssignment>>? assignments = null) => new()
    {
        BuildingsByLabel = new Dictionary<string, ImportBuildingRef> { [PeopleImportCommon.NormalizeLabel(Central.Label)] = Central },
        Units = PeopleImportData.ByKey([U101, U102]),
        ResidentsByDocument = (residents ?? []).ToDictionary(r => PeopleImportCommon.NormalizeDocument(r.Doc), r => r.Resident),
        UnitAssignments = assignments ?? new Dictionary<Guid, IReadOnlyList<ResidentsImportService.ExistingAssignment>>(),
        Today = Today
    };

    private static List<ResidentsImportService.ValidatedRow> ValidateResidents(ResidentsImportService.Context context, params string?[][] rows)
    {
        var template = ResidentsImportService.BuildTemplate([Central], [U101, U102]);
        var file = Fill(template, ResidentsImportService.SheetName, rows);
        var (parsed, error) = ResidentsImportService.Parse(new MemoryStream(file));
        Assert.Null(error);
        return ResidentsImportService.Validate(parsed!, context);
    }

    // Nombre, tipo doc, documento, correo, telefono, relacion, edificio, unidad, desde, principal, vence contrato
    private static string?[] Res(string name, string doc, string? unit = null, string? relation = null, string? since = null, string? primary = null, string? lease = null) =>
        [name, null, doc, $"{doc}@mail.com", "0981 123456", relation, unit is null ? null : Central.Label, unit, since, primary, lease];

    [Fact]
    public void La_plantilla_de_residentes_se_lee_con_valores_por_defecto()
    {
        var rows = ValidateResidents(ResidentsContext(),
            Res("Luis Pérez", "111", "101", "Inquilino", "01/09/2026", null, "31/08/2027"),
            Res("Mara Pérez", "222", "101", "Familiar"),
            Res("Solo Ficha", "333"));

        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal(ResidentRelationship.Tenant, rows[0].Relationship);
        Assert.Equal(new DateOnly(2026, 9, 1), rows[0].StartDate);
        Assert.Equal(new DateOnly(2027, 8, 31), rows[0].LeaseEnd);
        Assert.True(rows[0].IsPrimary);             // primer residente de la unidad
        Assert.False(rows[1].IsPrimary);
        Assert.Equal(Today, rows[1].StartDate);     // sin fecha = hoy
        Assert.Null(rows[2].Unit);
    }

    [Fact]
    public void Cada_fila_con_problemas_de_residentes_dice_por_que()
    {
        var rows = ValidateResidents(ResidentsContext(),
            ["", null, "111", "a@mail.com", "0981", null, null, null, null, null, null],           // 0 sin nombre
            ["Ana", null, "", "a@mail.com", "0981", null, null, null, null, null, null],           // 1 sin documento
            ["Ana", null, "112", "malo", "0981", null, null, null, null, null, null],              // 2 correo
            ["Ana", null, "113", "a@mail.com", "", null, null, null, null, null, null],            // 3 sin teléfono
            ["Ana", null, "114", "a@mail.com", "abc", null, null, null, null, null, null],         // 4 teléfono inválido
            ["Ana", null, "115", "a@mail.com", "0981", "Vecino", null, null, null, null, null],    // 5 relación
            ["Ana", null, "116", "a@mail.com", "0981", "Familiar", null, null, null, null, "01/01/2027"], // 6 contrato sin inquilino
            Res("Ana", "117", "999"),                                                               // 7 unidad inexistente
            Res("Ana", "118", "101", null, "31/31/2026"),                                           // 8 fecha inválida
            Res("Ana", "119", null, null, null, "Si"));                                             // 9 principal sin unidad

        Assert.Equal(10, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.Error));
        Assert.Contains("nombre", rows[0].Error);
        Assert.Contains("documento", rows[1].Error);
        Assert.Contains("correo", rows[2].Error);
        Assert.Contains("teléfono es obligatorio", rows[3].Error);
        Assert.Contains("teléfono solo", rows[4].Error);
        Assert.Contains("relación", rows[5].Error);
        Assert.Contains("Inquilino", rows[6].Error);
        Assert.Contains("999", rows[7].Error);
        Assert.Contains("fecha", rows[8].Error);
        Assert.Contains("junto con edificio y unidad", rows[9].Error);
    }

    [Fact]
    public void Un_residente_que_ya_vive_en_la_unidad_no_se_duplica_y_un_segundo_principal_es_error()
    {
        var luisId = Guid.NewGuid();
        var luis = ("111", new ResidentsImportService.ExistingResident(luisId, "Luis Pérez"));
        var assignments = new Dictionary<Guid, IReadOnlyList<ResidentsImportService.ExistingAssignment>>
        {
            [U101.Id] = [new ResidentsImportService.ExistingAssignment(luisId, true, new DateOnly(2025, 1, 1), null)],
            [U102.Id] = [new ResidentsImportService.ExistingAssignment(luisId, true, new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1))]   // ya terminó
        };

        var rows = ValidateResidents(ResidentsContext([luis], assignments),
            Res("Luis Pérez", "111", "101"),                  // ya vive ahí
            Res("Mara", "222", "101", null, null, "Si"),      // la unidad ya tiene principal vigente
            Res("Nico", "333", "102"));                       // la anterior residencia terminó: puede ser principal

        Assert.Null(rows[0].Error);
        Assert.True(rows[0].AlreadyAssigned);
        Assert.Contains("residente principal", rows[1].Error);
        Assert.Null(rows[2].Error);
        Assert.True(rows[2].IsPrimary);
    }

    // ─── Flujo completo contra la base (SQLite con el modelo real) ───────────

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string password, string hash) => hash == "hash:" + password;
    }

    private sealed class Scenario : IDisposable
    {
        public TestDb T { get; } = new();
        public Company Company { get; }
        public Building Building { get; }
        public Unit Unit101 { get; }
        public Unit Unit102 { get; }
        public FakeAccessScope Scope { get; }
        public OwnersImportController Owners { get; }
        public ResidentsImportController Residents { get; }

        public Scenario(string role = "CompanyAdmin")
        {
            Company = T.AddCompany();
            Building = T.AddBuilding(Company, "Edificio Sol");
            Unit101 = T.AddUnit(Building, "101");
            Unit102 = T.AddUnit(Building, "102");

            var tenant = new FakeTenantContext { Role = role, CompanyId = role == "SuperAdmin" ? null : Company.Id };
            Scope = new FakeAccessScope(tenant);
            Scope.Buildings.Add(Building.Id);

            var credits = new OwnerCreditService(T.Db);
            Owners = new OwnersImportController(T.Db, Scope, new PlainHasher(), credits, BuildHandover(tenant), NullLogger<OwnersImportController>.Instance);
            Residents = new ResidentsImportController(T.Db, Scope, NullLogger<ResidentsImportController>.Instance);
        }

        private MarketplaceHandoverService BuildHandover(FakeTenantContext tenant)
        {
            var gate = new MarketplaceModuleGate(T.Db);
            var scope = new MarketplaceScope(T.Db, tenant, Scope, gate);
            var audit = new MarketplaceAudit(T.Db, tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var listings = new MarketplaceListingService(T.Db, tenant, scope, audit);
            var push = new PushDispatcher(T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance);
            return new MarketplaceHandoverService(T.Db, tenant, scope, audit, push, listings);
        }

        public string BuildingLabel => $"{Building.Name} ({Building.Code})";

        public void Dispose() => T.Dispose();
    }

    private static IFormFile AsFile(byte[] bytes, string name = "carga.xlsx") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    private static T Value<T>(ActionResult<T> result) => Assert.IsType<OkObjectResult>(result.Result).Value is T v ? v : throw new InvalidCastException();

    private static byte[] OwnersFile(Scenario s, params string?[][] rows)
    {
        var template = OwnersImportService.BuildTemplate(
            [new ImportBuildingRef(s.Building.Id, s.Company.Id, s.Building.Name, s.Building.Code)], []);
        return Fill(template, OwnersImportService.SheetName, rows);
    }

    private static string?[] OwnerRow(Scenario s, string first, string doc, string email, string? unit, string? pct = null, string? primary = null) =>
        [first, "Gómez", null, doc, email, "0981123456", null, unit is null ? null : s.BuildingLabel, unit, pct, primary];

    [Fact]
    public async Task Importa_propietarios_con_contrasena_temporal_y_los_asigna_a_sus_unidades()
    {
        using var s = new Scenario();
        var file = OwnersFile(s,
            OwnerRow(s, "Ana", "111", "ana@mail.com", "101", "60", "Si"),
            OwnerRow(s, "Beto", "222", "beto@mail.com", "101", "40"),
            OwnerRow(s, "Ana", "111", "ana@mail.com", "102"),
            OwnerRow(s, "Sin", "333", "sin@mail.com", null));

        var preview = Value(await s.Owners.Import(AsFile(file), confirm: false, null, CancellationToken.None));
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(3, preview.NewPeople);
        Assert.Equal(3, preview.NewAssignments);
        Assert.False(preview.Created);
        Assert.Equal(0, await s.T.Db.ApplicationUsers.CountAsync(u => u.Role == UserRole.Owner));   // la vista previa no guarda

        var done = Value(await s.Owners.Import(AsFile(file), confirm: true, null, CancellationToken.None));
        Assert.True(done.Created);
        Assert.Equal($"Temporal*{DateTime.UtcNow.AddHours(-3).Year}", done.InitialPassword);

        using var check = s.T.NewContext();
        var owners = await check.ApplicationUsers.Where(u => u.Role == UserRole.Owner).ToListAsync();
        Assert.Equal(3, owners.Count);
        Assert.All(owners, o =>
        {
            Assert.Equal(s.Company.Id, o.CompanyId);
            Assert.True(o.MustChangePassword);
            Assert.True(o.IsActive);
            Assert.Equal("hash:" + done.InitialPassword, o.PasswordHash);
        });

        var ana = owners.Single(o => o.DocumentNumber == "111");
        var links = await check.UnitOwners.Where(l => l.OwnerId == ana.Id).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.True(l.IsPrimary));
        var u101 = await check.UnitOwners.Where(l => l.UnitId == s.Unit101.Id).ToListAsync();
        Assert.Equal(2, u101.Count);
        Assert.Equal(60m, u101.Single(l => l.OwnerId == ana.Id).OwnershipPercentage);
        Assert.Single(u101, l => l.IsPrimary);
        var withoutUnitId = owners.Single(o => o.DocumentNumber == "333").Id;
        Assert.Empty(await check.UnitOwners.Where(l => l.OwnerId == withoutUnitId).ToListAsync());
    }

    [Fact]
    public async Task Importar_dos_veces_el_mismo_archivo_no_duplica_nada()
    {
        using var s = new Scenario();
        var file = OwnersFile(s, OwnerRow(s, "Ana", "111", "ana@mail.com", "101"));

        Value(await s.Owners.Import(AsFile(file), true, null, CancellationToken.None));
        var again = Value(await s.Owners.Import(AsFile(file), true, null, CancellationToken.None));

        Assert.Equal(0, again.NewPeople);
        Assert.Equal(1, again.ExistingPeople);
        Assert.Equal(0, again.NewAssignments);
        Assert.Equal(1, again.AlreadyAssigned);
        Assert.Equal("Ya existe", again.Rows[0].Person);
        Assert.Equal("Ya estaba asignada", again.Rows[0].Assignment);
        Assert.Equal(1, await s.T.Db.ApplicationUsers.CountAsync(u => u.Role == UserRole.Owner));
        Assert.Equal(1, await s.T.Db.UnitOwners.CountAsync());
    }

    [Fact]
    public async Task Si_alguna_fila_tiene_errores_no_se_guarda_nada()
    {
        using var s = new Scenario();
        var file = OwnersFile(s,
            OwnerRow(s, "Ana", "111", "ana@mail.com", "101"),
            OwnerRow(s, "Beto", "222", "beto@mail.com", "999"));   // unidad inexistente

        var result = Value(await s.Owners.Import(AsFile(file), true, null, CancellationToken.None));

        Assert.Equal(1, result.ErrorRows);
        Assert.False(result.Created);
        Assert.Contains("No se guardó nada", result.Message);
        Assert.Equal(0, await s.T.Db.ApplicationUsers.CountAsync(u => u.Role == UserRole.Owner));
        Assert.Equal(0, await s.T.Db.UnitOwners.CountAsync());
    }

    [Fact]
    public async Task Solo_importan_el_super_admin_y_el_administrador_de_toda_la_empresa()
    {
        using var manager = new Scenario("BuildingManager");
        var file = OwnersFile(manager, OwnerRow(manager, "Ana", "111", "ana@mail.com", "101"));
        Assert.IsType<ForbidResult>((await manager.Owners.Import(AsFile(file), false, null, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>(await manager.Owners.DownloadTemplate(null, CancellationToken.None));
        Assert.IsType<ForbidResult>((await manager.Residents.Import(AsFile(file), false, null, CancellationToken.None)).Result);

        using var superAdmin = new Scenario("SuperAdmin");
        var superFile = OwnersFile(superAdmin, OwnerRow(superAdmin, "Ana", "111", "ana@mail.com", "101"));
        // El SuperAdmin tiene que elegir la empresa.
        Assert.IsType<BadRequestObjectResult>((await superAdmin.Owners.Import(AsFile(superFile), false, null, CancellationToken.None)).Result);
        var ok = Value(await superAdmin.Owners.Import(AsFile(superFile), true, superAdmin.Company.Id, CancellationToken.None));
        Assert.True(ok.Created);
        Assert.Equal(superAdmin.Company.Id, (await superAdmin.T.Db.ApplicationUsers.SingleAsync(u => u.Role == UserRole.Owner)).CompanyId);
    }

    [Fact]
    public async Task Un_administrador_no_puede_cargar_personas_en_edificios_de_otra_empresa()
    {
        using var s = new Scenario();
        var foreignCompany = s.T.AddCompany();
        var foreignBuilding = s.T.AddBuilding(foreignCompany, "Edificio Ajeno");
        s.T.AddUnit(foreignBuilding, "101");

        var template = OwnersImportService.BuildTemplate(
            [new ImportBuildingRef(foreignBuilding.Id, foreignCompany.Id, foreignBuilding.Name, foreignBuilding.Code)], []);
        var file = Fill(template, OwnersImportService.SheetName,
            ["Ana", "Gómez", null, "111", "ana@mail.com", null, null, $"{foreignBuilding.Name} ({foreignBuilding.Code})", "101", null, null]);

        var result = Value(await s.Owners.Import(AsFile(file), true, null, CancellationToken.None));

        Assert.Equal(1, result.ErrorRows);
        Assert.Contains("no está en la lista", result.Rows[0].Error);
        Assert.Equal(0, await s.T.Db.UnitOwners.CountAsync());
    }

    [Fact]
    public async Task Importa_residentes_vinculando_la_cuenta_por_correo_y_asignandolos_a_la_unidad()
    {
        using var s = new Scenario();
        var account = s.T.AddUser(s.Company, UserRole.Resident, "Luis");
        account.Email = "luis@mail.com";
        s.T.Db.SaveChanges();

        var template = ResidentsImportService.BuildTemplate(
            [new ImportBuildingRef(s.Building.Id, s.Company.Id, s.Building.Name, s.Building.Code)], []);
        var file = Fill(template, ResidentsImportService.SheetName,
            ["Luis Pérez", null, "111", "luis@mail.com", "0981 123456", "Inquilino", s.BuildingLabel, "101", "01/09/2026", null, "31/08/2027"],
            ["Mara Pérez", null, "222", "mara@mail.com", "0981 654321", "Familiar", s.BuildingLabel, "101", null, null, null]);

        var done = Value(await s.Residents.Import(AsFile(file), true, null, CancellationToken.None));
        Assert.True(done.Created);
        Assert.Equal(2, done.NewPeople);
        Assert.Equal(2, done.NewAssignments);
        Assert.Null(done.InitialPassword);   // los residentes no reciben cuenta

        using var check = s.T.NewContext();
        var luis = await check.Residents.SingleAsync(r => r.DocumentNumber == "111");
        Assert.Equal(account.Id, luis.ApplicationUserId);
        Assert.Equal(ResidentRelationship.Tenant, luis.Relationship);
        Assert.Equal(new DateOnly(2027, 8, 31), luis.LeaseEndDate);
        Assert.Null((await check.Residents.SingleAsync(r => r.DocumentNumber == "222")).ApplicationUserId);

        var luisLink = await check.UnitResidents.SingleAsync(l => l.ResidentId == luis.Id);
        Assert.True(luisLink.IsPrimary);
        Assert.Equal(new DateOnly(2026, 9, 1), luisLink.StartDate);
        Assert.Null(luisLink.EndDate);
        Assert.False((await check.UnitResidents.SingleAsync(l => l.Id != luisLink.Id)).IsPrimary);
        Assert.Equal(0, await check.UnitOwners.CountAsync());   // no toca propietarios
    }

    [Fact]
    public async Task Las_dos_cargas_son_independientes_una_no_exige_la_otra()
    {
        using var s = new Scenario();
        var ownersFile = OwnersFile(s, OwnerRow(s, "Ana", "111", "ana@mail.com", "101"));
        Value(await s.Owners.Import(AsFile(ownersFile), true, null, CancellationToken.None));

        // Sin residentes cargados, los propietarios ya quedaron completos; y viceversa.
        Assert.Equal(0, await s.T.Db.Residents.CountAsync());
        Assert.Equal(1, await s.T.Db.UnitOwners.CountAsync());

        var template = ResidentsImportService.BuildTemplate(
            [new ImportBuildingRef(s.Building.Id, s.Company.Id, s.Building.Name, s.Building.Code)], []);
        var residentsFile = Fill(template, ResidentsImportService.SheetName,
            ["Solo Ficha", null, "333", "solo@mail.com", "0981 111222", null, null, null, null, null, null]);
        var result = Value(await s.Residents.Import(AsFile(residentsFile), true, null, CancellationToken.None));

        Assert.True(result.Created);
        Assert.Equal("Sin unidad", result.Rows[0].Assignment);
        Assert.Equal(0, await s.T.Db.UnitResidents.CountAsync());
        Assert.Equal(1, await s.T.Db.UnitOwners.CountAsync());
    }
}
