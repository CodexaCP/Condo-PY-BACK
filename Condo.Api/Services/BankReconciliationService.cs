using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Resultado de una operacion de conciliacion: el valor, o el codigo y mensaje del error con su estado HTTP.</summary>
public sealed class ReconResult<T>
{
    public T? Value { get; init; }
    public int Status { get; init; } = 200;
    public string? Code { get; init; }
    public string? Message { get; init; }
    public bool Ok => Message is null;

    public static ReconResult<T> Success(T value) => new() { Value = value };
    public static ReconResult<T> Fail(int status, string code, string message) => new() { Status = status, Code = code, Message = message };
}

/// <summary>
/// Conciliacion bancaria manual de una cuenta bancaria del edificio. El libro es virtual (se arma de cobros, gastos e ingresos), asi que lo
/// conciliado se guarda como marcas sobre su origen. Una conciliacion abierta se arma tildando movimientos y comparando el saldo del extracto
/// con el saldo conciliado del libro; se cierra solo con diferencia cero. Todo cambio queda en el historial del Centro de configuracion.
/// </summary>
public sealed class BankReconciliationService(
    ICondoDbContext dbContext, FinanceLedgerService ledger, ITenantContext tenantContext)
{
    public const int MaxItemsPerRequest = 500;
    public const int MaxPendingRows = 1000;
    private const decimal MaxBalance = 9_999_999_999_999m;
    private const int NotesMaxLength = 500;

    private ConfigAuditWriter Audit => new(dbContext, tenantContext);

    // ─── Consultas ────────────────────────────────────────────────────────────

    public async Task<ReconciliationOverviewDto> OverviewAsync(LedgerContext ctx, bool canEdit, CancellationToken ct)
    {
        var today = FinancePeriods.Today();
        var buckets = await ledger.GetBucketsAsync(ctx, ctx.StartDate, today, ct);
        var balances = FinanceReportBuilder.BuildBalances(ctx, buckets, today, ctx.Resolved.DefaultId).Accounts.ToDictionary(a => a.Id);

        var reconciliations = await dbContext.BankReconciliations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId)
            .ToListAsync(ct);

        var accounts = ctx.Accounts.Where(a => a.Type == FinancialAccountType.Bank).OrderBy(a => a.Name).Select(a =>
        {
            var own = reconciliations.Where(r => r.AccountId == a.Id).ToList();
            var lastCompleted = own.Where(r => r.Status == BankReconciliationStatus.Completed).OrderByDescending(r => r.StatementDate).FirstOrDefault();
            var open = own.FirstOrDefault(r => r.Status == BankReconciliationStatus.Open);
            return new ReconciliationAccountDto
            {
                AccountId = a.Id,
                AccountName = a.Name,
                IsActive = a.IsActive,
                BookBalance = balances.TryGetValue(a.Id, out var b) ? b.Balance : a.OpeningBalance,
                LastCompletedDate = lastCompleted?.StatementDate,
                LastStatementBalance = lastCompleted?.StatementBalance,
                OpenReconciliationId = open?.Id,
                OpenStatementDate = open?.StatementDate
            };
        }).ToList();

        return new ReconciliationOverviewDto { BuildingId = ctx.BuildingId, CanEdit = canEdit, Accounts = accounts };
    }

    public async Task<List<ReconciliationSummaryDto>> HistoryAsync(Guid buildingId, Guid? accountId, CancellationToken ct)
    {
        var query = dbContext.BankReconciliations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == buildingId && x.Status == BankReconciliationStatus.Completed);
        if (accountId.HasValue) query = query.Where(x => x.AccountId == accountId.Value);

        var rows = await query
            .OrderByDescending(x => x.StatementDate)
            .Select(x => new { Entity = x, AccountName = x.Account != null ? x.Account.Name : string.Empty })
            .Take(100)
            .ToListAsync(ct);

        var ids = rows.Select(x => x.Entity.Id).ToList();
        var counts = (await dbContext.BankReconciledMovements.AsNoTracking()
                .Where(x => !x.IsDeleted && ids.Contains(x.ReconciliationId))
                .Select(x => x.ReconciliationId)
                .ToListAsync(ct))
            .GroupBy(x => x)
            .ToDictionary(g => g.Key, g => g.Count());
        var names = await UserNamesAsync(rows.Where(x => x.Entity.CompletedByUserId.HasValue).Select(x => x.Entity.CompletedByUserId!.Value), ct);

        return rows.Select(x => ToSummary(x.Entity, x.AccountName, counts.GetValueOrDefault(x.Entity.Id), names)).ToList();
    }

    public async Task<ReconResult<ReconciliationWorkspaceDto>> GetAsync(LedgerContext ctx, Guid id, bool canEdit, CancellationToken ct)
    {
        var reconciliation = await FindAsync(ctx.BuildingId, id, tracking: false, ct);
        if (reconciliation is null) return NotFoundResult<ReconciliationWorkspaceDto>();
        return ReconResult<ReconciliationWorkspaceDto>.Success(await BuildWorkspaceAsync(ctx, reconciliation, canEdit, ct));
    }

    // ─── Alta, edicion y descarte ─────────────────────────────────────────────

    public async Task<ReconResult<ReconciliationWorkspaceDto>> StartAsync(LedgerContext ctx, StartReconciliationRequest request, Guid companyId, CancellationToken ct)
    {
        var account = ctx.Accounts.FirstOrDefault(a => a.Id == request.AccountId);
        if (account is null) return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "account_not_found", "La cuenta no existe en este edificio.");
        if (account.Type != FinancialAccountType.Bank)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "account_not_bank", "Solo se concilian las cuentas bancarias.");
        }

        var dateError = ValidateStatement(ctx, request.StatementDate, request.StatementBalance, request.Notes);
        if (dateError is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "invalid_statement", dateError);

        var existing = await dbContext.BankReconciliations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.AccountId == account.Id)
            .ToListAsync(ct);
        var open = existing.FirstOrDefault(x => x.Status == BankReconciliationStatus.Open);
        if (open is not null)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "reconciliation_open_exists",
                $"La cuenta ya tiene una conciliación abierta (al {open.StatementDate:dd/MM/yyyy}). Seguila o descartala antes de empezar otra.");
        }

        var lastCompleted = existing.Where(x => x.Status == BankReconciliationStatus.Completed).OrderByDescending(x => x.StatementDate).FirstOrDefault();
        if (lastCompleted is not null && request.StatementDate <= lastCompleted.StatementDate)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "statement_date_before_last",
                $"La fecha de corte tiene que ser posterior a la de la última conciliación cerrada ({lastCompleted.StatementDate:dd/MM/yyyy}).");
        }

        var entity = new BankReconciliation
        {
            CompanyId = companyId,
            BuildingId = ctx.BuildingId,
            AccountId = account.Id,
            StatementDate = request.StatementDate,
            StatementBalance = decimal.Round(request.StatementBalance, 2),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Status = BankReconciliationStatus.Open,
            CreatedByUserId = tenantContext.UserId
        };
        dbContext.BankReconciliations.Add(entity);
        Audit.Add(companyId, ctx.BuildingId, ConfigSectionKeys.Reconciliation, "Started",
            $"Se empezó la conciliación de «{account.Name}» al {entity.StatementDate:dd/MM/yyyy} (saldo del extracto {entity.StatementBalance:N0}).",
            "BankReconciliation", entity.Id);

        var saved = await SaveAsync(ct);
        if (saved is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "reconciliation_open_exists", saved);

        return ReconResult<ReconciliationWorkspaceDto>.Success(await BuildWorkspaceAsync(ctx, entity, canEdit: true, ct));
    }

    public async Task<ReconResult<ReconciliationWorkspaceDto>> UpdateAsync(LedgerContext ctx, Guid id, UpdateReconciliationRequest request, CancellationToken ct)
    {
        var entity = await FindAsync(ctx.BuildingId, id, tracking: true, ct);
        if (entity is null) return NotFoundResult<ReconciliationWorkspaceDto>();
        if (entity.Status != BankReconciliationStatus.Open) return NotOpen<ReconciliationWorkspaceDto>();

        var error = ValidateStatement(ctx, request.StatementDate, request.StatementBalance, request.Notes);
        if (error is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "invalid_statement", error);

        var lastCompleted = await dbContext.BankReconciliations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.AccountId == entity.AccountId && x.Status == BankReconciliationStatus.Completed && x.Id != entity.Id)
            .OrderByDescending(x => x.StatementDate)
            .FirstOrDefaultAsync(ct);
        if (lastCompleted is not null && request.StatementDate <= lastCompleted.StatementDate)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "statement_date_before_last",
                $"La fecha de corte tiene que ser posterior a la de la última conciliación cerrada ({lastCompleted.StatementDate:dd/MM/yyyy}).");
        }

        // No se puede correr el corte antes de un movimiento que esta conciliacion ya marco.
        var latestMark = await dbContext.BankReconciledMovements.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ReconciliationId == entity.Id)
            .OrderByDescending(x => x.Date)
            .Select(x => (DateOnly?)x.Date)
            .FirstOrDefaultAsync(ct);
        if (latestMark.HasValue && request.StatementDate < latestMark.Value)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "statement_date_before_mark",
                $"Hay movimientos marcados con fecha {latestMark.Value:dd/MM/yyyy}: la fecha de corte no puede ser anterior. Desmarcalos primero.");
        }

        var changes = new List<ConfigChange>();
        var newBalance = decimal.Round(request.StatementBalance, 2);
        var newNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (entity.StatementDate != request.StatementDate)
            changes.Add(new ConfigChange("statementDate", "Fecha de corte", entity.StatementDate.ToString("dd/MM/yyyy"), request.StatementDate.ToString("dd/MM/yyyy")));
        if (entity.StatementBalance != newBalance)
            changes.Add(new ConfigChange("statementBalance", "Saldo del extracto", entity.StatementBalance.ToString("N0"), newBalance.ToString("N0")));
        if ((entity.Notes ?? string.Empty) != (newNotes ?? string.Empty))
            changes.Add(new ConfigChange("notes", "Notas", entity.Notes, newNotes));

        if (changes.Count > 0)
        {
            Audit.Add(entity.CompanyId, ctx.BuildingId, ConfigSectionKeys.Reconciliation, "Updated",
                $"Conciliación al {entity.StatementDate:dd/MM/yyyy}: cambió {string.Join(", ", changes.Select(c => c.Label))}.", "BankReconciliation", entity.Id, changes);
            entity.StatementDate = request.StatementDate;
            entity.StatementBalance = newBalance;
            entity.Notes = newNotes;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(ct);
        }

        return ReconResult<ReconciliationWorkspaceDto>.Success(await BuildWorkspaceAsync(ctx, entity, canEdit: true, ct));
    }

    /// <summary>Descarta una conciliacion abierta: deshace sus marcas (si alguna es de un mes cerrado, no se puede).</summary>
    public async Task<ReconResult<bool>> DiscardAsync(LedgerContext ctx, Guid id, CancellationToken ct)
    {
        var entity = await FindAsync(ctx.BuildingId, id, tracking: true, ct);
        if (entity is null) return NotFoundResult<bool>();
        if (entity.Status != BankReconciliationStatus.Open) return NotOpen<bool>();

        var marks = await dbContext.BankReconciledMovements
            .Where(x => !x.IsDeleted && x.ReconciliationId == entity.Id)
            .ToListAsync(ct);
        var closed = await new FinancePeriodGuard(dbContext).FindClosedAsync(ctx.BuildingId, marks.Select(m => m.Date), ct);
        if (closed is not null) return ReconResult<bool>.Fail(409, FinancePeriodGuard.ClosedCode, FinancePeriodGuard.MessageFor(closed));

        var now = DateTime.UtcNow;
        foreach (var mark in marks) Unmark(mark, now);
        entity.IsDeleted = true;
        entity.UpdatedAtUtc = now;
        Audit.Add(entity.CompanyId, ctx.BuildingId, ConfigSectionKeys.Reconciliation, "Discarded",
            $"Se descartó la conciliación al {entity.StatementDate:dd/MM/yyyy} ({marks.Count} movimientos desmarcados).", "BankReconciliation", entity.Id);
        await dbContext.SaveChangesAsync(ct);
        return ReconResult<bool>.Success(true);
    }

    // ─── Marcar y desmarcar ───────────────────────────────────────────────────

    public async Task<ReconResult<ReconciliationWorkspaceDto>> MarkAsync(
        LedgerContext ctx, Guid id, IReadOnlyList<ReconciliationItemRef> items, CancellationToken ct)
    {
        var entity = await FindAsync(ctx.BuildingId, id, tracking: true, ct);
        if (entity is null) return NotFoundResult<ReconciliationWorkspaceDto>();
        if (entity.Status != BankReconciliationStatus.Open) return NotOpen<ReconciliationWorkspaceDto>();

        var error = ValidateItems(items);
        if (error is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "invalid_items", error);

        var movements = BankReconciliationCalculator.GroupRows(await AccountRowsAsync(ctx, entity.AccountId, entity.StatementDate, ct), entity.StatementDate)
            .ToDictionary(m => ((int)m.SourceType, m.SourceId));
        var already = (await dbContext.BankReconciledMovements.AsNoTracking()
                .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && x.AccountId == entity.AccountId)
                .Select(x => new { x.SourceType, x.SourceId })
                .ToListAsync(ct))
            .Select(x => (x.SourceType, x.SourceId))
            .ToHashSet();

        var now = DateTime.UtcNow;
        var added = new List<BankReconciledMovement>();
        foreach (var item in items)
        {
            var key = ((int)item.SourceType, item.SourceId);
            if (!movements.TryGetValue(key, out var movement))
            {
                return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "movement_not_found",
                    "Hay un movimiento que no figura en esta cuenta hasta la fecha de corte (puede haberse modificado o eliminado). Actualizá la pantalla.");
            }

            if (already.Contains(key))
            {
                return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "movement_already_reconciled",
                    $"El movimiento «{movement.Description}» ya está conciliado.");
            }

            added.Add(new BankReconciledMovement
            {
                CompanyId = entity.CompanyId,
                BuildingId = ctx.BuildingId,
                AccountId = entity.AccountId,
                ReconciliationId = entity.Id,
                SourceType = key.Item1,
                SourceId = item.SourceId,
                Date = movement.Date,
                Amount = movement.Amount,
                Description = movement.Description.Length > 300 ? movement.Description[..300] : movement.Description,
                MarkedByUserId = tenantContext.UserId,
                MarkedAtUtc = now
            });
        }

        dbContext.BankReconciledMovements.AddRange(added);
        Audit.Add(entity.CompanyId, ctx.BuildingId, ConfigSectionKeys.Reconciliation, "Marked",
            $"Conciliación al {entity.StatementDate:dd/MM/yyyy}: se marcaron {added.Count} movimientos por {added.Sum(a => a.Amount):N0}.",
            "BankReconciliation", entity.Id);

        var saved = await SaveAsync(ct);
        if (saved is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "movement_already_reconciled", saved);

        return ReconResult<ReconciliationWorkspaceDto>.Success(await BuildWorkspaceAsync(ctx, entity, canEdit: true, ct));
    }

    public async Task<ReconResult<ReconciliationWorkspaceDto>> UnmarkAsync(
        LedgerContext ctx, Guid id, IReadOnlyList<ReconciliationItemRef> items, CancellationToken ct)
    {
        var entity = await FindAsync(ctx.BuildingId, id, tracking: true, ct);
        if (entity is null) return NotFoundResult<ReconciliationWorkspaceDto>();
        if (entity.Status != BankReconciliationStatus.Open) return NotOpen<ReconciliationWorkspaceDto>();

        var error = ValidateItems(items);
        if (error is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "invalid_items", error);

        var marks = await dbContext.BankReconciledMovements
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && x.AccountId == entity.AccountId)
            .ToListAsync(ct);

        var toUnmark = new List<BankReconciledMovement>();
        foreach (var item in items)
        {
            var mark = marks.FirstOrDefault(m => m.SourceType == (int)item.SourceType && m.SourceId == item.SourceId);
            if (mark is null)
            {
                return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "movement_not_marked", "Hay un movimiento que no está marcado como conciliado.");
            }

            // Lo conciliado en una conciliacion ya cerrada queda firme: se desmarca reabriendola.
            if (mark.ReconciliationId != entity.Id)
            {
                return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "mark_in_other_reconciliation",
                    $"«{mark.Description}» se concilió en otra conciliación: para desmarcarlo hay que reabrirla.");
            }

            toUnmark.Add(mark);
        }

        // Cierre de periodo: lo conciliado de un mes cerrado no se desmarca.
        var closed = await new FinancePeriodGuard(dbContext).FindClosedAsync(ctx.BuildingId, toUnmark.Select(m => m.Date), ct);
        if (closed is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(409, FinancePeriodGuard.ClosedCode, FinancePeriodGuard.MessageFor(closed));

        var now = DateTime.UtcNow;
        foreach (var mark in toUnmark) Unmark(mark, now);
        Audit.Add(entity.CompanyId, ctx.BuildingId, ConfigSectionKeys.Reconciliation, "Unmarked",
            $"Conciliación al {entity.StatementDate:dd/MM/yyyy}: se desmarcaron {toUnmark.Count} movimientos por {toUnmark.Sum(m => m.Amount):N0}.",
            "BankReconciliation", entity.Id);
        await dbContext.SaveChangesAsync(ct);

        return ReconResult<ReconciliationWorkspaceDto>.Success(await BuildWorkspaceAsync(ctx, entity, canEdit: true, ct));
    }

    // ─── Cerrar y reabrir ─────────────────────────────────────────────────────

    public async Task<ReconResult<ReconciliationWorkspaceDto>> CompleteAsync(LedgerContext ctx, Guid id, CancellationToken ct)
    {
        var entity = await FindAsync(ctx.BuildingId, id, tracking: true, ct);
        if (entity is null) return NotFoundResult<ReconciliationWorkspaceDto>();
        if (entity.Status != BankReconciliationStatus.Open) return NotOpen<ReconciliationWorkspaceDto>();

        var workspace = await BuildWorkspaceAsync(ctx, entity, canEdit: true, ct);
        if (workspace.MissingCount > 0)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "reconciliation_missing_movements",
                $"Hay {workspace.MissingCount} movimientos marcados que ya no figuran en el libro: desmarcalos antes de cerrar.");
        }

        if (workspace.Difference != 0m)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "reconciliation_difference",
                $"La conciliación no cierra: el saldo del extracto ({workspace.StatementBalance:N0}) difiere del saldo conciliado del libro ({workspace.ReconciledBalance:N0}) en {workspace.Difference:N0}. " +
                "Marcá los movimientos que faltan o registrá en el sistema lo que el banco ya asentó (comisiones, intereses).");
        }

        // Los marcados cuyo importe cambio quedan con su importe vigente como nueva foto.
        var marks = await dbContext.BankReconciledMovements
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && x.AccountId == entity.AccountId)
            .ToListAsync(ct);
        foreach (var dto in workspace.Marked.Where(m => m.State == "Changed"))
        {
            var mark = marks.First(m => m.SourceType == (int)dto.SourceType && m.SourceId == dto.SourceId);
            mark.Amount = dto.Amount;
            mark.UpdatedAtUtc = DateTime.UtcNow;
        }

        entity.Status = BankReconciliationStatus.Completed;
        entity.CompletedAtUtc = DateTime.UtcNow;
        entity.CompletedByUserId = tenantContext.UserId;
        entity.ReconciledBalanceAtCompletion = workspace.ReconciledBalance;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        Audit.Add(entity.CompanyId, ctx.BuildingId, ConfigSectionKeys.Reconciliation, "Completed",
            $"Se cerró la conciliación al {entity.StatementDate:dd/MM/yyyy}: saldo {workspace.ReconciledBalance:N0}, {workspace.Marked.Count(m => m.InThisReconciliation)} movimientos marcados.",
            "BankReconciliation", entity.Id);
        await dbContext.SaveChangesAsync(ct);

        return ReconResult<ReconciliationWorkspaceDto>.Success(await BuildWorkspaceAsync(ctx, entity, canEdit: true, ct));
    }

    /// <summary>Reabre una conciliacion cerrada, con motivo. Solo la ultima cerrada de su cuenta y si la cuenta no tiene otra abierta.</summary>
    public async Task<ReconResult<ReconciliationWorkspaceDto>> ReopenAsync(LedgerContext ctx, Guid id, string reason, CancellationToken ct)
    {
        var entity = await FindAsync(ctx.BuildingId, id, tracking: true, ct);
        if (entity is null) return NotFoundResult<ReconciliationWorkspaceDto>();
        if (entity.Status != BankReconciliationStatus.Completed)
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "reconciliation_not_completed", "Solo se reabre una conciliación cerrada.");
        }

        var text = reason?.Trim() ?? string.Empty;
        if (text.Length == 0) return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "reason_required", "El motivo de la reapertura es obligatorio.");
        if (text.Length > NotesMaxLength) return ReconResult<ReconciliationWorkspaceDto>.Fail(400, "reason_too_long", $"El motivo no puede superar los {NotesMaxLength} caracteres.");

        var siblings = await dbContext.BankReconciliations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.AccountId == entity.AccountId && x.Id != entity.Id)
            .ToListAsync(ct);
        if (siblings.Any(x => x.Status == BankReconciliationStatus.Open))
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "reconciliation_open_exists", "La cuenta tiene otra conciliación abierta: cerrala o descartala antes.");
        }

        if (siblings.Any(x => x.Status == BankReconciliationStatus.Completed && x.StatementDate > entity.StatementDate))
        {
            return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "not_last_reconciliation",
                "Solo se puede reabrir la última conciliación cerrada de la cuenta: hay otra posterior.");
        }

        entity.Status = BankReconciliationStatus.Open;
        entity.CompletedAtUtc = null;
        entity.CompletedByUserId = null;
        entity.ReconciledBalanceAtCompletion = null;
        entity.ReopenedAtUtc = DateTime.UtcNow;
        entity.ReopenedByUserId = tenantContext.UserId;
        entity.ReopenReason = text;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        Audit.Add(entity.CompanyId, ctx.BuildingId, ConfigSectionKeys.Reconciliation, "Reopened",
            $"Se reabrió la conciliación al {entity.StatementDate:dd/MM/yyyy}. Motivo: {text}", "BankReconciliation", entity.Id);

        var saved = await SaveAsync(ct);
        if (saved is not null) return ReconResult<ReconciliationWorkspaceDto>.Fail(409, "reconciliation_open_exists", saved);

        return ReconResult<ReconciliationWorkspaceDto>.Success(await BuildWorkspaceAsync(ctx, entity, canEdit: true, ct));
    }

    // ─── Armado de la pantalla ────────────────────────────────────────────────

    private async Task<ReconciliationWorkspaceDto> BuildWorkspaceAsync(LedgerContext ctx, BankReconciliation entity, bool canEdit, CancellationToken ct)
    {
        var account = ctx.Accounts.First(a => a.Id == entity.AccountId);
        var rows = await AccountRowsAsync(ctx, entity.AccountId, entity.StatementDate, ct);
        var movements = BankReconciliationCalculator.GroupRows(rows, entity.StatementDate);

        var markEntities = await dbContext.BankReconciledMovements.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == ctx.BuildingId && x.AccountId == entity.AccountId && x.Date <= entity.StatementDate)
            .ToListAsync(ct);
        var names = await UserNamesAsync(markEntities.Select(m => m.MarkedByUserId), ct);
        var marks = markEntities
            .Select(m => new ReconciliationMark(m.Id, m.ReconciliationId, m.SourceType, m.SourceId, m.Date, m.Amount, m.Description, m.MarkedByUserId, m.MarkedAtUtc))
            .ToList();

        var calc = BankReconciliationCalculator.Calculate(account.OpeningBalance, entity.StatementBalance, movements, marks, entity.Id, names);

        // Saldo del libro a la fecha de corte: todo lo que el libro asento en la cuenta hasta ahi.
        var bookBalance = account.OpeningBalance + movements.Sum(m => m.Amount);

        var alerts = new List<string>();
        if (calc.MissingCount > 0) alerts.Add($"{calc.MissingCount} movimientos marcados ya no figuran en el libro a esa fecha (se borraron, se revirtieron o cambiaron de cuenta o fecha). No cuentan y hay que desmarcarlos para cerrar.");
        if (calc.ChangedCount > 0) alerts.Add($"{calc.ChangedCount} movimientos marcados cambiaron de importe después de marcarlos: cuentan por su importe actual. Revisalos.");

        return new ReconciliationWorkspaceDto
        {
            Id = entity.Id,
            BuildingId = ctx.BuildingId,
            AccountId = entity.AccountId,
            AccountName = account.Name,
            StatementDate = entity.StatementDate,
            StatementBalance = entity.StatementBalance,
            Notes = entity.Notes,
            Status = entity.Status,
            CanEdit = canEdit && entity.Status == BankReconciliationStatus.Open,
            OpeningBalance = account.OpeningBalance,
            BookBalance = bookBalance,
            ReconciledBalance = calc.ReconciledBalance,
            Difference = calc.Difference,
            IsBalanced = calc.Difference == 0m && calc.MissingCount == 0,
            PendingIn = calc.PendingIn,
            PendingOut = calc.PendingOut,
            PendingCount = calc.Pending.Count,
            PendingTruncated = calc.Pending.Count > MaxPendingRows,
            ChangedCount = calc.ChangedCount,
            MissingCount = calc.MissingCount,
            Alerts = alerts,
            Marked = calc.Marked,
            Pending = calc.Pending.Take(MaxPendingRows).ToList()
        };
    }

    // Los renglones del libro de la cuenta desde la fecha de arranque hasta la fecha de corte.
    private async Task<List<LedgerRow>> AccountRowsAsync(LedgerContext ctx, Guid accountId, DateOnly to, CancellationToken ct) =>
        (await ledger.GetRowsAsync(ctx, ctx.StartDate, to, ct)).Where(r => r.AccountId == accountId).ToList();

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<BankReconciliation?> FindAsync(Guid buildingId, Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<BankReconciliation> query = dbContext.BankReconciliations.Where(x => !x.IsDeleted && x.Id == id && x.BuildingId == buildingId);
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct);
    }

    private static string? ValidateStatement(LedgerContext ctx, DateOnly statementDate, decimal balance, string? notes)
    {
        if (statementDate < ctx.StartDate) return $"La fecha de corte no puede ser anterior a la fecha de arranque de Finanzas ({ctx.StartDate:dd/MM/yyyy}).";
        if (statementDate > FinancePeriods.Today()) return "La fecha de corte no puede ser futura.";
        if (balance is < -MaxBalance or > MaxBalance) return "El saldo del extracto no es válido.";
        if (notes is { Length: > NotesMaxLength }) return $"Las notas no pueden superar los {NotesMaxLength} caracteres.";
        return null;
    }

    private static string? ValidateItems(IReadOnlyList<ReconciliationItemRef> items)
    {
        if (items.Count == 0) return "No se indicó ningún movimiento.";
        if (items.Count > MaxItemsPerRequest) return $"Se pueden procesar hasta {MaxItemsPerRequest} movimientos por vez.";
        if (items.Any(i => !Enum.IsDefined(i.SourceType) || i.SourceId == Guid.Empty)) return "Hay un movimiento que no es válido.";
        if (items.GroupBy(i => (i.SourceType, i.SourceId)).Any(g => g.Count() > 1)) return "Hay un movimiento repetido.";
        return null;
    }

    // Desmarcar no borra: la marca queda deshecha con quien y cuando.
    private void Unmark(BankReconciledMovement mark, DateTime now)
    {
        mark.UnmarkedAtUtc = now;
        mark.UnmarkedByUserId = tenantContext.UserId;
        mark.IsDeleted = true;
        mark.UpdatedAtUtc = now;
    }

    private async Task<string?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            return "Otra persona hizo un cambio al mismo tiempo (una conciliación abierta o un movimiento ya conciliado). Actualizá la pantalla y reintentá.";
        }
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await dbContext.ApplicationUsers.AsNoTracking()
            .Where(x => list.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    private static ReconciliationSummaryDto ToSummary(BankReconciliation x, string accountName, int markedCount, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = x.Id,
        AccountId = x.AccountId,
        AccountName = accountName,
        StatementDate = x.StatementDate,
        StatementBalance = x.StatementBalance,
        Status = x.Status,
        CompletedAtUtc = x.CompletedAtUtc,
        CompletedByName = x.CompletedByUserId.HasValue ? names.GetValueOrDefault(x.CompletedByUserId.Value) : null,
        ReconciledBalanceAtCompletion = x.ReconciledBalanceAtCompletion,
        MarkedCount = markedCount,
        Notes = x.Notes
    };

    private static ReconResult<T> NotFoundResult<T>() => ReconResult<T>.Fail(404, "reconciliation_not_found", "La conciliación no existe.");

    private static ReconResult<T> NotOpen<T>() =>
        ReconResult<T>.Fail(409, "reconciliation_not_open", "La conciliación está cerrada: reabrila (con motivo) para cambiarla.");
}
