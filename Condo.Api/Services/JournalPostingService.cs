using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>
/// Libro Diario: asienta los asientos sugeridos en un numero correlativo por edificio y ejercicio, sin huecos, y los deja guardados sin
/// posibilidad de editarlos (Ley 1034/83, arts. 79 y 80). Se asienta por periodo: todo lo posterior a «asentado hasta» y hasta el dia elegido,
/// en orden cronologico. Nunca se asienta un asiento incompleto. El numero se toma y se guarda en la misma transaccion que los asientos, y los
/// indices unicos (numero y origen del movimiento) frenan a dos personas que asientan a la vez: una sola gana y la otra recibe un conflicto.
/// </summary>
public class JournalPostingService(ICondoDbContext dbContext, AccountingEntriesService accounting, ITenantContext tenantContext)
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    /// <summary>Numero para mostrar y citar: ejercicio y correlativo de seis cifras (2026-000012).</summary>
    public static string LabelOf(int fiscalYear, int number) =>
        $"{fiscalYear.ToString(CultureInfo.InvariantCulture)}-{number.ToString("000000", CultureInfo.InvariantCulture)}";

    // ─── Validacion del dia hasta el que se asienta ───────────────────────────

    public static string? ValidateThrough(DateOnly? postedThrough, DateOnly through, DateOnly startDate, DateOnly today)
    {
        if (through > today) return "No se puede asentar hasta una fecha futura.";
        if (through < startDate) return "La fecha es anterior al arranque de las finanzas del edificio.";
        if (postedThrough.HasValue && through <= postedThrough.Value)
        {
            return $"Ya está asentado hasta el {postedThrough.Value:dd/MM/yyyy}: elegí una fecha posterior.";
        }

        return null;
    }

    private static DateOnly StartOf(DateOnly? postedThrough, DateOnly startDate) =>
        postedThrough.HasValue ? postedThrough.Value.AddDays(1) : startDate;

    // ─── Vista previa ─────────────────────────────────────────────────────────

    public async Task<(JournalPreviewDto? Preview, string? Error)> PreviewAsync(LedgerContext ctx, DateOnly through, CancellationToken ct)
    {
        var postedThrough = await PostedThroughAsync(ctx.BuildingId, ct);
        var error = ValidateThrough(postedThrough, through, ctx.StartDate, FinancePeriods.Today());
        if (error is not null) return (null, error);

        var start = StartOf(postedThrough, ctx.StartDate);
        var built = await accounting.BuildAsync(ctx, start, through, ct);
        var lastNumbers = await LastNumbersAsync(ctx.BuildingId, ct);
        var plan = Allocate(built.Entries, ctx.FiscalYearStartMonth, lastNumbers);
        var incomplete = built.Entries.Count(e => !e.IsComplete);

        return (new JournalPreviewDto
        {
            BuildingId = ctx.BuildingId,
            PostedThrough = postedThrough,
            From = start,
            Through = through,
            EntryCount = built.Entries.Count,
            IncompleteCount = incomplete,
            Pending = built.Pending,
            FirstLabel = plan.Count > 0 ? LabelOf(plan[0].FiscalYear, plan[0].Number) : null,
            LastLabel = plan.Count > 0 ? LabelOf(plan[^1].FiscalYear, plan[^1].Number) : null,
            TotalDebit = built.Entries.Sum(e => e.Debit),
            TotalCredit = built.Entries.Sum(e => e.Credit),
            CanPost = built.Entries.Count > 0 && incomplete == 0
        }, null);
    }

    // ─── Asentar ──────────────────────────────────────────────────────────────

    /// <summary>Asienta todo lo posterior a lo ya asentado y hasta <paramref name="through"/>. El error es <c>Conflict</c> si hay que corregir algo y reintentar.</summary>
    public async Task<(PostJournalResultDto? Result, string? Error, bool Conflict)> PostAsync(
        LedgerContext ctx, DateOnly through, Guid companyId, CancellationToken ct)
    {
        var settings = await dbContext.FinanceSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId, ct);
        if (settings is null) return (null, "Completá la configuración inicial de Finanzas del edificio.", true);

        var error = ValidateThrough(settings.JournalPostedThrough, through, ctx.StartDate, FinancePeriods.Today());
        if (error is not null) return (null, error, false);

        var start = StartOf(settings.JournalPostedThrough, ctx.StartDate);
        var built = await accounting.BuildAsync(ctx, start, through, ct);
        return await PostBuiltAsync(ctx, settings, built.Entries, through, companyId, ct);
    }

    /// <summary>Guarda en el Libro Diario los asientos ya armados (el armado y la guarda estan separados para poder probar cada uno).</summary>
    public async Task<(PostJournalResultDto? Result, string? Error, bool Conflict)> PostBuiltAsync(
        LedgerContext ctx, FinanceSettings settings, IReadOnlyList<AccountingEntryDto> entries, DateOnly through, Guid companyId, CancellationToken ct)
    {
        var incomplete = entries.Where(e => !e.IsComplete).ToList();
        if (incomplete.Count > 0)
        {
            var pending = string.Join(", ", entries.SelectMany(e => e.Lines).Where(l => l.Missing).Select(l => l.Name).Distinct());
            return (null, $"Hay {incomplete.Count} asientos incompletos porque falta la cuenta del plan de: {pending}. Asignalas en «Cuentas del plan» y volvé a asentar.", true);
        }

        var unbalanced = entries.FirstOrDefault(e => e.Debit != e.Credit);
        if (unbalanced is not null)
        {
            return (null, $"El asiento del {unbalanced.Date:dd/MM/yyyy} ({unbalanced.Description}) no cuadra: no se asentó nada.", true);
        }

        var sequences = await dbContext.JournalSequences
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .ToListAsync(ct);
        var plan = Allocate(entries, ctx.FiscalYearStartMonth, sequences.ToDictionary(x => x.FiscalYear, x => x.LastNumber));
        var now = DateTime.UtcNow;

        foreach (var item in plan)
        {
            var entry = item.Entry;
            var row = new JournalEntry
            {
                CompanyId = companyId,
                BuildingId = ctx.BuildingId,
                FiscalYear = item.FiscalYear,
                Number = item.Number,
                Date = entry.Date,
                OperationDate = entry.Date,
                Description = entry.Description,
                ThirdParty = entry.ThirdParty,
                Reference = entry.Reference,
                Kind = JournalEntryKind.Auto,
                SourceType = (int)entry.SourceType,
                SourceId = entry.SourceId,
                SourceAccountId = entry.SourceAccountId,
                Revision = 0,
                TotalDebit = entry.Debit,
                TotalCredit = entry.Credit,
                ContentHash = HashOf(entry),
                PostedAtUtc = now,
                PostedByUserId = tenantContext.UserId
            };
            var lineNo = 0;
            foreach (var line in entry.Lines)
            {
                row.Lines.Add(new JournalEntryLine
                {
                    LineNo = ++lineNo,
                    LedgerCategoryId = line.CategoryId,
                    Code = line.Code,
                    ExternalCode = line.ExternalCode,
                    Name = line.Name,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    Kind = line.Kind
                });
            }

            dbContext.JournalEntries.Add(row);
        }

        foreach (var group in plan.GroupBy(x => x.FiscalYear))
        {
            var last = group.Max(x => x.Number);
            var sequence = sequences.FirstOrDefault(x => x.FiscalYear == group.Key);
            if (sequence is null)
            {
                dbContext.JournalSequences.Add(new JournalSequence
                {
                    CompanyId = companyId, BuildingId = ctx.BuildingId, FiscalYear = group.Key, LastNumber = last
                });
            }
            else
            {
                sequence.LastNumber = last;
                sequence.UpdatedAtUtc = now;
            }
        }

        var before = settings.JournalPostedThrough;
        settings.JournalPostedThrough = through;
        settings.UpdatedAtUtc = now;

        var firstLabel = plan.Count > 0 ? LabelOf(plan[0].FiscalYear, plan[0].Number) : null;
        var lastLabel = plan.Count > 0 ? LabelOf(plan[^1].FiscalYear, plan[^1].Number) : null;
        var summary = plan.Count == 0
            ? $"Libro Diario: se asentó hasta el {through:dd/MM/yyyy} sin movimientos."
            : $"Libro Diario: se asentaron {plan.Count} asientos ({firstLabel} a {lastLabel}) hasta el {through:dd/MM/yyyy}.";
        Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Accounting, "Posted", summary, "JournalEntry", null,
            [new ConfigChange("journalPostedThrough", "Asentado hasta",
                before?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), through.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))]);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otra persona asento al mismo tiempo (choco el numero o el movimiento de origen): no se guardo nada de esta tanda.
            return (null, "Otra persona asentó al mismo tiempo. No se guardó nada de este asentado: actualizá la pantalla y reintentá.", true);
        }

        return (new PostJournalResultDto { PostedCount = plan.Count, FirstLabel = firstLabel, LastLabel = lastLabel, PostedThrough = through }, null, false);
    }

    // ─── Lectura del Libro Diario ─────────────────────────────────────────────

    public async Task<JournalDto> GetAsync(
        LedgerContext ctx, int? fiscalYear, DateOnly? from, DateOnly? to, int? number, int page, int pageSize, bool canPost, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var all = dbContext.JournalEntries.AsNoTracking().Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId);
        var years = await all.Select(x => x.FiscalYear).Distinct().OrderByDescending(x => x).ToListAsync(ct);
        var year = fiscalYear ?? (years.Count > 0 ? years[0] : FinancePeriods.FiscalYearOf(FinancePeriods.Today(), ctx.FiscalYearStartMonth));

        var query = all.Where(x => x.FiscalYear == year);
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        if (number.HasValue) query = query.Where(x => x.Number == number.Value);

        var totalCount = await query.CountAsync(ct);
        // Se suma en memoria (solo dos importes por asiento): asi no depende de que el proveedor de base de datos sume decimales.
        var amounts = await query.Select(x => new { x.TotalDebit, x.TotalCredit }).ToListAsync(ct);
        var rows = await query.Include(x => x.Lines)
            .OrderBy(x => x.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new JournalDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            FiscalYear = year,
            FiscalYears = years,
            PostedThrough = await PostedThroughAsync(ctx.BuildingId, ct),
            Entries = rows.Select(ToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalDebit = amounts.Sum(x => x.TotalDebit),
            TotalCredit = amounts.Sum(x => x.TotalCredit),
            CanPost = canPost
        };
    }

    private static JournalEntryDto ToDto(JournalEntry e) => new()
    {
        Id = e.Id,
        FiscalYear = e.FiscalYear,
        Number = e.Number,
        Label = LabelOf(e.FiscalYear, e.Number),
        Date = e.Date,
        OperationDate = e.OperationDate,
        Description = e.Description,
        ThirdParty = e.ThirdParty,
        Reference = e.Reference,
        Kind = e.Kind,
        SourceType = e.SourceType.HasValue ? (LedgerSourceType)e.SourceType.Value : null,
        SourceId = e.SourceId,
        ReversesEntryId = e.ReversesEntryId,
        Debit = e.TotalDebit,
        Credit = e.TotalCredit,
        PostedAtUtc = e.PostedAtUtc,
        PostedByUserId = e.PostedByUserId,
        Lines = e.Lines.OrderBy(l => l.LineNo).Select(l => new JournalEntryLineDto
        {
            LedgerCategoryId = l.LedgerCategoryId,
            Code = l.Code,
            ExternalCode = l.ExternalCode,
            Name = l.Name,
            Debit = l.Debit,
            Credit = l.Credit,
            Kind = l.Kind
        }).ToList()
    };

    // ─── Auxiliares ───────────────────────────────────────────────────────────

    public sealed record PlannedEntry(AccountingEntryDto Entry, int FiscalYear, int Number);

    /// <summary>
    /// Reparte los numeros: en el orden de los asientos (ya cronologico), el siguiente correlativo del ejercicio de cada fecha.
    /// <paramref name="lastNumbers"/> es el ultimo numero usado de cada ejercicio.
    /// </summary>
    public static List<PlannedEntry> Allocate(
        IReadOnlyList<AccountingEntryDto> entries, int fiscalYearStartMonth, IReadOnlyDictionary<int, int> lastNumbers)
    {
        var next = lastNumbers.ToDictionary(x => x.Key, x => x.Value);
        var plan = new List<PlannedEntry>(entries.Count);
        foreach (var entry in entries.OrderBy(e => e.Date))
        {
            var year = FinancePeriods.FiscalYearOf(entry.Date, fiscalYearStartMonth);
            var number = next.GetValueOrDefault(year) + 1;
            next[year] = number;
            plan.Add(new PlannedEntry(entry, year, number));
        }

        return plan;
    }

    /// <summary>Huella de un asiento: fecha, origen y renglones. Sirve para saber mas adelante si el movimiento de origen cambio.</summary>
    public static string HashOf(AccountingEntryDto entry)
    {
        var text = new StringBuilder()
            .Append(entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
            .Append((int)entry.SourceType).Append('|').Append(entry.SourceId).Append('|').Append(entry.SourceAccountId);
        foreach (var line in entry.Lines.OrderBy(l => l.Code, StringComparer.Ordinal).ThenBy(l => l.Name, StringComparer.Ordinal))
        {
            text.Append('\n').Append(line.Code).Append('|').Append(line.Name).Append('|')
                .Append(line.Debit.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                .Append(line.Credit.ToString("0.00", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private async Task<DateOnly?> PostedThroughAsync(Guid buildingId, CancellationToken ct) =>
        await dbContext.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .Select(x => x.JournalPostedThrough)
            .FirstOrDefaultAsync(ct);

    private async Task<Dictionary<int, int>> LastNumbersAsync(Guid buildingId, CancellationToken ct) =>
        await dbContext.JournalSequences.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId)
            .ToDictionaryAsync(x => x.FiscalYear, x => x.LastNumber, ct);
}
