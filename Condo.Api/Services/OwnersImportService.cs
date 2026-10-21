using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Condo.Domain.Enums;

namespace Condo.Api.Services;

// Carga masiva de propietarios: una fila por persona y unidad. La persona se crea (o se reutiliza si ya existe en la empresa) y,
// si la fila trae edificio y unidad, se la asigna como propietaria. El edificio y la unidad son opcionales: sin ellos solo se da de
// alta a la persona. No depende de la carga de residentes. La logica es pura (sin base de datos) para poder probarla; el controlador
// arma el contexto, guarda y fija la contraseña inicial.
public static partial class OwnersImportService
{
    public const string SheetName = "Propietarios";
    public const string TemplateMarker = "condopy-propietarios-v1";
    public const int MaxOwnersPerUnit = 2;

    // Contraseña inicial de las cuentas nuevas (la deben cambiar al ingresar): Temporal*<año en curso, hora de Paraguay>.
    public static string InitialPassword(DateTime utcNow) => $"Temporal*{utcNow.AddHours(-3).Year}";

    public static readonly string[] Headers =
    [
        "Nombre*", "Apellido*", "Tipo de documento", "Número de documento*", "Correo*", "Teléfono", "Usuario",
        "Edificio", "Unidad", "% titularidad", "Principal"
    ];

    private static readonly int[] Widths = [18, 20, 20, 20, 30, 18, 18, 34, 12, 14, 11];

    public sealed record ParsedRow(
        int RowNumber, string FirstName, string LastName, string DocType, string DocNumber, string Email, string Phone, string Username,
        string Building, string Unit, string Percentage, string Primary);

    public sealed record ExistingPerson(Guid Id, string FullName, string Email, string? DocumentNumber, UserRole Role);

    public sealed record ExistingUnitOwner(Guid OwnerId, decimal? Percentage, bool IsPrimary);

    // Lo que ya hay en la base y la validacion necesita conocer (cargado por el controlador, siempre de una sola empresa).
    public sealed class Context
    {
        public required IReadOnlyDictionary<string, ImportBuildingRef> BuildingsByLabel { get; init; }
        public required IReadOnlyDictionary<(Guid BuildingId, string Code), ImportUnitRef> Units { get; init; }
        // Propietarios de la empresa por documento (clave normalizada con PeopleImportCommon.NormalizeDocument).
        public required IReadOnlyDictionary<string, ExistingPerson> OwnersByDocument { get; init; }
        // Todas las cuentas de la empresa por correo en minusculas (el correo es unico dentro de la empresa).
        public required IReadOnlyDictionary<string, ExistingPerson> UsersByEmail { get; init; }
        public required IReadOnlySet<string> Usernames { get; init; }
        public required IReadOnlyDictionary<Guid, IReadOnlyList<ExistingUnitOwner>> UnitOwners { get; init; }
    }

    public sealed record ValidatedRow(
        ParsedRow Row, string? Error,
        string FullName, string FirstName, string LastName, string? DocType, string? DocNumber, string DocKey, string Email, string? Phone,
        string Username, ExistingPerson? Existing,
        ImportUnitRef? Unit, ImportBuildingRef? Building, decimal? Percentage, bool IsPrimary, bool AlreadyAssigned);

    // ─── Plantilla ───────────────────────────────────────────────────────────

    public static byte[] BuildTemplate(IReadOnlyList<ImportBuildingRef> buildings, IReadOnlyList<ImportUnitRef> units)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        PeopleImportCommon.WriteTitle(sheet, "Carga de propietarios — Expenses",
            "Completá una fila por persona y unidad desde la fila 5. Para un propietario con varias unidades repetí sus datos en una fila por unidad. " +
            "Edificio y Unidad son opcionales (juntos): sin ellos solo se da de alta a la persona. Usuario vacío = se arma con el correo. " +
            "% titularidad: de 1 a 100 (solo si hay copropietarios). Principal: Si o No (vacío = Si si la unidad aún no tiene principal). " +
            "Las cuentas nuevas reciben una contraseña inicial que deben cambiar al ingresar.");
        PeopleImportCommon.WriteHeaders(sheet, Headers, Widths);

        var lastRow = PeopleImportCommon.HeaderRow + PeopleImportCommon.MaxRows;
        // Documento, telefono y unidad como texto: Excel no convierte "0981..." ni "01" en numeros.
        sheet.Range(PeopleImportCommon.FirstDataRow, 4, lastRow, 4).Style.NumberFormat.Format = "@";
        sheet.Range(PeopleImportCommon.FirstDataRow, 6, lastRow, 6).Style.NumberFormat.Format = "@";
        sheet.Range(PeopleImportCommon.FirstDataRow, 9, lastRow, 9).Style.NumberFormat.Format = "@";

        AddList(sheet, 3, PeopleImportCommon.DocumentTypeList, "Tipo de documento", "Elegí un tipo de la lista.", lastRow);
        AddList(sheet, 11, "\"Si,No\"", "Principal", "Elegí Si o No.", lastRow);

        var percentage = sheet.Range(PeopleImportCommon.FirstDataRow, 10, lastRow, 10).CreateDataValidation();
        percentage.Decimal.Between(0.01, 100);
        percentage.ErrorStyle = XLErrorStyle.Stop;
        percentage.ErrorTitle = "% titularidad";
        percentage.ErrorMessage = "Va de 1 a 100.";
        percentage.IgnoreBlanks = true;

        PeopleImportCommon.AddReferenceSheets(workbook, sheet, 8, buildings, units);
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
        var (workbook, sheet, error) = PeopleImportCommon.Open(stream, SheetName, TemplateMarker, "propietarios", Headers);
        if (workbook is null || sheet is null) return (null, error);

        using (workbook)
        {
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? PeopleImportCommon.HeaderRow;
            var rows = new List<ParsedRow>();
            for (var r = PeopleImportCommon.FirstDataRow; r <= lastRow; r++)
            {
                var cells = Enumerable.Range(1, Headers.Length).Select(c => PeopleImportCommon.CellText(sheet.Cell(r, c))).ToArray();

                // Las filas totalmente vacias (formato sin datos) se ignoran.
                if (cells.All(c => c.Length == 0)) continue;

                rows.Add(new ParsedRow(r, cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7], cells[8], cells[9], cells[10]));
            }

            return (rows, null);
        }
    }

    // ─── Validacion ──────────────────────────────────────────────────────────

    private sealed class UnitState
    {
        public List<(string PersonKey, decimal? Percentage, bool IsPrimary, int? FileRow)> Owners { get; } = [];
    }

    private sealed record PersonInFile(string Email, string Username, ExistingPerson? Existing, int FirstRow);

    public static List<ValidatedRow> Validate(IReadOnlyList<ParsedRow> rows, Context context)
    {
        var result = new List<ValidatedRow>();
        var peopleByDocument = new Dictionary<string, PersonInFile>();
        var emailOwnerInFile = new Dictionary<string, (string DocKey, int Row)>();
        var usernames = new HashSet<string>(context.Usernames);
        var units = new Dictionary<Guid, UnitState>();

        foreach (var row in rows)
        {
            var validated = ValidateRow(row, context, peopleByDocument, emailOwnerInFile, usernames, units);
            result.Add(validated);
        }

        return result;
    }

    private static ValidatedRow ValidateRow(
        ParsedRow row, Context context, Dictionary<string, PersonInFile> peopleByDocument,
        Dictionary<string, (string DocKey, int Row)> emailOwnerInFile, HashSet<string> usernames, Dictionary<Guid, UnitState> units)
    {
        var firstName = row.FirstName.Trim();
        var lastName = row.LastName.Trim();
        var fullName = $"{firstName} {lastName}".Trim();
        var email = row.Email.Trim().ToLowerInvariant();
        var docNumber = PersonRules.TrimOrNull(row.DocNumber);
        var docKey = PeopleImportCommon.NormalizeDocument(docNumber);
        var phone = PersonRules.NormalizePhone(row.Phone);

        ValidatedRow Fail(string message) => new(
            row, message, fullName, firstName, lastName, null, docNumber, docKey, email, phone, string.Empty, null, null, null, null, false, false);

        // 1) Datos de la persona
        if (firstName.Length == 0) return Fail("El nombre es obligatorio.");
        if (firstName.Length > 80) return Fail("El nombre no puede superar los 80 caracteres.");
        if (lastName.Length == 0) return Fail("El apellido es obligatorio.");
        if (lastName.Length > 100) return Fail("El apellido no puede superar los 100 caracteres.");

        var (docType, docTypeError) = PeopleImportCommon.ParseDocumentType(row.DocType);
        if (docTypeError is not null) return Fail(docTypeError);
        if (docNumber is null) return Fail("El número de documento es obligatorio.");
        if (PersonRules.ValidateDocument(docType, docNumber) is { } docError) return Fail(docError);

        if (email.Length == 0) return Fail("El correo es obligatorio.");
        if (email.Length > 160) return Fail("El correo no puede superar los 160 caracteres.");
        if (!EmailRegex().IsMatch(email)) return Fail("El correo no tiene un formato válido.");

        if (phone is not null && !PersonRules.IsValidPhone(phone))
            return Fail("El teléfono no es válido (solo números, con el prefijo del país si lo tiene, ej. +595981123456).");

        var requestedUsername = row.Username.Trim().ToLowerInvariant();
        if (requestedUsername.Length > 0)
        {
            if (requestedUsername.Length > 60) return Fail("El usuario no puede superar los 60 caracteres.");
            if (!UsernameRegex().IsMatch(requestedUsername)) return Fail("Usuario inválido: solo minúsculas, números, puntos y guiones.");
        }

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

        var (percentage, percentageError) = PeopleImportCommon.ParsePercentage(row.Percentage);
        if (percentageError is not null) return Fail(percentageError);
        var (explicitPrimary, primaryError) = PeopleImportCommon.ParseYesNo(row.Primary, "Principal");
        if (primaryError is not null) return Fail(primaryError);
        if (unit is null && (percentage is not null || explicitPrimary is not null))
            return Fail("El % de titularidad y Principal solo se usan junto con edificio y unidad.");

        // 3) Quien es la persona: la del archivo (mismo documento), una que ya existe o una nueva
        ExistingPerson? existing;
        var username = string.Empty;
        string? personError = null;

        if (peopleByDocument.TryGetValue(docKey, out var inFile))
        {
            if (!string.Equals(inFile.Email, email, StringComparison.Ordinal))
                return Fail($"El documento ya aparece en la fila {inFile.FirstRow} con otro correo: una persona lleva un solo correo.");
            existing = inFile.Existing;
            username = inFile.Username;
        }
        else
        {
            if (emailOwnerInFile.TryGetValue(email, out var emailUse))
                return Fail($"El correo ya aparece en la fila {emailUse.Row} con otro documento.");

            if (context.OwnersByDocument.TryGetValue(docKey, out existing))
            {
                // Mismo documento que un propietario ya cargado: es la misma persona, su correo guardado manda.
                if (context.UsersByEmail.TryGetValue(email, out var emailUser) && emailUser.Id != existing.Id)
                    personError = $"El correo ya lo usa otra persona ({emailUser.FullName}).";
            }
            else if (context.UsersByEmail.TryGetValue(email, out var byEmail))
            {
                if (byEmail.Role is not (UserRole.Owner or UserRole.Resident))
                    personError = $"El correo ya lo usa una cuenta del sistema que no es de propietario ni de residente ({byEmail.FullName}).";
                else if (!string.IsNullOrEmpty(byEmail.DocumentNumber)
                         && PeopleImportCommon.NormalizeDocument(byEmail.DocumentNumber) != docKey)
                    personError = $"El correo ya pertenece a {byEmail.FullName}, que figura con otro documento.";
                else
                    existing = byEmail;
            }
            else
            {
                (username, personError) = ResolveUsername(requestedUsername, email, usernames);
            }
        }

        if (personError is not null) return Fail(personError);

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
                if (context.UnitOwners.TryGetValue(unit.Id, out var current))
                    state.Owners.AddRange(current.Select(o => (o.OwnerId.ToString(), o.Percentage, o.IsPrimary, (int?)null)));
            }

            var already = state.Owners.FirstOrDefault(o => o.PersonKey == personKey);
            if (already.PersonKey is not null)
            {
                if (already.FileRow is { } earlier)
                    return Fail($"Esta persona ya está asignada a esa unidad en la fila {earlier}.");
                alreadyAssigned = true;   // ya era propietaria en el sistema: no se toca
            }
            else
            {
                if (state.Owners.Count >= MaxOwnersPerUnit)
                    return Fail($"La unidad {unit.Code} ya tiene {MaxOwnersPerUnit} propietarios: es el máximo por unidad.");

                var total = state.Owners.Sum(o => o.Percentage ?? 0m) + (percentage ?? 0m);
                if (total > 100m)
                    return Fail($"Los porcentajes de titularidad de la unidad {unit.Code} no pueden superar 100% (con esta fila suman {total:0.##}%).");

                var hasPrimary = state.Owners.Any(o => o.IsPrimary);
                if (explicitPrimary == true && hasPrimary)
                    return Fail($"La unidad {unit.Code} ya tiene un propietario principal.");
                isPrimary = explicitPrimary ?? !hasPrimary;
            }
        }

        // 5) Todo bien: recien ahora se registra en el estado compartido (una fila con error no cuenta para las demas)
        if (!peopleByDocument.ContainsKey(docKey))
        {
            peopleByDocument[docKey] = new PersonInFile(email, username, existing, row.RowNumber);
            emailOwnerInFile[email] = (docKey, row.RowNumber);
            if (existing is null) usernames.Add(username);
        }

        if (unit is not null && state is not null)
        {
            if (!alreadyAssigned) state.Owners.Add((personKey, percentage, isPrimary, row.RowNumber));
            units[unit.Id] = state;
        }

        return new ValidatedRow(
            row, null, fullName, firstName, lastName, docType, docNumber, docKey, existing?.Email ?? email, phone, username, existing,
            unit, building, percentage, isPrimary, alreadyAssigned);
    }

    // Usuario pedido (debe estar libre) o armado con la parte del correo antes de la @ (si esta ocupado se agrega un numero).
    private static (string Username, string? Error) ResolveUsername(string requested, string email, ISet<string> taken)
    {
        if (requested.Length > 0)
            return taken.Contains(requested) ? (requested, $"El usuario \"{requested}\" ya existe en la empresa o en el archivo.") : (requested, null);

        var local = email[..email.IndexOf('@')];
        var baseName = new string(local.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '.').ToArray()).Trim('.', '-', '_');
        if (baseName.Length == 0 || !char.IsAsciiLetterOrDigit(baseName[0])) baseName = "usuario" + baseName;
        if (baseName.Length > 50) baseName = baseName[..50];

        var candidate = baseName;
        for (var n = 2; taken.Contains(candidate); n++) candidate = $"{baseName}{n}";
        return (candidate, null);
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.\-_]*$")]
    private static partial Regex UsernameRegex();
}
