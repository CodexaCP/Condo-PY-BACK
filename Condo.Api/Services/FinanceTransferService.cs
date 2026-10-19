using System.Data;
using System.Globalization;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Resultado de una operacion guiada de Finanzas: el valor o el error con su codigo HTTP y su codigo de maquina.</summary>
public sealed class FinanceOpResult<T>
{
    public T? Value { get; init; }
    public int Status { get; init; } = 200;
    public string? Code { get; init; }
    public string? Message { get; init; }
    public bool Ok => Message is null;

    public static FinanceOpResult<T> Success(T value) => new() { Value = value };
    public static FinanceOpResult<T> Fail(int status, string code, string message) => new() { Status = status, Code = code, Message = message };
}

/// <summary>
/// Operaciones guiadas de Finanzas: transferencias entre cuentas propias (deposito y retiro de efectivo, aporte y retiro del fondo de reserva,
/// transferencia entre bancos). No son ingreso ni gasto: mueven el saldo de cada cuenta y el total del edificio no cambia. Una transferencia no se
/// edita; se anula con motivo y el Libro Diario la corrige con un contraasiento. Todas las reglas viven aca para que las use igual la vista previa
/// y el alta.
/// </summary>
public class FinanceTransferService(
    ICondoDbContext dbContext,
    FinanceLedgerService ledger,
    ITenantContext tenantContext)
{
    private FinancePeriodGuard periodGuard => new(dbContext);

    public const string InvalidCode = "transfer_invalid";
    public const string NotFoundCode = "transfer_not_found";
    public const string AlreadyVoidedCode = "transfer_already_voided";
    public const string FiscalYearClosedCode = "transfer_fiscal_year_closed";
    public const string NegativeBalanceCode = "transfer_negative_balance";
    public const string FundNegativeCode = "transfer_fund_negative";
    public const string ReconciledCode = "transfer_reconciled";

    private const int MaxListItems = 500;

    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    // ─── Consulta ─────────────────────────────────────────────────────────────

    public async Task<FinanceTransfersDto> ListAsync(LedgerContext ctx, bool canEdit, CancellationToken ct)
    {
        var balances = await BalancesAsync(ctx, ct);
        var rows = await dbContext.FinanceTransfers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .OrderByDescending(x => x.TransferDate).ThenByDescending(x => x.CreatedAtUtc)
            .Take(MaxListItems)
            .ToListAsync(ct);

        var names = await UserNamesAsync(rows.Select(x => x.CreatedByUserId).Concat(rows.Where(x => x.VoidedByUserId.HasValue).Select(x => x.VoidedByUserId!.Value)), ct);
        var labels = await JournalLabelsAsync(ctx.BuildingId, rows.Select(x => x.Id).ToList(), ct);
        var postedThrough = await dbContext.FinanceSettings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .Select(x => x.JournalPostedThrough)
            .FirstOrDefaultAsync(ct);

        return new FinanceTransfersDto
        {
            BuildingId = ctx.BuildingId,
            BuildingName = ctx.BuildingName,
            CanEdit = canEdit,
            FinanceStartDate = ctx.StartDate,
            PostedThrough = postedThrough,
            TotalBalance = balances.Values.Sum(),
            Accounts = ctx.Accounts
                .OrderBy(a => a.Type).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new FinanceTransferAccountDto
                {
                    Id = a.Id, Name = a.Name, Type = a.Type, IsActive = a.IsActive, Balance = balances.GetValueOrDefault(a.Id)
                })
                .ToList(),
            Items = rows.Select(x => ToDto(ctx, x, names, labels)).ToList()
        };
    }

    // ─── Vista previa ─────────────────────────────────────────────────────────

    public async Task<FinanceOpResult<FinanceTransferPreviewDto>> PreviewAsync(LedgerContext ctx, CreateFinanceTransferRequest request, CancellationToken ct)
    {
        var prepared = await PrepareAsync(ctx, request, preview: true, ct);
        if (!prepared.Ok) return FinanceOpResult<FinanceTransferPreviewDto>.Fail(prepared.Status, prepared.Code!, prepared.Message!);

        var p = prepared.Value!;
        var plan = await PlanNamesAsync(ctx, [p.From.Id, p.To.Id], ct);
        var warnings = new List<string>();
        if (p.NegativeAfter) warnings.Add($"«{p.From.Name}» quedaría con saldo negativo ({Gs(p.FromBefore - request.Amount)}). Confirmá que es correcto.");
        if (plan.GetValueOrDefault(p.From.Id) is null) warnings.Add($"La cuenta «{p.From.Name}» no tiene cuenta del plan asignada: el asiento quedará incompleto hasta asignarla.");
        if (plan.GetValueOrDefault(p.To.Id) is null) warnings.Add($"La cuenta «{p.To.Name}» no tiene cuenta del plan asignada: el asiento quedará incompleto hasta asignarla.");
        if (plan.GetValueOrDefault(p.From.Id) is { } a && plan.GetValueOrDefault(p.To.Id) is { } b && a == b)
        {
            warnings.Add("Las dos cuentas usan la misma cuenta del plan: la operación mueve los saldos de Finanzas pero no genera asiento.");
        }

        return FinanceOpResult<FinanceTransferPreviewDto>.Success(new FinanceTransferPreviewDto
        {
            Kind = FinanceTransferRules.KindLabel(p.From.Type, p.To.Type),
            FromAccountName = p.From.Name,
            FromBefore = p.FromBefore,
            FromAfter = p.FromBefore - request.Amount,
            ToAccountName = p.To.Name,
            ToBefore = p.ToBefore,
            ToAfter = p.ToBefore + request.Amount,
            TotalBalance = p.TotalBalance,
            RequiresSupport = FinanceTransferRules.InvolvesReserveFund(p.From.Type, p.To.Type),
            NeedsNegativeConfirmation = p.NegativeAfter,
            DebitAccount = plan.GetValueOrDefault(p.To.Id),
            CreditAccount = plan.GetValueOrDefault(p.From.Id),
            Warnings = warnings
        });
    }

    // ─── Alta ─────────────────────────────────────────────────────────────────

    /// <summary>Alta. Revisa saldo y guarda en una transaccion serializable: dos operaciones simultaneas no pueden dejar el fondo de reserva en negativo.</summary>
    public Task<FinanceOpResult<FinanceTransferDto>> CreateAsync(
        LedgerContext ctx, CreateFinanceTransferRequest request, Guid companyId, CancellationToken ct) =>
        InTransactionAsync(() => CreateCoreAsync(ctx, request, companyId, ct), ct);

    private async Task<FinanceOpResult<FinanceTransferDto>> CreateCoreAsync(
        LedgerContext ctx, CreateFinanceTransferRequest request, Guid companyId, CancellationToken ct)
    {
        var prepared = await PrepareAsync(ctx, request, preview: false, ct);
        if (!prepared.Ok) return FinanceOpResult<FinanceTransferDto>.Fail(prepared.Status, prepared.Code!, prepared.Message!);

        var p = prepared.Value!;
        var entity = new FinanceTransfer
        {
            CompanyId = companyId,
            BuildingId = ctx.BuildingId,
            FromAccountId = p.From.Id,
            ToAccountId = p.To.Id,
            Amount = request.Amount,
            TransferDate = p.Date,
            Reference = p.Reference,
            Notes = p.Notes,
            SupportReference = p.Support,
            CreatedByUserId = tenantContext.UserId,
            Status = FinanceTransferStatus.Active
        };
        dbContext.FinanceTransfers.Add(entity);
        Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Accounting, "Created",
            $"Operaciones de Finanzas: {FinanceTransferRules.KindLabel(p.From.Type, p.To.Type).ToLowerInvariant()} de {Gs(request.Amount)} de «{p.From.Name}» a «{p.To.Name}» ({p.Date:dd/MM/yyyy}).",
            nameof(FinanceTransfer), entity.Id,
            [
                new ConfigChange("amount", "Importe", null, Gs(request.Amount)),
                new ConfigChange("from", "Desde", null, p.From.Name),
                new ConfigChange("to", "Hacia", null, p.To.Name),
                new ConfigChange("date", "Fecha", null, p.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))
            ]);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(409, "transfer_conflict", "No se pudo guardar la operación. Actualizá la pantalla y reintentá.");
        }

        var names = await UserNamesAsync([entity.CreatedByUserId], ct);
        return FinanceOpResult<FinanceTransferDto>.Success(ToDto(ctx, entity, names, new Dictionary<Guid, List<string>>()));
    }

    // ─── Anulacion ────────────────────────────────────────────────────────────

    public Task<FinanceOpResult<FinanceTransferDto>> VoidAsync(
        LedgerContext ctx, Guid id, string? reason, Guid companyId, CancellationToken ct) =>
        InTransactionAsync(() => VoidCoreAsync(ctx, id, reason, companyId, ct), ct);

    private async Task<FinanceOpResult<FinanceTransferDto>> VoidCoreAsync(
        LedgerContext ctx, Guid id, string? reason, Guid companyId, CancellationToken ct)
    {
        var entity = await dbContext.FinanceTransfers
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.BuildingId == ctx.BuildingId, ct);
        if (entity is null) return FinanceOpResult<FinanceTransferDto>.Fail(404, NotFoundCode, "La operación no existe en este edificio.");
        if (entity.Status == FinanceTransferStatus.Voided)
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(409, AlreadyVoidedCode, "La operación ya está anulada.");
        }

        var text = (reason ?? string.Empty).Trim();
        if (text.Length < FinanceTransferRules.ReasonMinLength)
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(400, InvalidCode, $"Indicá el motivo de la anulación (al menos {FinanceTransferRules.ReasonMinLength} caracteres).");
        }

        if (text.Length > FinanceTransferRules.ReasonMaxLength)
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(400, InvalidCode, $"El motivo no puede superar los {FinanceTransferRules.ReasonMaxLength} caracteres.");
        }

        var closed = await periodGuard.FindClosedAsync(ctx.BuildingId, entity.TransferDate, ct);
        if (closed is not null)
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(409, FinancePeriodGuard.ClosedCode, FinancePeriodGuard.MessageFor(closed));
        }

        var fiscalYear = FinancePeriods.FiscalYearOf(entity.TransferDate, ctx.FiscalYearStartMonth);
        if (await FiscalYearClosedAsync(ctx.BuildingId, fiscalYear, ct))
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(409, FiscalYearClosedCode,
                $"El ejercicio {fiscalYear} está cerrado: reabrilo desde «Cierre del ejercicio» antes de anular la operación.");
        }

        var mark = await dbContext.BankReconciledMovements.AsNoTracking()
            .Where(x => !x.IsDeleted && x.SourceType == (int)LedgerSourceType.Transfer && x.SourceId == entity.Id)
            .Select(x => new { x.Reconciliation!.StatementDate })
            .FirstOrDefaultAsync(ct);
        if (mark is not null)
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(409, ReconciledCode,
                $"La operación figura en la conciliación bancaria al {mark.StatementDate:dd/MM/yyyy}. Desmarcala (o reabrí esa conciliación) antes de anularla.");
        }

        // Anular saca la plata de la cuenta de destino: el fondo de reserva no puede quedar en negativo por eso.
        var to = ctx.Accounts.FirstOrDefault(a => a.Id == entity.ToAccountId);
        if (to is { Type: FinancialAccountType.ReserveFund })
        {
            var balance = (await BalancesAsync(ctx, ct)).GetValueOrDefault(to.Id);
            if (balance - entity.Amount < 0m)
            {
                return FinanceOpResult<FinanceTransferDto>.Fail(409, FundNegativeCode,
                    $"No se puede anular: el fondo de reserva «{to.Name}» quedaría en negativo (saldo {Gs(balance)}, la operación movió {Gs(entity.Amount)}).");
            }
        }

        var now = DateTime.UtcNow;
        entity.Status = FinanceTransferStatus.Voided;
        entity.VoidedAtUtc = now;
        entity.VoidedByUserId = tenantContext.UserId;
        entity.VoidReason = text;
        entity.UpdatedAtUtc = now;

        var from = ctx.Accounts.FirstOrDefault(a => a.Id == entity.FromAccountId);
        Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Accounting, "Voided",
            $"Operaciones de Finanzas: se anuló {FinanceTransferRules.KindLabel(from?.Type ?? default, to?.Type ?? default).ToLowerInvariant()} de {Gs(entity.Amount)} del {entity.TransferDate:dd/MM/yyyy}. Motivo: {text}",
            nameof(FinanceTransfer), entity.Id,
            [new ConfigChange("status", "Estado", "Activa", "Anulada")]);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return FinanceOpResult<FinanceTransferDto>.Fail(409, "transfer_conflict", "No se pudo anular la operación. Actualizá la pantalla y reintentá.");
        }

        var names = await UserNamesAsync([entity.CreatedByUserId, tenantContext.UserId], ct);
        var labels = await JournalLabelsAsync(ctx.BuildingId, [entity.Id], ct);
        return FinanceOpResult<FinanceTransferDto>.Success(ToDto(ctx, entity, names, labels));
    }

    // ─── Reglas compartidas ───────────────────────────────────────────────────

    // Corre el trabajo en una transaccion serializable (con la estrategia de reintentos del contexto). Si devuelve un error se deshace todo.
    private async Task<FinanceOpResult<T>> InTransactionAsync<T>(Func<Task<FinanceOpResult<T>>> work, CancellationToken ct)
    {
        FinanceOpResult<T>? result = null;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            result = await work();
            if (result.Ok) await transaction.CommitAsync(ct);
            else await transaction.RollbackAsync(ct);
        });
        return result!;
    }

    private sealed record Prepared(
        FinancialAccount From, FinancialAccount To, DateOnly Date, string Reference, string Notes, string Support,
        decimal FromBefore, decimal ToBefore, decimal TotalBalance, bool NegativeAfter);

    private async Task<FinanceOpResult<Prepared>> PrepareAsync(LedgerContext ctx, CreateFinanceTransferRequest request, bool preview, CancellationToken ct)
    {
        var today = FinancePeriods.Today();
        var date = request.TransferDate ?? today;
        var reference = (request.Reference ?? string.Empty).Trim();
        var notes = (request.Notes ?? string.Empty).Trim();
        var support = (request.SupportReference ?? string.Empty).Trim();

        var from = ctx.Accounts.FirstOrDefault(a => a.Id == request.FromAccountId);
        var to = ctx.Accounts.FirstOrDefault(a => a.Id == request.ToAccountId);

        var error = FinanceTransferRules.Validate(from, to, request.Amount, date, ctx.StartDate, today, reference, notes, support);
        if (error is not null) return FinanceOpResult<Prepared>.Fail(400, InvalidCode, error);

        var closed = await periodGuard.FindClosedAsync(ctx.BuildingId, date, ct);
        if (closed is not null) return FinanceOpResult<Prepared>.Fail(409, FinancePeriodGuard.ClosedCode, FinancePeriodGuard.MessageFor(closed));

        var fiscalYear = FinancePeriods.FiscalYearOf(date, ctx.FiscalYearStartMonth);
        if (await FiscalYearClosedAsync(ctx.BuildingId, fiscalYear, ct))
        {
            return FinanceOpResult<Prepared>.Fail(409, FiscalYearClosedCode,
                $"El ejercicio {fiscalYear} está cerrado: reabrilo desde «Cierre del ejercicio» antes de cargar operaciones con esa fecha.");
        }

        var balances = await BalancesAsync(ctx, ct);
        var fromBefore = balances.GetValueOrDefault(from!.Id);
        var toBefore = balances.GetValueOrDefault(to!.Id);
        var negative = fromBefore - request.Amount < 0m;

        // El fondo de reserva no puede quedar en negativo (no se confirma). Banco y caja si, con aviso.
        if (negative && from.Type == FinancialAccountType.ReserveFund)
        {
            return FinanceOpResult<Prepared>.Fail(409, FundNegativeCode,
                $"El fondo de reserva «{from.Name}» no puede quedar en negativo: saldo {Gs(fromBefore)}, importe {Gs(request.Amount)}.");
        }

        if (negative && !preview && !request.ConfirmNegativeBalance)
        {
            return FinanceOpResult<Prepared>.Fail(409, NegativeBalanceCode,
                $"«{from.Name}» quedaría con saldo negativo ({Gs(fromBefore - request.Amount)}). Confirmá que es correcto para registrarla.");
        }

        return FinanceOpResult<Prepared>.Success(new Prepared(
            from, to, date, reference, notes, support, fromBefore, toBefore, balances.Values.Sum(), negative));
    }

    // Saldo actual de cada cuenta (inicial + lo movido hasta hoy, con las transferencias).
    private async Task<Dictionary<Guid, decimal>> BalancesAsync(LedgerContext ctx, CancellationToken ct)
    {
        var buckets = await ledger.GetBucketsAsync(ctx, ctx.StartDate, FinancePeriods.Today(), ct, includeTransfers: true);
        var dto = FinanceReportBuilder.BuildBalances(ctx, buckets, FinancePeriods.Today(), ctx.Resolved.DefaultId);
        return dto.Accounts.ToDictionary(a => a.Id, a => a.Balance);
    }

    // El ejercicio tiene un cierre vigente (no reabierto) en el Libro Diario.
    private async Task<bool> FiscalYearClosedAsync(Guid buildingId, int fiscalYear, CancellationToken ct)
    {
        var closings = await dbContext.JournalEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Kind == JournalEntryKind.Closing && x.FiscalYear == fiscalYear)
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (closings.Count == 0) return false;

        var reversed = (await dbContext.JournalEntries.AsNoTracking()
                .Where(x => x.ReversesEntryId != null && closings.Contains(x.ReversesEntryId.Value))
                .Select(x => x.ReversesEntryId!.Value)
                .ToListAsync(ct))
            .ToHashSet();
        return closings.Any(id => !reversed.Contains(id));
    }

    // «código nombre» de la cuenta del plan de cada cuenta financiera (nulo si no tiene).
    private async Task<Dictionary<Guid, string?>> PlanNamesAsync(LedgerContext ctx, IReadOnlyCollection<Guid> accountIds, CancellationToken ct)
    {
        var roles = await dbContext.LedgerAccountRoles.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && x.Role == LedgerAccountRoleKind.FinancialAccount
                        && x.FinancialAccountId != null && accountIds.Contains(x.FinancialAccountId.Value))
            .ToListAsync(ct);
        return accountIds.ToDictionary(
            id => id,
            id => roles.FirstOrDefault(r => r.FinancialAccountId == id) is { } role && ctx.ById.TryGetValue(role.LedgerCategoryId, out var c)
                ? $"{c.Code} {c.Name}".Trim()
                : null);
    }

    // Asientos vigentes del Libro Diario (asentados y no anulados) de cada transferencia.
    private async Task<Dictionary<Guid, List<string>>> JournalLabelsAsync(Guid buildingId, IReadOnlyCollection<Guid> transferIds, CancellationToken ct)
    {
        if (transferIds.Count == 0) return [];

        var type = (int)LedgerSourceType.Transfer;
        var entries = await dbContext.JournalEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Kind == JournalEntryKind.Auto
                        && x.SourceType == type && x.SourceId != null && transferIds.Contains(x.SourceId.Value))
            .Select(x => new { x.Id, SourceId = x.SourceId!.Value, x.FiscalYear, x.Number })
            .ToListAsync(ct);
        if (entries.Count == 0) return [];

        var ids = entries.Select(e => e.Id).ToList();
        var reversed = (await dbContext.JournalEntries.AsNoTracking()
                .Where(x => x.ReversesEntryId != null && ids.Contains(x.ReversesEntryId.Value))
                .Select(x => x.ReversesEntryId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        return entries
            .Where(e => !reversed.Contains(e.Id))
            .OrderBy(e => e.FiscalYear).ThenBy(e => e.Number)
            .GroupBy(e => e.SourceId)
            .ToDictionary(g => g.Key, g => g.Select(e => JournalPostingService.LabelOf(e.FiscalYear, e.Number)).ToList());
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        return await dbContext.ApplicationUsers.AsNoTracking()
            .Where(x => list.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    private static FinanceTransferDto ToDto(
        LedgerContext ctx, FinanceTransfer x, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, List<string>> labels)
    {
        var from = ctx.Accounts.FirstOrDefault(a => a.Id == x.FromAccountId);
        var to = ctx.Accounts.FirstOrDefault(a => a.Id == x.ToAccountId);
        return new FinanceTransferDto
        {
            Id = x.Id,
            TransferDate = x.TransferDate,
            FromAccountId = x.FromAccountId,
            FromAccountName = from?.Name ?? string.Empty,
            ToAccountId = x.ToAccountId,
            ToAccountName = to?.Name ?? string.Empty,
            Kind = from is null || to is null ? "Transferencia entre cuentas" : FinanceTransferRules.KindLabel(from.Type, to.Type),
            Amount = x.Amount,
            Reference = x.Reference,
            Notes = x.Notes,
            SupportReference = x.SupportReference,
            Status = x.Status,
            CreatedAtUtc = x.CreatedAtUtc,
            CreatedByName = names.GetValueOrDefault(x.CreatedByUserId) ?? string.Empty,
            VoidedAtUtc = x.VoidedAtUtc,
            VoidedByName = x.VoidedByUserId.HasValue ? names.GetValueOrDefault(x.VoidedByUserId.Value) ?? string.Empty : string.Empty,
            VoidReason = x.VoidReason,
            JournalLabels = labels.GetValueOrDefault(x.Id) ?? []
        };
    }

    private static string Gs(decimal amount) => $"Gs. {amount.ToString("N0", CultureInfo.GetCultureInfo("es-PY"))}";
}
