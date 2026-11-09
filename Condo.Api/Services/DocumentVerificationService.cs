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
        return result;
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
