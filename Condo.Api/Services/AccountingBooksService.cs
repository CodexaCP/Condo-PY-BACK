using System.Globalization;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Datos que salen en el encabezado del Libro Diario: el titular ya resuelto y el contador.</summary>
public sealed record AccountingBookHeader(
    AccountingBookHolder Holder, string? LegalName, string? Ruc, string? Address, string? AccountantName, string? AccountantRegistration);

/// <summary>
/// Configuracion de los libros contables del edificio (Centro de configuracion, seccion «Libros contables»): quien es el titular
/// (consorcio o empresa administradora), sus datos para el encabezado del Libro Diario, el contador y la rubrica. El Diario es uno por
/// edificio aunque el titular sea la administradora. Es solo informacion: no mueve saldos ni cambia el libro.
/// </summary>
public class AccountingBooksService(ICondoDbContext dbContext, ITenantContext tenantContext)
{
    public const int MaxLegalName = 200;
    public const int MaxRuc = 20;
    public const int MaxAddress = 300;
    public const int MaxAccountantName = 200;
    public const int MaxAccountantRegistration = 30;
    public const int MaxBookNumber = 30;
    public const int MaxFolios = 100_000;

    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    // ─── Lectura ──────────────────────────────────────────────────────────────

    public async Task<AccountingBooksDto> GetAsync(Building building, bool canEdit, CancellationToken ct)
    {
        var settings = await dbContext.AccountingBookSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == building.Id, ct);
        var companyName = await CompanyNameAsync(building, ct);
        return ToDto(building, settings, companyName, canEdit);
    }

    /// <summary>Encabezado del Libro Diario del edificio (para los libros que se imprimen o exportan).</summary>
    public async Task<AccountingBookHeader> HeaderAsync(Building building, CancellationToken ct)
    {
        var settings = await dbContext.AccountingBookSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == building.Id, ct);
        return Resolve(building, settings, await CompanyNameAsync(building, ct));
    }

    private async Task<string?> CompanyNameAsync(Building building, CancellationToken ct)
    {
        var companyId = building.CompanyId ?? building.Condominium?.CompanyId;
        if (!companyId.HasValue)
        {
            companyId = await dbContext.Condominiums.AsNoTracking()
                .Where(x => x.Id == building.CondominiumId)
                .Select(x => (Guid?)x.CompanyId)
                .FirstOrDefaultAsync(ct);
        }

        return companyId.HasValue
            ? await dbContext.Companies.AsNoTracking().Where(x => x.Id == companyId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;
    }

    /// <summary>El titular ya resuelto: el dato cargado o, si falta, el de la ficha del edificio (consorcio) o el nombre de la empresa.</summary>
    public static AccountingBookHeader Resolve(Building building, AccountingBookSettings? settings, string? companyName)
    {
        var holder = settings?.Holder ?? AccountingBookHolder.Consortium;
        var defaults = DefaultsOf(holder, building, companyName);
        return new AccountingBookHeader(
            holder,
            Pick(settings?.HolderLegalName, defaults.LegalName),
            Pick(settings?.HolderRuc, defaults.Ruc),
            Pick(settings?.HolderAddress, defaults.Address),
            settings?.AccountantName,
            settings?.AccountantRegistration);
    }

    private static AccountingBookDefaultsDto DefaultsOf(AccountingBookHolder holder, Building building, string? companyName) =>
        holder == AccountingBookHolder.AdminCompany
            ? new AccountingBookDefaultsDto { LegalName = Clean(companyName) }
            : new AccountingBookDefaultsDto
            {
                LegalName = Clean(building.LegalName) ?? Clean(building.Name),
                Ruc = Clean(building.Ruc),
                Address = Clean(building.FiscalAddress) ?? Clean(building.Address)
            };

    private static AccountingBooksDto ToDto(Building building, AccountingBookSettings? s, string? companyName, bool canEdit)
    {
        var header = Resolve(building, s, companyName);
        return new AccountingBooksDto
        {
            BuildingId = building.Id,
            CanEdit = canEdit,
            Configured = s is not null,
            Holder = s?.Holder ?? AccountingBookHolder.Consortium,
            HolderLegalName = s?.HolderLegalName,
            HolderRuc = s?.HolderRuc,
            HolderAddress = s?.HolderAddress,
            AccountantName = s?.AccountantName,
            AccountantRegistration = s?.AccountantRegistration,
            BookNumber = s?.BookNumber,
            RubricDate = s?.RubricDate,
            AuthorizedFolios = s?.AuthorizedFolios,
            UsedFolios = s?.UsedFolios,
            ConsortiumDefaults = DefaultsOf(AccountingBookHolder.Consortium, building, companyName),
            AdminCompanyDefaults = DefaultsOf(AccountingBookHolder.AdminCompany, building, companyName),
            Effective = new AccountingBookDefaultsDto { LegalName = header.LegalName, Ruc = header.Ruc, Address = header.Address }
        };
    }

    // ─── Escritura ────────────────────────────────────────────────────────────

    /// <summary>Valida el pedido. El RUC solo se controla si lo cambian: no se bloquea guardar otros datos por un RUC viejo ya cargado.</summary>
    public static string? Validate(UpdateAccountingBooksRequest r, string? currentRuc)
    {
        if (!Enum.IsDefined(r.Holder)) return "El titular del libro no es válido.";

        var error =
            Max(r.HolderLegalName, MaxLegalName, "La razón social del titular") ??
            Max(r.HolderRuc, MaxRuc, "El RUC del titular") ??
            Max(r.HolderAddress, MaxAddress, "El domicilio del titular") ??
            Max(r.AccountantName, MaxAccountantName, "El nombre del contador") ??
            Max(r.AccountantRegistration, MaxAccountantRegistration, "La matrícula del contador") ??
            Max(r.BookNumber, MaxBookNumber, "El número de libro");
        if (error is not null) return error;

        var ruc = Clean(r.HolderRuc);
        if (ruc is not null && !string.Equals(ruc, Clean(currentRuc), StringComparison.Ordinal) && !BuildingProfile.IsValidRuc(ruc))
        {
            return "El RUC del titular no es válido: usá el formato 80012345-0 con su dígito verificador correcto.";
        }

        if (r.RubricDate is { } date && (date < new DateOnly(1983, 1, 1) || date > FinancePeriods.Today()))
        {
            return "La fecha de rúbrica no es válida (no puede ser anterior a 1983 ni futura).";
        }

        if (r.AuthorizedFolios is < 0 or > MaxFolios) return $"Los folios autorizados deben estar entre 0 y {MaxFolios:N0}.";
        if (r.UsedFolios is < 0 or > MaxFolios) return $"Los folios usados deben estar entre 0 y {MaxFolios:N0}.";
        if (r.AuthorizedFolios.HasValue && r.UsedFolios.HasValue && r.UsedFolios > r.AuthorizedFolios)
        {
            return "Los folios usados no pueden superar a los autorizados.";
        }

        return null;
    }

    public async Task<(AccountingBooksDto? Books, string? Error)> UpdateAsync(
        Building building, UpdateAccountingBooksRequest request, Guid companyId, CancellationToken ct)
    {
        var settings = await dbContext.AccountingBookSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == building.Id, ct);
        var error = Validate(request, settings?.HolderRuc);
        if (error is not null) return (null, error);

        var creating = settings is null;
        settings ??= new AccountingBookSettings { CompanyId = companyId, BuildingId = building.Id };

        var changes = new List<ConfigChange>();
        void Track(string field, string label, string? before, string? after)
        {
            if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
            {
                changes.Add(new ConfigChange(field, label, before, after));
            }
        }

        var holder = request.Holder;
        var legalName = Clean(request.HolderLegalName);
        var ruc = Clean(request.HolderRuc);
        var address = Clean(request.HolderAddress);
        var accountant = Clean(request.AccountantName);
        var registration = Clean(request.AccountantRegistration);
        var bookNumber = Clean(request.BookNumber);

        Track("holder", "Titular del libro", creating ? null : HolderLabel(settings.Holder), HolderLabel(holder));
        Track("holderLegalName", "Razón social del titular", settings.HolderLegalName, legalName);
        Track("holderRuc", "RUC del titular", settings.HolderRuc, ruc);
        Track("holderAddress", "Domicilio del titular", settings.HolderAddress, address);
        Track("accountantName", "Contador", settings.AccountantName, accountant);
        Track("accountantRegistration", "Matrícula del contador", settings.AccountantRegistration, registration);
        Track("bookNumber", "Número de libro", settings.BookNumber, bookNumber);
        Track("rubricDate", "Fecha de rúbrica", DateLabel(settings.RubricDate), DateLabel(request.RubricDate));
        Track("authorizedFolios", "Folios autorizados", IntLabel(settings.AuthorizedFolios), IntLabel(request.AuthorizedFolios));
        Track("usedFolios", "Folios usados", IntLabel(settings.UsedFolios), IntLabel(request.UsedFolios));

        if (changes.Count == 0 && !creating) return (await GetAsync(building, canEdit: true, ct), null);

        settings.Holder = holder;
        settings.HolderLegalName = legalName;
        settings.HolderRuc = ruc;
        settings.HolderAddress = address;
        settings.AccountantName = accountant;
        settings.AccountantRegistration = registration;
        settings.BookNumber = bookNumber;
        settings.RubricDate = request.RubricDate;
        settings.AuthorizedFolios = request.AuthorizedFolios;
        settings.UsedFolios = request.UsedFolios;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        if (creating) dbContext.AccountingBookSettings.Add(settings);

        Audit.Add(companyId, building.Id, ConfigSectionKeys.Books, creating ? "Created" : "Updated",
            creating
                ? $"Libros contables: se configuró el titular ({HolderLabel(holder)})."
                : $"Libros contables: cambió {string.Join(", ", changes.Select(x => x.Label.ToLowerInvariant()))}.",
            "AccountingBookSettings", settings.Id, changes);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos guardados a la vez sobre un edificio sin configuracion: el segundo choca con el indice unico.
            return (null, "Otro cambio se hizo al mismo tiempo. Actualizá la pantalla y reintentá.");
        }

        return (await GetAsync(building, canEdit: true, ct), null);
    }

    // ─── Auxiliares ───────────────────────────────────────────────────────────

    public static string HolderLabel(AccountingBookHolder holder) =>
        holder == AccountingBookHolder.AdminCompany ? "Empresa administradora" : "Consorcio";

    private static string? DateLabel(DateOnly? value) => value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string? IntLabel(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Pick(string? value, string? fallback) => Clean(value) ?? fallback;

    private static string? Max(string? value, int max, string label) =>
        value is not null && value.Trim().Length > max ? $"{label} no puede superar los {max} caracteres." : null;
}
