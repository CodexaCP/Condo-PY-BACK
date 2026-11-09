using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Condo.Api.Documents;
using Condo.Application.Abstractions;
using Condo.Application.Localization;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Resultado publico de escanear el QR de un documento: solo datos que ya estan impresos en el papel de la cartelera.</summary>
public sealed class DocumentVerificationResult
{
    // valid | draft | superseded | revoked | unavailable | notfound
    public string Status { get; set; } = "notfound";
    public string? DocumentType { get; set; }
    public string? Title { get; set; }
    public string? BuildingName { get; set; }
    public string? AdministratorName { get; set; }
    public string? PeriodName { get; set; }
    public string? TotalFormatted { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? ReplacedAtUtc { get; set; }

    // Botones de la pagina publica. Solo se completan en un documento vigente y solo con datos que ya son publicos del edificio.
    // Vencimiento del periodo (yyyy-MM-dd), para el recordatorio de calendario.
    public string? DueDate { get; set; }
    // Enlace de WhatsApp con el mensaje armado (el telefono del edificio, si lo tiene cargado).
    public string? WhatsAppUrl { get; set; }
    // A quien llega ese WhatsApp: manager = el encargado del edificio; administration = el telefono del edificio o del administrador.
    public string? WhatsAppContact { get; set; }
    public string? AppDownloadUrl { get; set; }
    // Cuentas para transferir: solo si el edificio activo el interruptor "mostrar en el QR" (apagado por defecto).
    public List<DocumentVerificationBankAccount> BankAccounts { get; set; } = new();
}

public sealed class DocumentVerificationBankAccount
{
    public string BankName { get; set; } = string.Empty;
    // Checking | Savings
    public string AccountType { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public string? HolderDocument { get; set; }
    public string? Alias { get; set; }
}

/// <summary>
/// QR de verificacion de documentos (hoy la liquidacion de expensas). Emite el token al generar el PDF —de forma idempotente, tambien para las
/// liquidaciones que ya existian— y resuelve el token cuando alguien escanea el QR. El token es aleatorio (128 bits): la direccion no
/// se puede adivinar ni recorrer. La pagina publica solo ve un resumen minimo; nunca unidades, montos individuales ni datos personales.
/// </summary>
public partial class DocumentVerificationService(ICondoDbContext dbContext, IConfiguration configuration)
{
    public const string PublicPath = "/v/";

    [GeneratedRegex("^[A-Za-z0-9_-]{16,32}$")]
    private static partial Regex TokenRegex();

    public string UrlFor(string token) =>
        (configuration["Frontend:BaseUrl"] ?? "https://expenses.com.py").TrimEnd('/') + PublicPath + token;

    /// <summary>
    /// Devuelve la direccion de verificacion de la liquidacion que se esta imprimiendo. Si el contenido es el mismo que el de la ultima emision se
    /// reutiliza el mismo token (el papel viejo y el nuevo valen igual); si cambio, la emision anterior queda reemplazada y se crea una nueva.
    /// El resumen ya debe traer las lineas de gasto y los porcentajes cargados (lo que usa el PDF).
    /// </summary>
    public async Task<string> IssueSettlementAsync(
        ExpensePeriod period, ExpenseSettlementSummaryDto summary, string? administratorName, CancellationToken ct)
    {
        var published = period.Status == ExpensePeriodStatus.Published;
        var total = SettlementPdfDocument.PrintedGrandTotal(summary);
        var hash = ComputeHash(period, summary, total, published);

        var active = await dbContext.DocumentVerifications
            .Where(x => !x.IsDeleted && x.CompanyId == period.CompanyId
                        && x.DocumentType == DocumentVerification.TypeSettlement && x.DocumentId == period.Id
                        && x.SupersededAtUtc == null && x.RevokedAtUtc == null)
            .ToListAsync(ct);

        var current = active.FirstOrDefault(x => x.ContentHash == hash);
        if (current is not null) return UrlFor(current.Token);

        var now = DateTime.UtcNow;
        foreach (var old in active)
        {
            old.SupersededAtUtc = now;
            old.UpdatedAtUtc = now;
        }

        var companyName = await dbContext.Companies.AsNoTracking()
            .Where(x => x.Id == period.CompanyId).Select(x => x.Name).FirstOrDefaultAsync(ct);

        var entity = new DocumentVerification
        {
            CompanyId = period.CompanyId,
            Token = NewToken(),
            DocumentType = DocumentVerification.TypeSettlement,
            DocumentId = period.Id,
            BuildingId = period.BuildingId,
            ContentHash = hash,
            Title = "Liquidación de expensas",
            BuildingName = Trim(summary.BuildingName, 200),
            AdministratorName = Trim(string.IsNullOrWhiteSpace(administratorName) ? companyName : administratorName, 200),
            PeriodName = Trim(summary.ExpensePeriodName, 100),
            TotalAmount = total,
            IsPublished = published,
            IssuedAtUtc = now
        };
        dbContext.DocumentVerifications.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        return UrlFor(entity.Token);
    }

    /// <summary>Resuelve el token del QR. Un token mal formado o inexistente da el mismo resultado (no se puede distinguir ni recorrer).</summary>
    public async Task<DocumentVerificationResult> LookupAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !TokenRegex().IsMatch(token))
            return new DocumentVerificationResult { Status = "notfound" };

        var row = await dbContext.DocumentVerifications.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Token == token, ct);
        if (row is null) return new DocumentVerificationResult { Status = "notfound" };

        var result = new DocumentVerificationResult
        {
            DocumentType = row.DocumentType,
            Title = row.Title,
            BuildingName = row.BuildingName,
            AdministratorName = row.AdministratorName,
            PeriodName = row.PeriodName,
            IssuedAtUtc = row.IssuedAtUtc
        };

        using (await MoneyScope.ForCompanyAsync(dbContext, row.CompanyId, ct))
            result.TotalFormatted = Money.Format(row.TotalAmount);

        if (row.RevokedAtUtc.HasValue)
        {
            result.Status = "revoked";
            result.ReplacedAtUtc = row.RevokedAtUtc;
            return result;
        }

        if (row.SupersededAtUtc.HasValue)
        {
            result.Status = "superseded";
            result.ReplacedAtUtc = row.SupersededAtUtc;
            return result;
        }

        // El documento ya no existe (periodo eliminado): el papel quedo huerfano.
        var exists = await dbContext.ExpensePeriods.AsNoTracking()
            .AnyAsync(x => x.Id == row.DocumentId && x.CompanyId == row.CompanyId && !x.IsDeleted, ct);
        if (!exists)
        {
            result.Status = "unavailable";
            return result;
        }

        result.Status = row.IsPublished ? "valid" : "draft";
        if (result.Status == "valid") await FillActionsAsync(result, row, ct);
        return result;
    }

    // Datos de los botones (WhatsApp, vencimiento, app, transferencia). Se leen en vivo del edificio: si cambia el telefono o la cuenta, el QR
    // impreso muestra lo nuevo. Nunca incluye datos de unidades ni de personas.
    private async Task FillActionsAsync(DocumentVerificationResult result, DocumentVerification row, CancellationToken ct)
    {
        var due = await dbContext.ExpensePeriods.AsNoTracking()
            .Where(x => x.Id == row.DocumentId && x.CompanyId == row.CompanyId)
            .Select(x => (DateOnly?)x.DueDate).FirstOrDefaultAsync(ct);
        result.DueDate = due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var building = await dbContext.Buildings.AsNoTracking()
            .Where(x => x.Id == row.BuildingId && x.CompanyId == row.CompanyId && !x.IsDeleted)
            .Select(x => new { x.WhatsAppPhone, x.AdministratorPhone, x.QrShowBankAccounts })
            .FirstOrDefaultAsync(ct);

        var apk = configuration["App:ApkUrl"];
        result.AppDownloadUrl = string.IsNullOrWhiteSpace(apk) ? null : apk;
        if (building is null) return;

        // El contacto es el encargado del edificio (el usuario BuildingManager con acceso activo a este edificio); si no tiene telefono o no hay
        // encargado, el WhatsApp del edificio y, por ultimo, el telefono del administrador.
        var managers = await dbContext.UserBuildingAccesses.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.BuildingId == row.BuildingId && x.CompanyId == row.CompanyId
                        && x.ApplicationUser != null && !x.ApplicationUser.IsDeleted && x.ApplicationUser.IsActive
                        && x.ApplicationUser.Role == UserRole.BuildingManager)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new { x.ApplicationUser!.WhatsAppPhone, x.ApplicationUser.PhonePrefix, x.ApplicationUser.Phone })
            .ToListAsync(ct);
        var managerDigits = managers
            .Select(m => PhoneDigits(m.WhatsAppPhone) ?? PhoneDigits(m.PhonePrefix, m.Phone))
            .FirstOrDefault(d => d is not null);

        var digits = managerDigits ?? PhoneDigits(building.WhatsAppPhone) ?? PhoneDigits(building.AdministratorPhone);
        if (digits is not null)
        {
            var text = $"Hola, tengo una consulta sobre la liquidación de expensas de {row.PeriodName} del edificio {row.BuildingName}.";
            result.WhatsAppUrl = $"https://wa.me/{digits}?text={Uri.EscapeDataString(text)}";
            result.WhatsAppContact = managerDigits is not null ? "manager" : "administration";
        }

        if (building.QrShowBankAccounts)
        {
            result.BankAccounts = await dbContext.BuildingBankAccounts.AsNoTracking()
                .Where(x => x.BuildingId == row.BuildingId && x.CompanyId == row.CompanyId && !x.IsDeleted && x.IsActive)
                .OrderBy(x => x.BankName)
                .Select(x => new DocumentVerificationBankAccount
                {
                    BankName = x.BankName,
                    AccountType = x.AccountType.ToString(),
                    AccountNumber = x.AccountNumber,
                    HolderName = x.HolderName,
                    HolderDocument = x.HolderDocument,
                    Alias = x.Alias
                })
                .ToListAsync(ct);
        }
    }

    // Telefono guardado en dos partes (prefijo del pais y numero local): se une y se saca el 0 inicial del numero local. Sin prefijo no se
    // puede saber el pais, asi que no se usa.
    private static string? PhoneDigits(string? prefix, string? phone)
    {
        var prefixDigits = new string((prefix ?? string.Empty).Where(char.IsDigit).ToArray());
        var local = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray()).TrimStart('0');
        return prefixDigits.Length == 0 || local.Length == 0 ? null : PhoneDigits(prefixDigits + local);
    }

    // Los telefonos del edificio se guardan completos con el prefijo del pais (+595981123456): wa.me los quiere solo con digitos.
    private static string? PhoneDigits(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length is >= 8 and <= 15 ? digits : null;
    }

    // Contenido que identifica una emision: si cambia cualquiera de estos datos, el papel anterior deja de ser la version vigente.
    private static string ComputeHash(ExpensePeriod period, ExpenseSettlementSummaryDto summary, decimal total, bool published)
    {
        var inv = CultureInfo.InvariantCulture;
        var text = string.Join("|",
            period.Id, period.BuildingId,
            summary.TotalBuildingExpenses.ToString("F2", inv), summary.TotalBuildingIncomes.ToString("F2", inv),
            summary.ReserveFundAmount.ToString("F2", inv), summary.ExtraordinaryAmount.ToString("F2", inv),
            summary.NetCommonAmount.ToString("F2", inv), total.ToString("F2", inv),
            summary.GeneratedAtUtc?.ToString("O", inv) ?? "-", published ? "P" : "D");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    // 128 bits aleatorios en base64url sin relleno: 22 caracteres.
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Trim(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..max];
    }
}
