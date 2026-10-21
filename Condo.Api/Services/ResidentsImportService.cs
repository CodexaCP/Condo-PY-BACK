using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

// Carga masiva de residentes: una fila por persona y unidad. El residente se crea (o se reutiliza si ya existe en la empresa por su
// documento) y, si la fila trae edificio y unidad, se lo asigna a esa unidad. El edificio y la unidad son opcionales: sin ellos solo se
// da de alta la ficha. No depende de la carga de propietarios. Logica pura (sin base de datos) para poder probarla.
public static partial class ResidentsImportService
{
    public const string SheetName = "Residentes";
    public const string TemplateMarker = "condopy-residentes-v1";

    public static readonly string[] Headers =
    [
        "Nombre completo*", "Tipo de documento", "Número de documento*", "Correo*", "Teléfono*", "Relación",
        "Edificio", "Unidad", "Desde", "Principal", "Vence el contrato"
    ];

    private static readonly int[] Widths = [28, 20, 20, 30, 18, 14, 34, 12, 14, 11, 18];

    public sealed record ParsedRow(
        int RowNumber, string FullName, string DocType, string DocNumber, string Email, string Phone, string Relationship,
        string Building, string Unit, string Since, string Primary, string LeaseEnd);

    public sealed record ExistingResident(Guid Id, string FullName);

    // Asignacion ya cargada (por unidad): sirve para no duplicar y para respetar un solo principal vigente.
    public sealed record ExistingAssignment(Guid ResidentId, bool IsPrimary, DateOnly StartDate, DateOnly? EndDate);

    public sealed class Context
    {
        public required IReadOnlyDictionary<string, ImportBuildingRef> BuildingsByLabel { get; init; }
        public required IReadOnlyDictionary<(Guid BuildingId, string Code), ImportUnitRef> Units { get; init; }
        // Residentes de la empresa por documento (clave normalizada con PeopleImportCommon.NormalizeDocument).
        public required IReadOnlyDictionary<string, ExistingResident> ResidentsByDocument { get; init; }
        public required IReadOnlyDictionary<Guid, IReadOnlyList<ExistingAssignment>> UnitAssignments { get; init; }
        public required DateOnly Today { get; init; }
    }

    public sealed record ValidatedRow(
        ParsedRow Row, string? Error,
        string FullName, string? DocType, string DocNumber, string DocKey, string Email, string Phone,
        ResidentRelationship? Relationship, DateOnly? LeaseEnd, ExistingResident? Existing,
        ImportUnitRef? Unit, ImportBuildingRef? Building, DateOnly StartDate, bool IsPrimary, bool AlreadyAssigned);

    // ─── Plantilla ───────────────────────────────────────────────────────────

    public static byte[] BuildTemplate(IReadOnlyList<ImportBuildingRef> buildings, IReadOnlyList<ImportUnitRef> units)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        PeopleImportCommon.WriteTitle(sheet, "Carga de residentes — Expenses",
            "Completá una fila por persona y unidad desde la fila 5. Para un residente en varias unidades repetí sus datos en una fila por unidad. " +
            "Edificio y Unidad son opcionales (juntos): sin ellos solo se da de alta la ficha. Desde: fecha de inicio (dd/mm/aaaa; vacío = hoy). " +
            "Principal: Si o No (vacío = Si si la unidad aún no tiene residente principal). Vence el contrato: solo para inquilinos.");
        PeopleImportCommon.WriteHeaders(sheet, Headers, Widths);

        var lastRow = PeopleImportCommon.HeaderRow + PeopleImportCommon.MaxRows;
        // Documento, telefono y unidad como texto; las fechas con formato de fecha.
        sheet.Range(PeopleImportCommon.FirstDataRow, 3, lastRow, 3).Style.NumberFormat.Format = "@";
        sheet.Range(PeopleImportCommon.FirstDataRow, 5, lastRow, 5).Style.NumberFormat.Format = "@";
        sheet.Range(PeopleImportCommon.FirstDataRow, 8, lastRow, 8).Style.NumberFormat.Format = "@";
        sheet.Range(PeopleImportCommon.FirstDataRow, 9, lastRow, 9).Style.NumberFormat.Format = "dd/mm/yyyy";
        sheet.Range(PeopleImportCommon.FirstDataRow, 11, lastRow, 11).Style.NumberFormat.Format = "dd/mm/yyyy";

        AddList(sheet, 2, PeopleImportCommon.DocumentTypeList, "Tipo de documento", "Elegí un tipo de la lista.", lastRow);
        AddList(sheet, 6, "\"Inquilino,Familiar,Empleado,Otro\"", "Relación", "Elegí una opción de la lista.", lastRow);
        AddList(sheet, 10, "\"Si,No\"", "Principal", "Elegí Si o No.", lastRow);

        PeopleImportCommon.AddReferenceSheets(workbook, sheet, 7, buildings, units);
        PeopleImportCommon.AddMarker(workbook, TemplateMarker);
        return PeopleImportCommon.Save(workbook);
    }

    private static void AddList(IXLWorksheet sheet, int column, string list, string title, string message, int lastRow)
    {
        var validation = sheet.Range(PeopleImportCommon.FirstDataRow, column, lastRow, column).CreateDataValidation();
        validation.List(list, true);
        validation.ErrorStyle = XLErrorStyle.Stop;
        validation.ErrorTitle = title;
        validation.ErrorMessage = message;
        validation.IgnoreBlanks = true;
    }

    // ─── Lectura ─────────────────────────────────────────────────────────────

    public static (List<ParsedRow>? Rows, string? Error) Parse(Stream stream)
    {
        var (workbook, sheet, error) = PeopleImportCommon.Open(stream, SheetName, TemplateMarker, "residentes", Headers);
        if (workbook is null || sheet is null) return (null, error);

        using (workbook)
        {
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? PeopleImportCommon.HeaderRow;
            var rows = new List<ParsedRow>();
            for (var r = PeopleImportCommon.FirstDataRow; r <= lastRow; r++)
            {
                var cells = Enumerable.Range(1, Headers.Length).Select(c => PeopleImportCommon.CellText(sheet.Cell(r, c))).ToArray();
                if (cells.All(c => c.Length == 0)) continue;

                rows.Add(new ParsedRow(r, cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7], cells[8], cells[9], cells[10]));
            }

            return (rows, null);
        }
    }

    // ─── Validacion ──────────────────────────────────────────────────────────

    private sealed class UnitState
    {
        public List<(string PersonKey, bool IsPrimary, DateOnly Start, DateOnly? End, int? FileRow)> Assignments { get; } = [];
    }

    public static List<ValidatedRow> Validate(IReadOnlyList<ParsedRow> rows, Context context)
    {
        var result = new List<ValidatedRow>();
        var peopleByDocument = new Dictionary<string, (ExistingResident? Existing, int FirstRow)>();
        var units = new Dictionary<Guid, UnitState>();

        foreach (var row in rows) result.Add(ValidateRow(row, context, peopleByDocument, units));
        return result;
    }

    private static ValidatedRow ValidateRow(
        ParsedRow row, Context context, Dictionary<string, (ExistingResident? Existing, int FirstRow)> peopleByDocument, Dictionary<Guid, UnitState> units)
    {
        var fullName = row.FullName.Trim();
        var email = row.Email.Trim().ToLowerInvariant();
        var docNumber = row.DocNumber.Trim();
        var docKey = PeopleImportCommon.NormalizeDocument(docNumber);
        var phone = row.Phone.Trim();

        ValidatedRow Fail(string message) => new(
            row, message, fullName, null, docNumber, docKey, email, phone, null, null, null, null, null, context.Today, false, false);

        // 1) Datos de la persona (las mismas reglas del alta manual de residentes)
        if (fullName.Length == 0) return Fail("El nombre completo es obligatorio.");
        if (fullName.Length > 200) return Fail("El nombre no puede superar los 200 caracteres.");

        var (docType, docTypeError) = PeopleImportCommon.ParseDocumentType(row.DocType);
        if (docTypeError is not null) return Fail(docTypeError);
        if (docNumber.Length == 0) return Fail("El número de documento es obligatorio.");
        if (PersonRules.ValidateDocument(docType, docNumber) is { } docError) return Fail(docError);

        if (email.Length == 0) return Fail("El correo es obligatorio.");
        if (!EmailRegex().IsMatch(email)) return Fail("El correo no tiene un formato válido.");

        if (phone.Length == 0) return Fail("El teléfono es obligatorio.");
        if (!PhoneRegex().IsMatch(phone)) return Fail("El teléfono solo puede contener números, espacios, paréntesis, + o guiones.");

        ResidentRelationship? relationship = null;
        switch (PeopleImportCommon.Normalize(row.Relationship))
        {
            case "": break;
            case "inquilino": case "tenant": relationship = ResidentRelationship.Tenant; break;
            case "familiar": case "family": relationship = ResidentRelationship.Family; break;
            case "empleado": case "employee": relationship = ResidentRelationship.Employee; break;
            case "otro": case "other": relationship = ResidentRelationship.Other; break;
            default: return Fail("La relación debe ser Inquilino, Familiar, Empleado u Otro.");
        }

        var (leaseEnd, leaseError) = PeopleImportCommon.ParseDate(row.LeaseEnd, "El vencimiento del contrato");
        if (leaseError is not null) return Fail(leaseError);
        if (leaseEnd is not null && relationship != ResidentRelationship.Tenant)
            return Fail("El vencimiento del contrato solo se usa con la relación Inquilino.");

        // 2) Edificio y unidad (opcionales, pero juntos)
        ImportBuildingRef? building = null;
        ImportUnitRef? unit = null;
        var hasBuilding = row.Building.Trim().Length > 0;
        var hasUnit = row.Unit.Trim().Length > 0;
        if (hasBuilding != hasUnit) return Fail(hasBuilding ? "Indicá también la unidad (o dejá edificio y unidad vacíos)." : "Indicá también el edificio (o dejá edificio y unidad vacíos).");
        if (hasBuilding)
        {
            if (!context.BuildingsByLabel.TryGetValue(PeopleImportCommon.NormalizeLabel(row.Building), out building))
                return Fail("El edificio no está en la lista: elegilo del desplegable (hoja Edificios).");
            var code = PeopleImportCommon.NormalizeUnitCode(row.Unit);
            if (!context.Units.TryGetValue((building.Id, code), out unit))
                return Fail($"La unidad {code} no existe en {building.Name}: se consultan en la hoja Unidades.");
        }

        var (since, sinceError) = PeopleImportCommon.ParseDate(row.Since, "La fecha de inicio");
        if (sinceError is not null) return Fail(sinceError);
        var (explicitPrimary, primaryError) = PeopleImportCommon.ParseYesNo(row.Primary, "Principal");
        if (primaryError is not null) return Fail(primaryError);
        if (unit is null && (since is not null || explicitPrimary is not null))
            return Fail("Desde y Principal solo se usan junto con edificio y unidad.");
        var startDate = since ?? context.Today;

        // 3) Quien es la persona: la del archivo (mismo documento), una que ya existe o una nueva
        ExistingResident? existing;
        if (peopleByDocument.TryGetValue(docKey, out var inFile)) existing = inFile.Existing;
        else context.ResidentsByDocument.TryGetValue(docKey, out existing);

        // 4) Asignacion a la unidad
        var alreadyAssigned = false;
        var isPrimary = false;
        UnitState? state = null;
        var personKey = existing is not null ? existing.Id.ToString() : "new:" + docKey;
        if (unit is not null)
        {
            if (!units.TryGetValue(unit.Id, out state))
            {
                state = new UnitState();
                if (context.UnitAssignments.TryGetValue(unit.Id, out var current))
                    state.Assignments.AddRange(current.Select(a => (a.ResidentId.ToString(), a.IsPrimary, a.StartDate, a.EndDate, (int?)null)));
            }

            // Asignaciones que siguen vigentes a la fecha de inicio de esta fila.
            var active = state.Assignments.Where(a => a.End is null || a.End >= startDate).ToList();

            var same = active.FirstOrDefault(a => a.PersonKey == personKey);
            if (same.PersonKey is not null)
            {
                if (same.FileRow is { } earlier)
                    return Fail($"Esta persona ya está asignada a esa unidad en la fila {earlier}.");
                alreadyAssigned = true;   // ya vive en la unidad segun el sistema: no se toca
            }
            else
            {
                var hasPrimary = active.Any(a => a.IsPrimary);
                if (explicitPrimary == true && hasPrimary)
                    return Fail($"La unidad {unit.Code} ya tiene un residente principal vigente.");
                isPrimary = explicitPrimary ?? !hasPrimary;
            }
        }

        // 5) Todo bien: recien ahora se registra en el estado compartido
        peopleByDocument.TryAdd(docKey, (existing, row.RowNumber));
        if (unit is not null && state is not null)
        {
            if (!alreadyAssigned) state.Assignments.Add((personKey, isPrimary, startDate, null, row.RowNumber));
            units[unit.Id] = state;
        }

        return new ValidatedRow(
            row, null, existing?.FullName ?? fullName, docType, docNumber, docKey, email, phone, relationship, leaseEnd, existing,
            unit, building, startDate, isPrimary, alreadyAssigned);
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[0-9 ()+\-]+$")]
    private static partial Regex PhoneRegex();
}
