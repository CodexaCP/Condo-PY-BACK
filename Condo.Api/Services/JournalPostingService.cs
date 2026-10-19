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
/// Una diferencia entre lo asentado en el Libro Diario y lo que hoy dice el libro de Finanzas. <c>Original</c> es el asiento asentado que se
/// corrige (nulo si el movimiento nunca se asento) y <c>Current</c> el asiento que saldria hoy (nulo si el movimiento ya no existe).
/// <c>NextRevision</c> es la version que llevaria el asiento nuevo.
/// </summary>
public sealed record JournalDifference(JournalDifferenceKind Kind, JournalEntry? Original, AccountingEntryDto? Current, int NextRevision)
{
    // Contraasiento + asiento nuevo (Changed), solo asiento nuevo (Missing) o solo contraasiento (Removed).
    public int EntryCount => Kind == JournalDifferenceKind.Changed ? 2 : 1;
}

/// <summary>
/// Libro Diario: asienta los asientos sugeridos en un numero correlativo por edificio y ejercicio, sin huecos, y los deja guardados sin
/// posibilidad de editarlos (Ley 1034/83, arts. 79 y 80). Se asienta por periodo: todo lo posterior a «asentado hasta» y hasta el dia elegido,
/// en orden cronologico. Nunca se asienta un asiento incompleto. El numero se toma y se guarda en la misma transaccion que los asientos, y los
/// indices unicos (numero, movimiento de origen y contraasiento) frenan a dos personas que asientan a la vez: una sola gana y la otra recibe
/// un conflicto.
///
/// Lo asentado no se corrige nunca: si el movimiento de origen cambia (se edita, se revierte o aparece uno con fecha ya asentada), al asentar
/// se genera un contraasiento que invierte el asiento viejo y, si el movimiento sigue existiendo, un asiento nuevo. La fecha contable de esas
/// correcciones es el dia hasta el que se asienta (nunca antes de lo ya asentado, asi el numero sigue el orden cronologico) y la fecha real
/// de la operacion queda en OperationDate.
/// </summary>
public class JournalPostingService(ICondoDbContext dbContext, AccountingEntriesService accounting, ITenantContext tenantContext)
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;
    private const int MaxDescriptionLength = 500;

    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    /// <summary>Numero para mostrar y citar: ejercicio y correlativo de seis cifras (2026-000012).</summary>
    public static string LabelOf(int fiscalYear, int number) =>
        $"{fiscalYear.ToString(CultureInfo.InvariantCulture)}-{number.ToString("000000", CultureInfo.InvariantCulture)}";

    // ─── Validacion del dia hasta el que se asienta ───────────────────────────

    /// <summary>
    /// El dia puede ser el ya asentado (sirve para asentar solo las correcciones) o uno posterior; nunca uno anterior ni futuro.
    /// </summary>
    public static string? ValidateThrough(DateOnly? postedThrough, DateOnly through, DateOnly startDate, DateOnly today)
    {
        if (through > today) return "No se puede asentar hasta una fecha futura.";
        if (through < startDate) return "La fecha es anterior al arranque de las finanzas del edificio.";
        if (postedThrough.HasValue && through < postedThrough.Value)
        {
            return $"Ya está asentado hasta el {postedThrough.Value:dd/MM/yyyy}: elegí esa fecha o una posterior.";
        }

        return null;
    }

    private static DateOnly StartOf(DateOnly? postedThrough, DateOnly startDate) =>
        postedThrough.HasValue ? postedThrough.Value.AddDays(1) : startDate;

    private static string NothingToPost(DateOnly postedThrough) =>
        $"No hay nada para asentar: ya está asentado hasta el {postedThrough:dd/MM/yyyy} y no hay diferencias con el libro. Elegí una fecha posterior.";

    // ─── Diferencias contra lo asentado ───────────────────────────────────────

    /// <summary>
    /// Compara lo que hoy arma el libro (asientos del dia de arranque a «asentado hasta») con lo asentado. Por cada movimiento se mira su
    /// asiento vigente: el de mayor version que ningun contraasiento anulo. Pura: no toca la base.
    /// </summary>
    public static List<JournalDifference> Compare(
        IReadOnlyList<AccountingEntryDto> current, IReadOnlyList<JournalEntry> posted, IReadOnlySet<Guid> reversedIds)
    {
        var groups = posted
            .Where(e => (e.Kind == JournalEntryKind.Auto || e.Kind == JournalEntryKind.Opening) && e.SourceType.HasValue && e.SourceId.HasValue)
            .GroupBy(e => (e.SourceType!.Value, e.SourceId!.Value, e.OperationDate, e.SourceAccountId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Revision).ToList());
        var currentByKey = current.ToDictionary(e => ((int)e.SourceType, e.SourceId, e.Date, e.SourceAccountId));

        var result = new List<JournalDifference>();
        foreach (var (key, versions) in groups)
        {
            var active = versions.FirstOrDefault(e => !reversedIds.Contains(e.Id));
            var next = versions[0].Revision + 1;
            if (currentByKey.TryGetValue(key, out var now))
            {
                if (active is null) result.Add(new JournalDifference(JournalDifferenceKind.Missing, null, now, next));
                else if (!HashMatches(active.ContentHash, now)) result.Add(new JournalDifference(JournalDifferenceKind.Changed, active, now, next));
            }
            else if (active is not null)
            {
                result.Add(new JournalDifference(JournalDifferenceKind.Removed, active, null, next));
            }
        }

        foreach (var (key, now) in currentByKey)
        {
            if (!groups.ContainsKey(key)) result.Add(new JournalDifference(JournalDifferenceKind.Missing, null, now, 0));
        }

        return result
            .OrderBy(d => d.Original?.OperationDate ?? d.Current!.Date)
            .ThenBy(d => d.Original?.Number ?? int.MaxValue)
            .ThenBy(d => d.Current?.Description, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Diferencias de un edificio: nada si todavia no se asento ningun dia.</summary>
    public async Task<List<JournalDifference>> DetectAsync(LedgerContext ctx, DateOnly? postedThrough, CancellationToken ct)
    {
        if (!postedThrough.HasValue || postedThrough.Value < ctx.StartDate) return [];

        var built = await accounting.BuildAsync(ctx, ctx.StartDate, postedThrough.Value, ct);
        var posted = await dbContext.JournalEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && (x.Kind == JournalEntryKind.Auto || x.Kind == JournalEntryKind.Opening) && x.SourceType != null)
            .ToListAsync(ct);
        var reversed = (await dbContext.JournalEntries.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && x.ReversesEntryId != null)
                .Select(x => x.ReversesEntryId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        return Compare(built.Entries, posted, reversed);
    }

    public async Task<JournalDifferencesDto> DifferencesAsync(LedgerContext ctx, CancellationToken ct)
    {
        var postedThrough = await PostedThroughAsync(ctx.BuildingId, ct);
        var items = await DetectAsync(ctx, postedThrough, ct);
        var reasons = await ReasonsAsync(items, ct);
        return new JournalDifferencesDto
        {
            BuildingId = ctx.BuildingId,
            PostedThrough = postedThrough,
            Items = items.Select(d => ToDto(d, reasons)).ToList(),
            ChangedCount = items.Count(d => d.Kind == JournalDifferenceKind.Changed),
            MissingCount = items.Count(d => d.Kind == JournalDifferenceKind.Missing),
            RemovedCount = items.Count(d => d.Kind == JournalDifferenceKind.Removed),
            IncompleteCount = items.Count(d => d.Current is { IsComplete: false }),
            Pending = PendingNames(items.Where(d => d.Current is not null).Select(d => d.Current!))
        };
    }

    /// <summary>Que paso con el movimiento, en palabras, segun su tipo y el tipo de diferencia.</summary>
    public static string CauseOf(JournalDifferenceKind kind, LedgerSourceType type) => (kind, type) switch
    {
        (JournalDifferenceKind.Changed, LedgerSourceType.OpeningBalance) => "Saldos iniciales modificados",
        (JournalDifferenceKind.Removed, LedgerSourceType.OpeningBalance) => "Saldos iniciales eliminados",
        (JournalDifferenceKind.Missing, LedgerSourceType.OpeningBalance) => "Asiento de apertura sin asentar",
        (JournalDifferenceKind.Changed, LedgerSourceType.OwnerPayment) => "Pago modificado",
        (JournalDifferenceKind.Changed, LedgerSourceType.BuildingExpense) => "Gasto modificado",
        (JournalDifferenceKind.Changed, LedgerSourceType.BuildingIncome) => "Ingreso modificado",
        (JournalDifferenceKind.Changed, LedgerSourceType.SupplierCreditNote) => "Nota de crédito modificada",
        (JournalDifferenceKind.Removed, LedgerSourceType.OwnerPayment) => "Pago revertido",
        (JournalDifferenceKind.Removed, LedgerSourceType.BuildingExpense) => "Gasto eliminado o ya no cuenta en caja",
        (JournalDifferenceKind.Removed, LedgerSourceType.BuildingIncome) => "Ingreso eliminado",
        (JournalDifferenceKind.Removed, LedgerSourceType.SupplierCreditNote) => "Nota de crédito anulada",
        (JournalDifferenceKind.Missing, LedgerSourceType.OwnerPayment) => "Pago cargado después de asentar ese día",
        (JournalDifferenceKind.Missing, LedgerSourceType.BuildingExpense) => "Gasto cargado después de asentar ese día",
        (JournalDifferenceKind.Missing, LedgerSourceType.BuildingIncome) => "Ingreso cargado después de asentar ese día",
        _ => "Movimiento cargado después de asentar ese día"
    };

    private static LedgerSourceType SourceTypeOf(JournalDifference d) =>
        d.Current?.SourceType ?? (LedgerSourceType)(d.Original?.SourceType ?? (int)LedgerSourceType.BuildingExpense);

    private static Guid? SourceIdOf(JournalDifference d) => d.Current?.SourceId ?? d.Original?.SourceId;

    /// <summary>
    /// Motivo que dejo quien hizo el cambio, para los movimientos que lo guardan: la reversa de un pago (queda en el pago del propietario) y la
    /// anulacion de una nota de credito del proveedor. Se usa en la glosa del contraasiento. Indexado por el id del movimiento de origen.
    /// </summary>
    public async Task<Dictionary<Guid, string>> ReasonsAsync(IReadOnlyList<JournalDifference> differences, CancellationToken ct)
    {
        var reasons = new Dictionary<Guid, string>();
        var removed = differences.Where(d => d.Kind == JournalDifferenceKind.Removed).ToList();

        var paymentIds = removed.Where(d => SourceTypeOf(d) == LedgerSourceType.OwnerPayment && SourceIdOf(d).HasValue)
            .Select(d => SourceIdOf(d)!.Value).Distinct().ToList();
        if (paymentIds.Count > 0)
        {
            var payments = await dbContext.Payments.AsNoTracking().Where(x => paymentIds.Contains(x.Id))
                .Select(x => new { x.Id, x.CompanyId, x.Reference }).ToListAsync(ct);
            var references = payments.Select(x => x.Reference).Distinct().ToList();
            var owners = await dbContext.OwnerPayments.AsNoTracking()
                .Where(x => references.Contains(x.Reference) && x.RejectionReason.StartsWith(ReversalPrefix))
                .Select(x => new { x.CompanyId, x.Reference, x.RejectionReason }).ToListAsync(ct);
            foreach (var p in payments)
            {
                var owner = owners.FirstOrDefault(o => o.CompanyId == p.CompanyId && o.Reference == p.Reference);
                if (owner is not null) reasons[p.Id] = owner.RejectionReason[ReversalPrefix.Length..].Trim();
            }
        }

        var noteIds = removed.Where(d => SourceTypeOf(d) == LedgerSourceType.SupplierCreditNote && SourceIdOf(d).HasValue)
            .Select(d => SourceIdOf(d)!.Value).Distinct().ToList();
        if (noteIds.Count > 0)
        {
            var notes = await dbContext.BuildingExpenseCreditNotes.AsNoTracking()
                .Where(x => noteIds.Contains(x.Id) && x.Status == BuildingExpenseCreditNoteStatus.Voided && x.VoidReason != "")
                .Select(x => new { x.Id, x.VoidReason }).ToListAsync(ct);
            foreach (var n in notes) reasons[n.Id] = n.VoidReason.Trim();
        }

        return reasons;
    }

    // «PAGO REVERTIDO: motivo»: asi guarda la reversa de un pago el pago del propietario.
    private const string ReversalPrefix = "PAGO REVERTIDO: ";

    private static JournalDifferenceDto ToDto(JournalDifference d, IReadOnlyDictionary<Guid, string> reasons) => new()
    {
        Kind = d.Kind,
        SourceType = SourceTypeOf(d),
        Cause = CauseOf(d.Kind, SourceTypeOf(d)),
        Reason = SourceIdOf(d) is { } sourceId && reasons.TryGetValue(sourceId, out var reason) ? reason : null,
        EntryId = d.Original?.Id,
        EntryLabel = d.Original is null ? null : LabelOf(d.Original.FiscalYear, d.Original.Number),
        OperationDate = d.Original?.OperationDate ?? d.Current!.Date,
        Description = d.Original?.Description ?? d.Current!.Description,
        ThirdParty = d.Original?.ThirdParty ?? d.Current!.ThirdParty,
        AmountBefore = d.Original?.TotalDebit,
        AmountAfter = d.Current?.Debit,
        Incomplete = d.Current is { IsComplete: false }
    };

    private static List<string> PendingNames(IEnumerable<AccountingEntryDto> entries) =>
        entries.SelectMany(e => e.Lines).Where(l => l.Missing).Select(l => l.Name).Distinct()
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    // ─── Vista previa ─────────────────────────────────────────────────────────

    public async Task<(JournalPreviewDto? Preview, string? Error)> PreviewAsync(LedgerContext ctx, DateOnly through, CancellationToken ct)
    {
        var postedThrough = await PostedThroughAsync(ctx.BuildingId, ct);
        var error = ValidateThrough(postedThrough, through, ctx.StartDate, FinancePeriods.Today());
        if (error is not null) return (null, error);

        var differences = await DetectAsync(ctx, postedThrough, ct);
        var start = StartOf(postedThrough, ctx.StartDate);
        IReadOnlyList<AccountingEntryDto> entries = !postedThrough.HasValue || through > postedThrough.Value
            ? (await accounting.BuildAsync(ctx, start, through, ct)).Entries
            : [];
        if (postedThrough.HasValue && through == postedThrough.Value && differences.Count == 0)
        {
            return (null, NothingToPost(postedThrough.Value));
        }

        var lastNumbers = await LastNumbersAsync(ctx.BuildingId, ct);
        var dates = entries.OrderBy(e => e.Date).Select(e => e.Date)
            .Concat(differences.SelectMany(d => Enumerable.Repeat(through, d.EntryCount))).ToList();
        var numbers = AllocateDates(dates, ctx.FiscalYearStartMonth, lastNumbers);
        var incomplete = entries.Count(e => !e.IsComplete) + differences.Count(d => d.Current is { IsComplete: false });
        var reasons = await ReasonsAsync(differences, ct);
        var drafts = await DraftPeriodNamesAsync(entries.Concat(differences.Where(d => d.Current is not null).Select(d => d.Current!)), ct);

        return (new JournalPreviewDto
        {
            BuildingId = ctx.BuildingId,
            PostedThrough = postedThrough,
            From = start,
            Through = through,
            EntryCount = entries.Count,
            IncompleteCount = incomplete,
            Pending = PendingNames(entries.Concat(differences.Where(d => d.Current is not null).Select(d => d.Current!))),
            FirstLabel = numbers.Count > 0 ? LabelOf(numbers[0].FiscalYear, numbers[0].Number) : null,
            LastLabel = numbers.Count > 0 ? LabelOf(numbers[^1].FiscalYear, numbers[^1].Number) : null,
            TotalDebit = entries.Sum(e => e.Debit),
            TotalCredit = entries.Sum(e => e.Credit),
            Differences = differences.Select(d => ToDto(d, reasons)).ToList(),
            CorrectionEntryCount = differences.Sum(d => d.EntryCount),
            DraftPeriods = drafts,
            CanPost = numbers.Count > 0 && incomplete == 0 && drafts.Count == 0
        }, null);
    }

    // ─── Asentar ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Asienta todo lo posterior a lo ya asentado hasta <paramref name="through"/> y corrige lo asentado que cambio. El error es
    /// <c>Conflict</c> si hay que arreglar algo y reintentar.
    /// </summary>
    public async Task<(PostJournalResultDto? Result, string? Error, bool Conflict)> PostAsync(
        LedgerContext ctx, DateOnly through, Guid companyId, CancellationToken ct)
    {
        var settings = await dbContext.FinanceSettings.FirstOrDefaultAsync(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId, ct);
        if (settings is null) return (null, "Completá la configuración inicial de Finanzas del edificio.", true);

        var posted = settings.JournalPostedThrough;
        var error = ValidateThrough(posted, through, ctx.StartDate, FinancePeriods.Today());
        if (error is not null) return (null, error, false);

        var differences = await DetectAsync(ctx, posted, ct);
        IReadOnlyList<AccountingEntryDto> entries = !posted.HasValue || through > posted.Value
            ? (await accounting.BuildAsync(ctx, StartOf(posted, ctx.StartDate), through, ct)).Entries
            : [];
        if (posted.HasValue && through == posted.Value && differences.Count == 0) return (null, NothingToPost(posted.Value), false);

        return await PostBuiltAsync(ctx, settings, entries, through, companyId, ct, differences);
    }

    /// <summary>
    /// Guarda en el Libro Diario los asientos ya armados y las correcciones (el armado y la guarda estan separados para poder probar cada uno).
    /// Orden: primero los asientos nuevos por fecha y despues las correcciones, todas con la fecha del dia hasta el que se asienta.
    /// </summary>
    public async Task<(PostJournalResultDto? Result, string? Error, bool Conflict)> PostBuiltAsync(
        LedgerContext ctx, FinanceSettings settings, IReadOnlyList<AccountingEntryDto> entries, DateOnly through, Guid companyId,
        CancellationToken ct, IReadOnlyList<JournalDifference>? differences = null)
    {
        differences ??= [];
        var toCheck = entries.Concat(differences.Where(d => d.Current is not null).Select(d => d.Current!)).ToList();

        var incomplete = toCheck.Where(e => !e.IsComplete).ToList();
        if (incomplete.Count > 0)
        {
            var pending = string.Join(", ", PendingNames(toCheck));
            return (null, $"Hay {incomplete.Count} asientos incompletos porque falta la cuenta del plan de: {pending}. Asignalas y volvé a asentar.", true);
        }

        // Un gasto o ingreso de un periodo en borrador todavia se puede editar o borrar: no se asienta hasta que el periodo se publique.
        var drafts = await DraftPeriodNamesAsync(toCheck, ct);
        if (drafts.Count > 0)
        {
            return (null, $"No se puede asentar: hay gastos o ingresos en períodos en borrador ({string.Join(", ", drafts)}). Publicá esos períodos o asentá hasta una fecha anterior: mientras estén en borrador pueden cambiar.", true);
        }

        var unbalanced = toCheck.FirstOrDefault(e => e.Debit != e.Credit);
        if (unbalanced is not null)
        {
            return (null, $"El asiento del {unbalanced.Date:dd/MM/yyyy} ({unbalanced.Description}) no cuadra: no se asentó nada.", true);
        }

        // Los asientos a anular se leen con sus renglones para invertirlos.
        var originalIds = differences.Where(d => d.Original is not null).Select(d => d.Original!.Id).ToList();
        var originals = originalIds.Count == 0
            ? new Dictionary<Guid, JournalEntry>()
            : await dbContext.JournalEntries.AsNoTracking().Include(x => x.Lines)
                .Where(x => originalIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var reasons = await ReasonsAsync(differences, ct);
        var now = DateTime.UtcNow;
        var rows = new List<JournalEntry>();
        foreach (var entry in entries.OrderBy(e => e.Date))
        {
            rows.Add(NewRow(entry, entry.Date, 0, entry.Description, companyId, ctx.BuildingId, now));
        }

        var correctionCount = 0;
        foreach (var d in differences)
        {
            if (d.Original is not null)
            {
                var why = SourceIdOf(d) is { } sourceId && reasons.TryGetValue(sourceId, out var reason) ? reason : null;
                rows.Add(ReversalRow(originals[d.Original.Id], through, companyId, ctx.BuildingId, now, CauseOf(d.Kind, SourceTypeOf(d)), why));
                correctionCount++;
            }

            if (d.Current is not null)
            {
                var note = d.Original is not null
                    ? $"{d.Current.Description} (corrige el asiento {LabelOf(d.Original.FiscalYear, d.Original.Number)})"
                    : $"{d.Current.Description} (movimiento del {d.Current.Date:dd/MM/yyyy}, asentado después)";
                rows.Add(NewRow(d.Current, through, d.NextRevision, note, companyId, ctx.BuildingId, now));
                correctionCount++;
            }
        }

        await AssignNumbersAsync(ctx, rows, companyId, now, ct);

        var before = settings.JournalPostedThrough;
        settings.JournalPostedThrough = through;
        settings.UpdatedAtUtc = now;

        var firstLabel = rows.Count > 0 ? LabelOf(rows[0].FiscalYear, rows[0].Number) : null;
        var lastLabel = rows.Count > 0 ? LabelOf(rows[^1].FiscalYear, rows[^1].Number) : null;
        var summary = rows.Count == 0
            ? $"Libro Diario: se asentó hasta el {through:dd/MM/yyyy} sin movimientos."
            : $"Libro Diario: se asentaron {rows.Count} asientos ({firstLabel} a {lastLabel}) hasta el {through:dd/MM/yyyy}"
              + (correctionCount > 0 ? $", {correctionCount} de ellos correcciones de asientos que cambiaron (contraasiento y asiento nuevo)." : ".");
        Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Accounting, "Posted", summary, "JournalEntry", null,
            [new ConfigChange("journalPostedThrough", "Asentado hasta",
                before?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), through.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))]);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otra persona asento al mismo tiempo (choco el numero, el movimiento de origen o el contraasiento): no se guardo nada de esta tanda.
            return (null, "Otra persona asentó al mismo tiempo. No se guardó nada de este asentado: actualizá la pantalla y reintentá.", true);
        }

        return (new PostJournalResultDto
        {
            PostedCount = rows.Count, CorrectionEntryCount = correctionCount, FirstLabel = firstLabel, LastLabel = lastLabel, PostedThrough = through
        }, null, false);
    }

    /// <summary>
    /// Numera los asientos nuevos (en el orden dado, que ya es cronologico), los agrega al contexto y actualiza el ultimo numero de cada ejercicio.
    /// No guarda: lo guarda el SaveChanges de quien llama, en la misma transaccion que el resto (todo o nada).
    /// </summary>
    public async Task AssignNumbersAsync(LedgerContext ctx, IReadOnlyList<JournalEntry> rows, Guid companyId, DateTime now, CancellationToken ct)
    {
        var sequences = await dbContext.JournalSequences
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .ToListAsync(ct);
        var numbers = AllocateDates(rows.Select(r => r.Date).ToList(), ctx.FiscalYearStartMonth, sequences.ToDictionary(x => x.FiscalYear, x => x.LastNumber));
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].FiscalYear = numbers[i].FiscalYear;
            rows[i].Number = numbers[i].Number;
            dbContext.JournalEntries.Add(rows[i]);
        }

        foreach (var group in numbers.GroupBy(x => x.FiscalYear))
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
    }

    // Asiento que sale de un movimiento del libro. La fecha contable es la del movimiento salvo en las correcciones.
    private JournalEntry NewRow(
        AccountingEntryDto entry, DateOnly date, int revision, string description, Guid companyId, Guid buildingId, DateTime now)
    {
        var row = new JournalEntry
        {
            CompanyId = companyId,
            BuildingId = buildingId,
            Date = date,
            OperationDate = entry.Date,
            Description = Truncate(description),
            ThirdParty = entry.ThirdParty,
            Reference = entry.Reference,
            Kind = entry.SourceType == LedgerSourceType.OpeningBalance ? JournalEntryKind.Opening : JournalEntryKind.Auto,
            SourceType = (int)entry.SourceType,
            SourceId = entry.SourceId,
            SourceAccountId = entry.SourceAccountId,
            Revision = revision,
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
                Kind = line.Kind,
                AccountType = line.AccountType
            });
        }

        return row;
    }

    // Contraasiento: los mismos renglones con el debe y el haber invertidos. No tiene origen propio (lo identifica ReversesEntryId).
    public JournalEntry ReversalRow(JournalEntry original, DateOnly date, Guid companyId, Guid buildingId, DateTime now, string cause, string? reason)
    {
        var row = new JournalEntry
        {
            CompanyId = companyId,
            BuildingId = buildingId,
            Date = date,
            OperationDate = original.OperationDate,
            Description = Truncate($"Contraasiento del asiento {LabelOf(original.FiscalYear, original.Number)} ({cause.ToLowerInvariant()}{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $". Motivo: {reason}")}): {original.Description}"),
            ThirdParty = original.ThirdParty,
            Reference = original.Reference,
            Kind = JournalEntryKind.Reversal,
            ReversesEntryId = original.Id,
            TotalDebit = original.TotalCredit,
            TotalCredit = original.TotalDebit,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"reversal|{original.Id}"))).ToLowerInvariant(),
            PostedAtUtc = now,
            PostedByUserId = tenantContext.UserId
        };
        var lineNo = 0;
        foreach (var line in original.Lines.OrderBy(l => l.LineNo))
        {
            row.Lines.Add(new JournalEntryLine
            {
                LineNo = ++lineNo,
                LedgerCategoryId = line.LedgerCategoryId,
                Code = line.Code,
                ExternalCode = line.ExternalCode,
                Name = line.Name,
                Debit = line.Credit,
                Credit = line.Debit,
                Kind = line.Kind,
                AccountType = line.AccountType
            });
        }

        return row;
    }

    private static string Truncate(string text) =>
        text.Length > MaxDescriptionLength ? text[..MaxDescriptionLength] : text;

    // ─── Periodos en borrador e impacto sobre lo asentado ─────────────────────

    /// <summary>
    /// Nombres de los periodos en borrador a los que pertenecen los gastos e ingresos de estos asientos. Un periodo en borrador todavia se
    /// puede editar, asi que lo que sale de el no se asienta hasta que se publique.
    /// </summary>
    public async Task<List<string>> DraftPeriodNamesAsync(IEnumerable<AccountingEntryDto> entries, CancellationToken ct)
    {
        var list = entries.ToList();
        var expenseIds = list.Where(e => e.SourceType == LedgerSourceType.BuildingExpense).Select(e => e.SourceId).Distinct().ToList();
        var incomeIds = list.Where(e => e.SourceType == LedgerSourceType.BuildingIncome).Select(e => e.SourceId).Distinct().ToList();
        var names = new List<string>();

        if (expenseIds.Count > 0)
        {
            names.AddRange(await dbContext.BuildingExpenses.AsNoTracking()
                .Where(x => expenseIds.Contains(x.Id) && x.ExpensePeriod!.Status == ExpensePeriodStatus.Draft)
                .Select(x => x.ExpensePeriod!.Name).Distinct().ToListAsync(ct));
        }

        if (incomeIds.Count > 0)
        {
            names.AddRange(await dbContext.BuildingIncomes.AsNoTracking()
                .Where(x => incomeIds.Contains(x.Id) && x.ExpensePeriod!.Status == ExpensePeriodStatus.Draft)
                .Select(x => x.ExpensePeriod!.Name).Distinct().ToListAsync(ct));
        }

        return names.Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Cuantos asientos vigentes (asentados y no anulados) salen de estos movimientos. Sirve para avisar, antes de revertir un pago o deshacer la
    /// publicacion de un periodo, que eso va a generar un contraasiento.
    /// </summary>
    public async Task<JournalImpactDto> ImpactAsync(
        IReadOnlyCollection<Guid> buildingIds, LedgerSourceType sourceType, IReadOnlyCollection<Guid> sourceIds, CancellationToken ct)
    {
        if (buildingIds.Count == 0 || sourceIds.Count == 0) return new JournalImpactDto();

        var type = (int)sourceType;
        var entries = await dbContext.JournalEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && buildingIds.Contains(x.BuildingId) && x.Kind == JournalEntryKind.Auto
                        && x.SourceType == type && x.SourceId != null && sourceIds.Contains(x.SourceId.Value))
            .Select(x => new { x.Id, x.FiscalYear, x.Number })
            .ToListAsync(ct);
        if (entries.Count == 0) return new JournalImpactDto();

        var ids = entries.Select(e => e.Id).ToList();
        var reversed = (await dbContext.JournalEntries.AsNoTracking()
                .Where(x => x.ReversesEntryId != null && ids.Contains(x.ReversesEntryId.Value))
                .Select(x => x.ReversesEntryId!.Value).ToListAsync(ct))
            .ToHashSet();
        var active = entries.Where(e => !reversed.Contains(e.Id)).OrderBy(e => e.FiscalYear).ThenBy(e => e.Number).ToList();

        return new JournalImpactDto
        {
            Posted = active.Count > 0,
            PostedCount = active.Count,
            Labels = active.Take(5).Select(e => LabelOf(e.FiscalYear, e.Number)).ToList()
        };
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

        // Numero del contraasiento de cada asiento anulado y del asiento que invierte cada contraasiento (pueden estar en otro ejercicio).
        var ids = rows.Select(r => r.Id).ToList();
        var reversedBy = await all.Where(x => x.ReversesEntryId != null && ids.Contains(x.ReversesEntryId.Value))
            .Select(x => new { Original = x.ReversesEntryId!.Value, x.FiscalYear, x.Number })
            .ToDictionaryAsync(x => x.Original, x => LabelOf(x.FiscalYear, x.Number), ct);
        var reversedIds = rows.Where(r => r.ReversesEntryId.HasValue).Select(r => r.ReversesEntryId!.Value).ToList();
        var reverses = await all.Where(x => reversedIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FiscalYear, x.Number })
            .ToDictionaryAsync(x => x.Id, x => LabelOf(x.FiscalYear, x.Number), ct);

        return new JournalDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            FiscalYear = year,
            FiscalYears = years,
            PostedThrough = await PostedThroughAsync(ctx.BuildingId, ct),
            Entries = rows.Select(r => ToDto(r, reversedBy, reverses)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalDebit = amounts.Sum(x => x.TotalDebit),
            TotalCredit = amounts.Sum(x => x.TotalCredit),
            CanPost = canPost
        };
    }

    private static JournalEntryDto ToDto(JournalEntry e, IReadOnlyDictionary<Guid, string> reversedBy, IReadOnlyDictionary<Guid, string> reverses) => new()
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
        ReversesLabel = e.ReversesEntryId.HasValue && reverses.TryGetValue(e.ReversesEntryId.Value, out var original) ? original : null,
        ReversedByLabel = reversedBy.TryGetValue(e.Id, out var reversal) ? reversal : null,
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

    // ─── Numeracion y huellas ─────────────────────────────────────────────────

    public sealed record PlannedEntry(AccountingEntryDto Entry, int FiscalYear, int Number);

    public sealed record AllocatedNumber(int FiscalYear, int Number);

    /// <summary>
    /// Reparte los numeros: en el orden de las fechas dadas (ya cronologico), el siguiente correlativo del ejercicio de cada una.
    /// <paramref name="lastNumbers"/> es el ultimo numero usado de cada ejercicio.
    /// </summary>
    public static List<AllocatedNumber> AllocateDates(
        IReadOnlyList<DateOnly> dates, int fiscalYearStartMonth, IReadOnlyDictionary<int, int> lastNumbers)
    {
        var next = lastNumbers.ToDictionary(x => x.Key, x => x.Value);
        var result = new List<AllocatedNumber>(dates.Count);
        foreach (var date in dates)
        {
            var year = FinancePeriods.FiscalYearOf(date, fiscalYearStartMonth);
            var number = next.GetValueOrDefault(year) + 1;
            next[year] = number;
            result.Add(new AllocatedNumber(year, number));
        }

        return result;
    }

    /// <summary>Lo mismo para asientos armados: el numero de cada uno, en orden de fecha.</summary>
    public static List<PlannedEntry> Allocate(
        IReadOnlyList<AccountingEntryDto> entries, int fiscalYearStartMonth, IReadOnlyDictionary<int, int> lastNumbers)
    {
        var ordered = entries.OrderBy(e => e.Date).ToList();
        var numbers = AllocateDates(ordered.Select(e => e.Date).ToList(), fiscalYearStartMonth, lastNumbers);
        return ordered.Select((e, i) => new PlannedEntry(e, numbers[i].FiscalYear, numbers[i].Number)).ToList();
    }

    /// <summary>
    /// Huella de un asiento: fecha, origen y renglones (rubro, importes). Sirve para saber si el movimiento de origen cambio despues de asentarlo.
    /// No incluye el codigo ni el nombre de las cuentas ni la cuenta del plan de los bancos y del IVA: renombrar o recodificar el plan, o
    /// reasignar la cuenta de un banco, no es un cambio del movimiento y no debe generar correcciones. El rubro si cuenta (reclasificar un gasto).
    /// </summary>
    public static string HashOf(AccountingEntryDto entry)
    {
        var text = new StringBuilder("v2|")
            .Append(entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
            .Append((int)entry.SourceType).Append('|').Append(entry.SourceId).Append('|').Append(entry.SourceAccountId);
        var lines = entry.Lines
            // En la apertura cada renglon es una cuenta distinta (un banco, la caja, el fondo, la contrapartida): cuenta la del plan de todos.
            .Select(l => (l.Kind, Category: l.Kind == "Rubro" || entry.SourceType == LedgerSourceType.OpeningBalance ? l.CategoryId?.ToString() ?? string.Empty : string.Empty, l.Debit, l.Credit))
            .OrderBy(l => l.Kind, StringComparer.Ordinal).ThenBy(l => l.Category, StringComparer.Ordinal)
            .ThenBy(l => l.Debit).ThenBy(l => l.Credit);
        foreach (var line in lines)
        {
            text.Append('\n').Append(line.Kind).Append('|').Append(line.Category).Append('|')
                .Append(line.Debit.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                .Append(line.Credit.ToString("0.00", CultureInfo.InvariantCulture));
        }

        return Sha(text.ToString());
    }

    // Huella de la fase 1 (con codigo y nombre de cuenta): los asientos asentados antes de la fase 2 se siguen reconociendo.
    private static string LegacyHashOf(AccountingEntryDto entry)
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

        return Sha(text.ToString());
    }

    private static bool HashMatches(string stored, AccountingEntryDto entry) =>
        string.Equals(stored, HashOf(entry), StringComparison.Ordinal) || string.Equals(stored, LegacyHashOf(entry), StringComparison.Ordinal);

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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
