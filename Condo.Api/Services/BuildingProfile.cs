using System.Text.RegularExpressions;
using Condo.Application.Models;
using Condo.Domain.Entities;

namespace Condo.Api.Services;

// Reglas de la ficha de registro del edificio (datos generales, legales, fiscales, de cobro y de configuracion):
// validacion, normalizacion y copia entre el formulario y la entidad. Todo es opcional; lo que se carga tiene que ser valido.
public static partial class BuildingProfile
{
    public const int MaxBankAccounts = 10;

    public static string? Validate(BuildingProfileData p, IReadOnlyList<BuildingBankAccountDto>? bankAccounts, string? currentRuc, bool canEditFinancial)
    {
        string? error =
            Max(p.Department, 100, "El departamento") ??
            Max(p.City, 100, "La ciudad") ??
            Max(p.Neighborhood, 100, "El barrio") ??
            Max(p.LocationReference, 300, "La referencia de ubicación") ??
            Max(p.LogoUrl, 500, "El logo") ??
            Max(p.OfficeHours, 200, "El horario de administración") ??
            Max(p.FincaNumber, 50, "La finca") ??
            Max(p.PadronNumber, 50, "El padrón") ??
            Max(p.CadastralAccount, 50, "La cuenta corriente catastral") ??
            Max(p.LegalEntityNumber, 50, "La personería jurídica") ??
            Max(p.BylawsUrl, 500, "El reglamento") ??
            Max(p.BylawsFileName, 200, "El nombre del reglamento") ??
            Max(p.AdministratorName, 200, "El administrador responsable") ??
            Max(p.EmergencyContactName, 200, "El contacto de emergencia") ??
            Max(p.TimeZoneId, 60, "La zona horaria");
        if (error is not null) return error;

        if (p.YearBuilt is { } year && (year < 1800 || year > DateTime.UtcNow.Year + 5))
            return "El año de construcción no es válido.";
        if (p.TowersCount is < 0 or > 500) return "La cantidad de torres debe estar entre 0 y 500.";
        if (p.FloorsCount is < 0 or > 500) return "La cantidad de pisos debe estar entre 0 y 500.";
        if (p.UnitsCount is < 0 or > 100000) return "La cantidad de unidades debe estar entre 0 y 100.000.";

        if (p.Latitude.HasValue != p.Longitude.HasValue)
            return "Cargá la latitud y la longitud juntas, o dejá las dos vacías.";
        if (p.Latitude is < -90m or > 90m) return "La latitud debe estar entre -90 y 90.";
        if (p.Longitude is < -180m or > 180m) return "La longitud debe estar entre -180 y 180.";

        if (p.LegalEntityDate is { } date && date < new DateOnly(1900, 1, 1))
            return "La fecha de la personería jurídica no es válida.";

        foreach (var (value, label) in new[]
                 {
                     (p.WhatsAppPhone, "El WhatsApp"),
                     (p.AdministratorPhone, "El teléfono del administrador"),
                     (p.EmergencyContactPhone, "El teléfono de emergencia")
                 })
        {
            if (!string.IsNullOrWhiteSpace(value) && !PhoneRegex().IsMatch(NormalizePhone(value)!))
                return $"{label} no es válido (con el prefijo del país, ej. +595981123456).";
        }

        if (!canEditFinancial) return null;

        error =
            Max(p.LegalName, 200, "La razón social") ??
            Max(p.EconomicActivity, 200, "La actividad económica") ??
            Max(p.FiscalAddress, 300, "La dirección fiscal") ??
            Max(p.InvoiceEmail, 160, "El email para comprobantes") ??
            Max(p.PaymentInstructions, 1000, "Las instrucciones de pago");
        if (error is not null) return error;

        // El RUC se valida solo si lo cambian: no se bloquea guardar otros datos por un RUC viejo ya cargado.
        var ruc = TrimOrNull(p.Ruc);
        if (ruc is not null && !string.Equals(ruc, TrimOrNull(currentRuc), StringComparison.Ordinal) && !IsValidRuc(ruc))
            return "El RUC no es válido: usá el formato 80012345-0 con su dígito verificador correcto.";

        var invoiceEmail = TrimOrNull(p.InvoiceEmail);
        if (invoiceEmail is not null && !EmailRegex().IsMatch(invoiceEmail))
            return "El email para comprobantes no es válido.";

        if (p.DefaultDueDay is < 1 or > 31) return "El día de vencimiento debe estar entre 1 y 31.";
        if (p.GraceDays is < 0 or > 365) return "Los días de gracia deben estar entre 0 y 365.";

        if (bankAccounts is not null)
        {
            if (bankAccounts.Count > MaxBankAccounts) return $"Se pueden cargar hasta {MaxBankAccounts} cuentas bancarias.";
            for (var i = 0; i < bankAccounts.Count; i++)
            {
                var a = bankAccounts[i];
                var n = i + 1;
                if (string.IsNullOrWhiteSpace(a.BankName)) return $"Cuenta bancaria {n}: el banco es obligatorio.";
                if (string.IsNullOrWhiteSpace(a.AccountNumber)) return $"Cuenta bancaria {n}: el número de cuenta es obligatorio.";
                if (string.IsNullOrWhiteSpace(a.HolderName)) return $"Cuenta bancaria {n}: el titular es obligatorio.";
                error = Max(a.BankName, 120, $"Cuenta bancaria {n}: el banco") ??
                        Max(a.AccountNumber, 40, $"Cuenta bancaria {n}: el número de cuenta") ??
                        Max(a.HolderName, 200, $"Cuenta bancaria {n}: el titular") ??
                        Max(a.HolderDocument, 30, $"Cuenta bancaria {n}: el documento del titular") ??
                        Max(a.Alias, 60, $"Cuenta bancaria {n}: el alias");
                if (error is not null) return error;
            }
        }

        return null;
    }

    // Formato 80012345-0 (base de 5 a 9 digitos, guion y digito verificador).
    // Digito verificador de la SET: modulo 11, pesos 2..11 de derecha a izquierda; resto > 1 => 11 - resto, si no 0.
    public static bool IsValidRuc(string ruc)
    {
        var m = RucRegex().Match(ruc.Trim());
        if (!m.Success) return false;
        return ComputeRucCheckDigit(m.Groups[1].Value) == m.Groups[2].Value[0] - '0';
    }

    public static int ComputeRucCheckDigit(string baseNumber)
    {
        var k = 2;
        var total = 0;
        for (var i = baseNumber.Length - 1; i >= 0; i--)
        {
            if (k > 11) k = 2;
            total += (baseNumber[i] - '0') * k;
            k++;
        }
        var rest = total % 11;
        return rest > 1 ? 11 - rest : 0;
    }

    // canEditFinancial = false deja intactos los datos fiscales, de cobranza y las cuentas (el encargado del edificio no los toca).
    public static void Apply(Building e, BuildingProfileData p, bool canEditFinancial)
    {
        e.PropertyType = p.PropertyType;
        e.Department = TrimOrNull(p.Department);
        e.City = TrimOrNull(p.City);
        e.Neighborhood = TrimOrNull(p.Neighborhood);
        e.LocationReference = TrimOrNull(p.LocationReference);
        e.Latitude = p.Latitude.HasValue && p.Longitude.HasValue ? decimal.Round(p.Latitude.Value, 6) : null;
        e.Longitude = p.Latitude.HasValue && p.Longitude.HasValue ? decimal.Round(p.Longitude!.Value, 6) : null;
        e.YearBuilt = p.YearBuilt;
        e.TowersCount = p.TowersCount;
        e.FloorsCount = p.FloorsCount;
        e.UnitsCount = p.UnitsCount;
        e.LogoUrl = TrimOrNull(p.LogoUrl);
        e.WhatsAppPhone = NormalizePhone(p.WhatsAppPhone);
        e.OfficeHours = TrimOrNull(p.OfficeHours);

        e.FincaNumber = TrimOrNull(p.FincaNumber);
        e.PadronNumber = TrimOrNull(p.PadronNumber);
        e.CadastralAccount = TrimOrNull(p.CadastralAccount);
        e.LegalEntityNumber = TrimOrNull(p.LegalEntityNumber);
        e.LegalEntityDate = p.LegalEntityDate;
        e.BylawsUrl = TrimOrNull(p.BylawsUrl);
        e.BylawsFileName = e.BylawsUrl is null ? null : TrimOrNull(p.BylawsFileName);
        e.AdministratorName = TrimOrNull(p.AdministratorName);
        e.AdministratorPhone = NormalizePhone(p.AdministratorPhone);
        e.EmergencyContactName = TrimOrNull(p.EmergencyContactName);
        e.EmergencyContactPhone = NormalizePhone(p.EmergencyContactPhone);

        e.TimeZoneId = TrimOrNull(p.TimeZoneId);

        if (!canEditFinancial) return;

        e.Ruc = TrimOrNull(p.Ruc);
        e.LegalName = TrimOrNull(p.LegalName);
        e.TaxpayerType = p.TaxpayerType;
        e.VatRegime = p.VatRegime;
        e.EconomicActivity = TrimOrNull(p.EconomicActivity);
        e.FiscalAddress = TrimOrNull(p.FiscalAddress);
        e.InvoiceEmail = TrimOrNull(p.InvoiceEmail)?.ToLowerInvariant();
        e.DefaultDueDay = p.DefaultDueDay;
        e.GraceDays = p.GraceDays;
        e.PaymentInstructions = TrimOrNull(p.PaymentInstructions);
    }

    // La lista enviada reemplaza a las cuentas guardadas: las que traen Id se actualizan, las nuevas se agregan y las que
    // ya no vienen se dan de baja (borrado logico). Trabaja sobre e.BankAccounts, que debe venir cargada al editar.
    // Devuelve las cuentas nuevas: quien llama las agrega por el DbSet (con Id propio, EF las tomaria por existentes si entran por la coleccion).
    public static List<BuildingBankAccount> SyncBankAccounts(Building e, Guid companyId, IReadOnlyList<BuildingBankAccountDto> items)
    {
        var current = e.BankAccounts.Where(x => !x.IsDeleted).ToList();
        var keptIds = new HashSet<Guid>();
        var added = new List<BuildingBankAccount>();

        foreach (var item in items)
        {
            var entity = item.Id.HasValue ? current.FirstOrDefault(x => x.Id == item.Id.Value) : null;
            if (entity is null)
            {
                entity = new BuildingBankAccount { CompanyId = companyId, BuildingId = e.Id };
                added.Add(entity);
            }

            entity.BankName = item.BankName.Trim();
            entity.AccountType = item.AccountType;
            entity.AccountNumber = item.AccountNumber.Trim();
            entity.HolderName = item.HolderName.Trim();
            entity.HolderDocument = TrimOrNull(item.HolderDocument);
            entity.Alias = TrimOrNull(item.Alias);
            entity.IsActive = item.IsActive;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            keptIds.Add(entity.Id);
        }

        foreach (var removed in current.Where(x => !keptIds.Contains(x.Id)))
        {
            removed.IsDeleted = true;
            removed.UpdatedAtUtc = DateTime.UtcNow;
        }

        return added;
    }

    public static void Fill(BuildingProfileData target, Building e)
    {
        target.PropertyType = e.PropertyType;
        target.Department = e.Department;
        target.City = e.City;
        target.Neighborhood = e.Neighborhood;
        target.LocationReference = e.LocationReference;
        target.Latitude = e.Latitude;
        target.Longitude = e.Longitude;
        target.YearBuilt = e.YearBuilt;
        target.TowersCount = e.TowersCount;
        target.FloorsCount = e.FloorsCount;
        target.UnitsCount = e.UnitsCount;
        target.LogoUrl = e.LogoUrl;
        target.WhatsAppPhone = e.WhatsAppPhone;
        target.OfficeHours = e.OfficeHours;
        target.FincaNumber = e.FincaNumber;
        target.PadronNumber = e.PadronNumber;
        target.CadastralAccount = e.CadastralAccount;
        target.LegalEntityNumber = e.LegalEntityNumber;
        target.LegalEntityDate = e.LegalEntityDate;
        target.BylawsUrl = e.BylawsUrl;
        target.BylawsFileName = e.BylawsFileName;
        target.AdministratorName = e.AdministratorName;
        target.AdministratorPhone = e.AdministratorPhone;
        target.EmergencyContactName = e.EmergencyContactName;
        target.EmergencyContactPhone = e.EmergencyContactPhone;
        target.Ruc = e.Ruc;
        target.LegalName = e.LegalName;
        target.TaxpayerType = e.TaxpayerType;
        target.VatRegime = e.VatRegime;
        target.EconomicActivity = e.EconomicActivity;
        target.FiscalAddress = e.FiscalAddress;
        target.InvoiceEmail = e.InvoiceEmail;
        target.DefaultDueDay = e.DefaultDueDay;
        target.GraceDays = e.GraceDays;
        target.PaymentInstructions = e.PaymentInstructions;
        target.TimeZoneId = e.TimeZoneId;
    }

    public static BuildingBankAccountDto ToDto(BuildingBankAccount a) => new()
    {
        Id = a.Id,
        BankName = a.BankName,
        AccountType = a.AccountType,
        AccountNumber = a.AccountNumber,
        HolderName = a.HolderName,
        HolderDocument = a.HolderDocument,
        Alias = a.Alias,
        IsActive = a.IsActive
    };

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return new string(value.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')')).ToArray());
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Max(string? value, int max, string label) =>
        value is not null && value.Trim().Length > max ? $"{label} no puede superar los {max} caracteres." : null;

    [GeneratedRegex(@"^(\d{5,9})-(\d)$")]
    private static partial Regex RucRegex();

    [GeneratedRegex(@"^\+?\d{6,20}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();
}
