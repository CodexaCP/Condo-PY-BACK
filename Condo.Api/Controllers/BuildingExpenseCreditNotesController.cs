using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

// Notas de credito que el PROVEEDOR emite sobre un gasto del edificio (no confundir con las notas de credito al propietario, en
// CreditNotesController). Nunca se toca un comprobante ya emitido a un propietario.
//
//  - Periodo en borrador: la nota baja el monto del gasto (BuildingExpense.Amount queda neto, OriginalAmount guarda lo facturado) y la
//    liquidacion se calcula con el neto.
//  - Periodo cerrado: se rechaza; primero se anula la liquidacion.
//  - Periodo publicado: el gasto ya se repartio y cobro. La nota se prorratea entre las unidades segun lo que realmente se les cobro
//    de ese gasto y la parte de cada una se acredita como saldo a favor de su propietario principal (se consume como cualquier otro
//    saldo, sin restriccion por edificio). Los aportes de fondo de reserva y extraordinario no se recalculan.
[ApiController]
[Authorize]
[Route("api/building-expenses")]
public class BuildingExpenseCreditNotesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext,
    OwnerCreditService credits,
    PushDispatcher push) : ControllerBase
{
    private const int NumeroMaxLength = 50;
    private const int TimbradoMaxLength = 20;
    private const int ReasonMaxLength = 500;
    private const int DocumentUrlMaxLength = 500;

    private const string ClosedMessage =
        "El período está cerrado. Anulá la liquidación (Gastos y cargos › Liquidación › Anular) y registrá la nota de crédito con el período en borrador.";

    [HttpGet("{expenseId:guid}/credit-notes")]
    public async Task<ActionResult<IReadOnlyList<BuildingExpenseCreditNoteDto>>> GetByExpense(Guid expenseId, CancellationToken cancellationToken)
    {
        var expense = await dbContext.BuildingExpenses.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expenseId, cancellationToken);
        if (expense is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(expense.BuildingId, cancellationToken)) return Forbid();

        var notes = await dbContext.BuildingExpenseCreditNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingExpenseId == expenseId)
            .OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var allocations = await LoadAllocationDtosAsync(notes.Select(x => x.Id).ToList(), cancellationToken);

        return Ok(notes.Select(n => ToDto(n, allocations.GetValueOrDefault(n.Id))).ToList());
    }

    // Simula la nota con ese monto sin guardar nada: en borrador, como quedaria el gasto; en periodo publicado, el reparto por unidad.
    [HttpPost("{expenseId:guid}/credit-notes/preview")]
    public async Task<ActionResult<BuildingExpenseCreditNotePreviewDto>> Preview(
        Guid expenseId, [FromBody] BuildingExpenseCreditNotePreviewRequest request, CancellationToken cancellationToken)
    {
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m) return BadRequest("El monto de la nota de crédito debe ser mayor que cero.");

        var expense = await dbContext.BuildingExpenses.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expenseId, cancellationToken);
        if (expense is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(expense.BuildingId, cancellationToken)) return Forbid();

        var period = await dbContext.ExpensePeriods.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expense.ExpensePeriodId, cancellationToken);
        if (period is null) return BadRequest("El periodo de expensas asociado no existe.");

        if (period.Status == ExpensePeriodStatus.Closed) return BadRequest(ClosedMessage);

        if (period.Status == ExpensePeriodStatus.Draft)
        {
            if (amount >= expense.Amount) return BadRequest(DraftCapMessage(expense.Amount));
            return Ok(new BuildingExpenseCreditNotePreviewDto
            {
                Mode = BuildingExpenseCreditNoteMode.Netted,
                Amount = amount,
                NewExpenseAmount = expense.Amount - amount,
                MaxAmount = expense.Amount - 1m
            });
        }

        var creditedSoFar = await CreditedSoFarAsync(expense.Id, cancellationToken);
        var max = expense.Amount - creditedSoFar;
        if (amount > max) return BadRequest(PublishedCapMessage(max));

        var plan = await BuildPlanAsync(expense, amount, cancellationToken);
        var preview = new BuildingExpenseCreditNotePreviewDto
        {
            Mode = BuildingExpenseCreditNoteMode.Credited,
            Amount = amount,
            CreditedSoFar = creditedSoFar,
            MaxAmount = max,
            ChargedTotal = plan.ChargedTotal,
            UnitsWithoutOwner = plan.UnitsWithoutOwner,
            Rows = plan.Rows.Select(r => new BuildingExpenseCreditNotePreviewRowDto
            {
                UnitId = r.UnitId, UnitCode = r.UnitCode, OwnerId = r.OwnerId, OwnerName = r.OwnerName,
                ChargeAmount = r.ChargeAmount, CreditAmount = r.CreditAmount
            }).ToList()
        };

        if (plan.Error is not null) preview.Message = plan.Error;
        else if (plan.Rows.Count == 0) preview.Message = NoDistributionMessage(expense);
        else if (plan.UnitsWithoutOwner.Count > 0) preview.Message = UnitsWithoutOwnerMessage(plan.UnitsWithoutOwner);

        return Ok(preview);
    }

    [HttpPost("{expenseId:guid}/credit-notes")]
    public async Task<ActionResult<BuildingExpenseCreditNoteResultDto>> Create(
        Guid expenseId, [FromBody] CreateBuildingExpenseCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var numero = (request.Numero ?? string.Empty).Trim();
        var timbrado = string.IsNullOrWhiteSpace(request.Timbrado) ? null : request.Timbrado.Trim();
        var reason = (request.Reason ?? string.Empty).Trim();
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);

        if (numero.Length == 0 || numero.Length > NumeroMaxLength)
            return BadRequest($"El número de la nota de crédito es obligatorio (máximo {NumeroMaxLength} caracteres).");
        if (timbrado is not null && timbrado.Length > TimbradoMaxLength)
            return BadRequest($"El timbrado no puede superar los {TimbradoMaxLength} caracteres.");
        if (reason.Length == 0 || reason.Length > ReasonMaxLength)
            return BadRequest($"El motivo es obligatorio (máximo {ReasonMaxLength} caracteres).");
        if (amount <= 0m)
            return BadRequest("El monto de la nota de crédito debe ser mayor que cero.");
        if (request.IssueDate < new DateOnly(2000, 1, 1) || request.IssueDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest("La fecha de la nota de crédito no es válida.");

        // El documento que envio el proveedor es el respaldo de la nota: es obligatorio.
        var documentUrl = (request.DocumentUrl ?? string.Empty).Trim();
        if (documentUrl.Length == 0)
            return BadRequest("Adjuntá el documento de la nota de crédito que envió el proveedor.");
        if (!IsValidUploadUrl(documentUrl))
            return BadRequest("El archivo de la nota de crédito no es válido. Subilo de nuevo.");

        var expense = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expenseId, cancellationToken);
        if (expense is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(expense.BuildingId, cancellationToken)) return Forbid();

        var period = await dbContext.ExpensePeriods.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expense.ExpensePeriodId, cancellationToken);
        if (period is null) return BadRequest("El periodo de expensas asociado no existe.");

        if (period.Status == ExpensePeriodStatus.Closed) return BadRequest(ClosedMessage);

        // Una misma nota (proveedor + numero, y timbrado si se carga) no se registra dos veces: el control mira toda la empresa (la
        // nota puede haberse cargado en otro gasto u otro edificio) y reconoce el numero aunque se escriba distinto.
        var supplierKey = BuildingExpenseCreditNoteKeys.Normalize(expense.SupplierName);
        var numeroKey = BuildingExpenseCreditNoteKeys.Normalize(numero);
        var timbradoKey = BuildingExpenseCreditNoteKeys.Normalize(timbrado);
        if (numeroKey.Length == 0)
            return BadRequest("El número de la nota de crédito no es válido.");

        var existing = await FindDuplicateAsync(expense.CompanyId, supplierKey, timbradoKey, numeroKey, cancellationToken);
        if (existing is not null) return Conflict(DuplicateMessage(existing));

        var published = period.Status == ExpensePeriodStatus.Published;

        // Validaciones y calculo propios de cada caso, antes de tocar nada.
        AllocationPlan? plan = null;
        if (published)
        {
            var creditedSoFar = await CreditedSoFarAsync(expense.Id, cancellationToken);
            var max = expense.Amount - creditedSoFar;
            if (amount > max) return BadRequest(PublishedCapMessage(max));

            plan = await BuildPlanAsync(expense, amount, cancellationToken);
            if (plan.Error is not null) return BadRequest(plan.Error);
            if (plan.UnitsWithoutOwner.Count > 0) return BadRequest(UnitsWithoutOwnerMessage(plan.UnitsWithoutOwner));
        }
        else
        {
            // El gasto tiene que seguir con monto positivo: si el proveedor anulo todo, se elimina el gasto.
            if (amount >= expense.Amount) return BadRequest(DraftCapMessage(expense.Amount));
        }

        var note = new BuildingExpenseCreditNote
        {
            CompanyId = expense.CompanyId,
            BuildingId = expense.BuildingId,
            BuildingExpenseId = expense.Id,
            ExpensePeriodId = expense.ExpensePeriodId,
            SupplierName = expense.SupplierName,
            SupplierKey = supplierKey,
            NumeroKey = numeroKey,
            TimbradoKey = timbradoKey,
            Numero = numero,
            Timbrado = timbrado,
            IssueDate = request.IssueDate,
            Amount = amount,
            Reason = reason,
            DocumentUrl = documentUrl,
            Mode = published ? BuildingExpenseCreditNoteMode.Credited : BuildingExpenseCreditNoteMode.Netted,
            Status = BuildingExpenseCreditNoteStatus.Applied,
            CreatedByUserId = tenantContext.UserId
        };
        dbContext.BuildingExpenseCreditNotes.Add(note);

        var pushes = new List<PendingPush>();
        decimal creditedToOwners = 0m;

        if (published)
        {
            creditedToOwners = await CreditOwnersAsync(note, expense, period, plan!, pushes, cancellationToken);
        }
        else
        {
            expense.OriginalAmount ??= expense.Amount;
            expense.Amount -= amount;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Otra persona la registro justo ahora: el indice unico de la base la rechazo.
            var raced = await FindDuplicateAsync(expense.CompanyId, supplierKey, timbradoKey, numeroKey, cancellationToken);
            if (raced is null) throw;
            return Conflict(DuplicateMessage(raced));
        }

        // Los avisos van despues de guardar, fuera de la transaccion.
        await SendPushesAsync(pushes);

        return Ok(await BuildResultAsync(note, expense, period, creditedToOwners, cancellationToken));
    }

    [HttpPost("credit-notes/{id:guid}/void")]
    public async Task<ActionResult<BuildingExpenseCreditNoteResultDto>> Void(
        Guid id, [FromBody] VoidBuildingExpenseCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length == 0 || reason.Length > ReasonMaxLength)
            return BadRequest($"El motivo es obligatorio (máximo {ReasonMaxLength} caracteres).");

        var note = await dbContext.BuildingExpenseCreditNotes
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (note is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(note.BuildingId, cancellationToken)) return Forbid();

        if (note.Status != BuildingExpenseCreditNoteStatus.Applied)
            return BadRequest("Esta nota de crédito ya está anulada.");

        var expense = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == note.BuildingExpenseId, cancellationToken);
        if (expense is null) return BadRequest("El gasto de la nota de crédito ya no existe.");

        var period = await dbContext.ExpensePeriods.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == note.ExpensePeriodId, cancellationToken);
        if (period is null) return BadRequest("El periodo de expensas asociado no existe.");

        var pushes = new List<PendingPush>();

        if (note.Mode == BuildingExpenseCreditNoteMode.Netted)
        {
            // Solo en borrador: es la unica etapa en la que el gasto se puede cambiar y la liquidacion recalcular.
            if (period.Status == ExpensePeriodStatus.Closed) return BadRequest(ClosedMessage);
            if (period.Status == ExpensePeriodStatus.Published)
                return BadRequest("El período ya está publicado: esta nota ya se tuvo en cuenta en la liquidación y no se puede anular.");

            expense.Amount += note.Amount;
            if (expense.OriginalAmount.HasValue && expense.Amount >= expense.OriginalAmount.Value)
            {
                expense.Amount = expense.OriginalAmount.Value;
                expense.OriginalAmount = null;
            }
        }
        else
        {
            var error = await RevertCreditsAsync(note, expense, period, reason, pushes, cancellationToken);
            if (error is not null) return BadRequest(error);
        }

        note.Status = BuildingExpenseCreditNoteStatus.Voided;
        note.VoidReason = reason;
        note.VoidedAtUtc = DateTime.UtcNow;
        note.VoidedByUserId = tenantContext.UserId;
        await dbContext.SaveChangesAsync(cancellationToken);

        await SendPushesAsync(pushes);

        return Ok(await BuildResultAsync(note, expense, period, 0m, cancellationToken));
    }

    // ── Periodo publicado: reparto y saldo a favor ────────────────────────────

    private sealed record PendingPush(Guid RecipientId, string Title, string Body, Guid PeriodId);

    private sealed record PlanRow(Guid UnitId, string UnitCode, Guid? OwnerId, string OwnerName, decimal ChargeAmount, decimal CreditAmount);

    private sealed class AllocationPlan
    {
        public List<PlanRow> Rows { get; } = new();
        public List<string> UnitsWithoutOwner { get; } = new();
        public decimal ChargedTotal { get; set; }
        public string? Error { get; set; }
    }

    private async Task SendPushesAsync(IEnumerable<PendingPush> pushes)
    {
        foreach (var p in pushes)
        {
            await push.NotifyUserAsync(p.RecipientId, p.Title, p.Body, "ExpensePeriod", p.PeriodId, CancellationToken.None,
                nameof(NotificationType.SupplierCreditApplied));
        }
    }

    // Lo ya acreditado al reparto del gasto por otras notas de periodo publicado.
    private async Task<decimal> CreditedSoFarAsync(Guid expenseId, CancellationToken ct) =>
        (await dbContext.BuildingExpenseCreditNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingExpenseId == expenseId
                        && x.Status == BuildingExpenseCreditNoteStatus.Applied && x.Mode == BuildingExpenseCreditNoteMode.Credited)
            .Select(x => x.Amount)
            .ToListAsync(ct)).Sum();

    // Prorratea la nota sobre lo que realmente se cobro de ese gasto a cada unidad (no sobre los coeficientes de hoy, que pudieron
    // cambiar). La suma de los creditos es exactamente el monto de la nota: la diferencia de redondeo va a la unidad con mas margen.
    private async Task<AllocationPlan> BuildPlanAsync(BuildingExpense expense, decimal amount, CancellationToken ct)
    {
        var plan = new AllocationPlan();

        // Los totales se suman en memoria: son pocas filas (una por unidad) y asi no dependen del motor de la base.
        var charged = (await dbContext.ExpenseCharges.AsNoTracking()
                .Where(x => !x.IsDeleted && x.SourceBuildingExpenseId == expense.Id && !x.IsReversal && x.Amount > 0)
                .Select(x => new { x.UnitId, x.Amount })
                .ToListAsync(ct))
            .GroupBy(x => x.UnitId)
            .Select(g => new { UnitId = g.Key, Amount = g.Sum(c => c.Amount) })
            .ToList();

        // Gasto que no se reparte entre las unidades (no distribuido o pagado por el fondo de reserva): no hay a quien acreditar.
        if (charged.Count == 0) return plan;

        plan.ChargedTotal = charged.Sum(x => x.Amount);
        if (plan.ChargedTotal < amount)
        {
            plan.Error = $"La nota (Gs. {amount:N0}) supera lo que se cobró de este gasto a las unidades (Gs. {plan.ChargedTotal:N0}).";
            return plan;
        }

        var unitIds = charged.Select(x => x.UnitId).ToList();

        var previous = (await dbContext.BuildingExpenseCreditNoteAllocations.AsNoTracking()
                .Where(x => !x.IsDeleted && unitIds.Contains(x.UnitId)
                            && x.CreditNote!.BuildingExpenseId == expense.Id && x.CreditNote.Status == BuildingExpenseCreditNoteStatus.Applied)
                .Select(x => new { x.UnitId, x.Amount })
                .ToListAsync(ct))
            .GroupBy(x => x.UnitId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        var units = await dbContext.Units.AsNoTracking()
            .Where(x => unitIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code })
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);

        var owners = (await dbContext.UnitOwners.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsPrimary && unitIds.Contains(x.UnitId))
                .OrderBy(x => x.StartDate)
                .Select(x => new { x.UnitId, x.OwnerId, Name = x.Owner != null ? x.Owner.FullName : string.Empty })
                .ToListAsync(ct))
            .GroupBy(x => x.UnitId)
            .ToDictionary(g => g.Key, g => g.First());

        // Credito de cada unidad, con tope en lo que le queda por acreditar de ese gasto.
        var shares = new Dictionary<Guid, decimal>();
        var capacity = new Dictionary<Guid, decimal>();
        foreach (var c in charged)
        {
            capacity[c.UnitId] = c.Amount - previous.GetValueOrDefault(c.UnitId);
            var share = decimal.Round(c.Amount * amount / plan.ChargedTotal, 2, MidpointRounding.AwayFromZero);
            shares[c.UnitId] = decimal.Min(share, capacity[c.UnitId]);
        }

        // Diferencia de redondeo: a la unidad con mas margen (si falta repartir) o a la que mas se paso (si sobra), de a centavo.
        var remainder = amount - shares.Values.Sum();
        while (remainder != 0m)
        {
            var target = remainder > 0
                ? shares.Keys.OrderByDescending(u => capacity[u] - shares[u]).First()
                : shares.Keys.OrderByDescending(u => shares[u]).First();
            var room = remainder > 0 ? capacity[target] - shares[target] : shares[target];
            if (room <= 0m)
            {
                plan.Error = "No se pudo repartir la nota entre las unidades. Revisá las notas anteriores de este gasto.";
                return plan;
            }

            var move = decimal.Min(decimal.Abs(remainder), room);
            shares[target] += remainder > 0 ? move : -move;
            remainder += remainder > 0 ? -move : move;
        }

        foreach (var c in charged.OrderBy(x => units.GetValueOrDefault(x.UnitId), StringComparer.OrdinalIgnoreCase))
        {
            var credit = shares[c.UnitId];
            if (credit <= 0m) continue;

            var code = units.GetValueOrDefault(c.UnitId) ?? string.Empty;
            var owner = owners.GetValueOrDefault(c.UnitId);
            if (owner is null) plan.UnitsWithoutOwner.Add(code);
            plan.Rows.Add(new PlanRow(c.UnitId, code, owner?.OwnerId, owner?.Name ?? string.Empty, c.Amount, credit));
        }

        return plan;
    }

    // Crea los lotes de saldo a favor, el reparto guardado y los avisos. No guarda: lo hace quien llama, en la misma transaccion.
    private async Task<decimal> CreditOwnersAsync(
        BuildingExpenseCreditNote note, BuildingExpense expense, ExpensePeriod period, AllocationPlan plan,
        List<PendingPush> pushes, CancellationToken ct)
    {
        var reference = $"NC proveedor {note.Numero} · {note.SupplierName}";
        var total = 0m;

        foreach (var row in plan.Rows)
        {
            var lot = await credits.AddSupplierCreditLotAsync(
                row.OwnerId!.Value, expense.CompanyId, row.CreditAmount, note.Id, expense.BuildingId, row.UnitId, reference,
                $"Saldo a favor por la nota de crédito {note.Numero} de {note.SupplierName} sobre «{expense.Description}» ({period.Name}), unidad {row.UnitCode}.",
                ct);

            dbContext.BuildingExpenseCreditNoteAllocations.Add(new BuildingExpenseCreditNoteAllocation
            {
                CompanyId = expense.CompanyId,
                CreditNoteId = note.Id,
                UnitId = row.UnitId,
                OwnerId = row.OwnerId.Value,
                Amount = row.CreditAmount,
                OwnerCreditMovementId = lot.Id
            });
            total += row.CreditAmount;
        }

        foreach (var group in plan.Rows.GroupBy(r => r.OwnerId!.Value))
        {
            var amount = group.Sum(r => r.CreditAmount);
            var codes = string.Join(", ", group.Select(r => r.UnitCode));
            var title = "Saldo a favor por ajuste de un gasto";
            var body = $"{note.SupplierName} emitió una nota de crédito sobre «{expense.Description}» ({period.Name}). " +
                       $"Se acreditó {MarketplaceNotices.Gs(amount)} a tu saldo a favor (unidad {codes}); se aplica en tu próximo pago.";

            AddNotification(expense.CompanyId, group.Key, title, body, period.Id);
            pushes.Add(new PendingPush(group.Key, title, body, period.Id));
        }

        return total;
    }

    // Anular una nota de periodo publicado: se devuelve el saldo a favor, y solo si nadie lo uso todavia.
    private async Task<string?> RevertCreditsAsync(
        BuildingExpenseCreditNote note, BuildingExpense expense, ExpensePeriod period, string reason,
        List<PendingPush> pushes, CancellationToken ct)
    {
        var allocations = await dbContext.BuildingExpenseCreditNoteAllocations
            .Include(x => x.OwnerCreditMovement)
            .Include(x => x.Unit)
            .Where(x => !x.IsDeleted && x.CreditNoteId == note.Id)
            .ToListAsync(ct);

        var used = allocations.Where(a => a.OwnerCreditMovement is null || a.OwnerCreditMovement.IsDeleted
                                          || a.OwnerCreditMovement.RemainingAmount != a.OwnerCreditMovement.Amount).ToList();
        if (used.Count > 0)
        {
            var units = string.Join(", ", used.Select(a => a.Unit?.Code ?? "?"));
            return $"No se puede anular: el saldo a favor acreditado por esta nota ya se usó (unidad {units}). " +
                   "Un saldo ya aplicado a un pago no se revierte desde acá.";
        }

        var ownerIds = allocations.Select(a => a.OwnerId).Distinct().ToList();
        var ownerCredits = await dbContext.OwnerCredits
            .Where(x => !x.IsDeleted && x.CompanyId == expense.CompanyId && ownerIds.Contains(x.OwnerId))
            .ToDictionaryAsync(x => x.OwnerId, ct);

        foreach (var group in allocations.GroupBy(a => a.OwnerId))
        {
            var amount = group.Sum(a => a.Amount);
            if (!ownerCredits.TryGetValue(group.Key, out var credit) || credit.Amount < amount)
                return "No se puede anular: el saldo a favor del propietario ya no alcanza para devolver lo acreditado.";
        }

        foreach (var group in allocations.GroupBy(a => a.OwnerId))
        {
            var amount = group.Sum(a => a.Amount);
            ownerCredits[group.Key].Amount -= amount;

            foreach (var a in group)
            {
                var lot = a.OwnerCreditMovement!;
                lot.RemainingAmount = 0m;
                lot.Description += " Anulado: la nota de crédito del proveedor fue anulada.";
            }

            var title = "Se anuló un ajuste de un gasto";
            var body = $"Se anuló la nota de crédito {note.Numero} de {note.SupplierName} sobre «{expense.Description}» ({period.Name}). " +
                       $"Se descontaron {MarketplaceNotices.Gs(amount)} de tu saldo a favor. Motivo: {reason}";
            AddNotification(expense.CompanyId, group.Key, title, body, period.Id);
            pushes.Add(new PendingPush(group.Key, title, body, period.Id));
        }

        return null;
    }

    private void AddNotification(Guid companyId, Guid recipientId, string title, string body, Guid periodId) =>
        dbContext.Notifications.Add(new Notification
        {
            CompanyId = companyId,
            RecipientId = recipientId,
            Type = NotificationType.SupplierCreditApplied,
            Title = title,
            Body = body,
            EntityType = "ExpensePeriod",
            EntityId = periodId
        });

    // ── Mensajes y utilidades ─────────────────────────────────────────────────

    private static string DraftCapMessage(decimal expenseAmount) =>
        $"La nota de crédito no puede igualar ni superar el monto pendiente del gasto (Gs. {expenseAmount:N0}). " +
        "Si el proveedor anuló el gasto completo, eliminá el gasto.";

    private static string PublishedCapMessage(decimal max) =>
        max <= 0m
            ? "Este gasto ya tiene acreditado todo su monto con notas de crédito anteriores."
            : $"La nota supera lo que todavía se puede acreditar de este gasto (Gs. {max:N0}).";

    private static string UnitsWithoutOwnerMessage(IEnumerable<string> unitCodes) =>
        $"Asigná un propietario principal a las unidades {string.Join(", ", unitCodes)} antes de aplicar la nota de crédito " +
        "(si no se vendieron, asignalas al dueño del edificio).";

    private static string NoDistributionMessage(BuildingExpense expense) =>
        expense.PaidByReserveFund
            ? "Este gasto lo pagó el fondo de reserva: la nota se registra sin generar saldo a favor a las unidades."
            : "Este gasto no se cobró a las unidades: la nota se registra sin generar saldo a favor.";

    private sealed record DuplicateInfo(string Numero, string SupplierName, DateOnly IssueDate, string ExpenseDescription, string BuildingName);

    // Misma nota aplicada: mismo proveedor y numero, y mismo timbrado (si alguna de las dos no lo trae, no se puede distinguir por
    // timbrado y se considera la misma).
    private async Task<DuplicateInfo?> FindDuplicateAsync(
        Guid companyId, string supplierKey, string timbradoKey, string numeroKey, CancellationToken cancellationToken) =>
        await dbContext.BuildingExpenseCreditNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == BuildingExpenseCreditNoteStatus.Applied && x.CompanyId == companyId
                        && x.SupplierKey == supplierKey && x.NumeroKey == numeroKey
                        && (x.TimbradoKey == timbradoKey || x.TimbradoKey == "" || timbradoKey == ""))
            .Select(x => new DuplicateInfo(x.Numero, x.SupplierName, x.IssueDate,
                x.BuildingExpense != null ? x.BuildingExpense.Description : string.Empty,
                x.Building != null ? x.Building.Name : string.Empty))
            .FirstOrDefaultAsync(cancellationToken);

    private static string DuplicateMessage(DuplicateInfo d) =>
        $"Esta nota de crédito ya está registrada: NC {d.Numero} de {d.SupplierName}, del {d.IssueDate:dd/MM/yyyy} " +
        $"(gasto «{d.ExpenseDescription}», {d.BuildingName}). No se puede registrar dos veces.";

    // Solo archivos que subio la propia plataforma (/api/uploads devuelve /uploads/...).
    private static bool IsValidUploadUrl(string url) =>
        url.Length <= DocumentUrlMaxLength
        && url.StartsWith("/uploads/", StringComparison.Ordinal)
        && !url.Contains("..", StringComparison.Ordinal)
        && !url.Contains('\\')
        && !url.Contains("//", StringComparison.Ordinal);

    private async Task<Dictionary<Guid, List<BuildingExpenseCreditNoteAllocationDto>>> LoadAllocationDtosAsync(
        IReadOnlyCollection<Guid> noteIds, CancellationToken ct)
    {
        if (noteIds.Count == 0) return new();

        var rows = await dbContext.BuildingExpenseCreditNoteAllocations.AsNoTracking()
            .Where(x => !x.IsDeleted && noteIds.Contains(x.CreditNoteId))
            .Select(x => new
            {
                x.CreditNoteId,
                x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : string.Empty,
                x.OwnerId,
                OwnerName = x.Owner != null ? x.Owner.FullName : string.Empty,
                x.Amount
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.CreditNoteId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.UnitCode, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new BuildingExpenseCreditNoteAllocationDto
                    {
                        UnitId = x.UnitId, UnitCode = x.UnitCode, OwnerId = x.OwnerId, OwnerName = x.OwnerName, Amount = x.Amount
                    })
                    .ToList());
    }

    private async Task<BuildingExpenseCreditNoteResultDto> BuildResultAsync(
        BuildingExpenseCreditNote note, BuildingExpense expense, ExpensePeriod period, decimal creditedToOwners, CancellationToken cancellationToken)
    {
        var building = await dbContext.Buildings.AsNoTracking().FirstAsync(x => x.Id == expense.BuildingId, cancellationToken);
        var targetUnit = expense.TargetUnitId.HasValue
            ? await dbContext.Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expense.TargetUnitId.Value, cancellationToken)
            : null;
        var rubro = expense.LedgerCategoryId.HasValue
            ? await dbContext.LedgerCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expense.LedgerCategoryId.Value, cancellationToken)
            : null;

        var settlementCalculated = await dbContext.ExpenseSettlements.AsNoTracking().AnyAsync(
            x => !x.IsDeleted && x.ExpensePeriodId == period.Id && x.Status == ExpenseSettlementStatus.Calculated, cancellationToken);

        var allocations = await LoadAllocationDtosAsync(new[] { note.Id }, cancellationToken);

        return new BuildingExpenseCreditNoteResultDto
        {
            CreditNote = ToDto(note, allocations.GetValueOrDefault(note.Id)),
            Expense = BuildingExpensesController.ToDto(expense, building, period, targetUnit, rubro),
            SettlementNeedsRecalculation = settlementCalculated && period.Status == ExpensePeriodStatus.Draft,
            CreditedToOwners = creditedToOwners
        };
    }

    private static BuildingExpenseCreditNoteDto ToDto(BuildingExpenseCreditNote x, List<BuildingExpenseCreditNoteAllocationDto>? allocations) => new()
    {
        Id = x.Id,
        BuildingId = x.BuildingId,
        BuildingExpenseId = x.BuildingExpenseId,
        ExpensePeriodId = x.ExpensePeriodId,
        SupplierName = x.SupplierName,
        Numero = x.Numero,
        Timbrado = x.Timbrado,
        IssueDate = x.IssueDate,
        Amount = x.Amount,
        Reason = x.Reason,
        DocumentUrl = x.DocumentUrl,
        Mode = x.Mode,
        Status = x.Status,
        CreatedByUserId = x.CreatedByUserId,
        CreatedAtUtc = x.CreatedAtUtc,
        VoidReason = x.VoidReason,
        VoidedAtUtc = x.VoidedAtUtc,
        Allocations = allocations ?? new()
    };
}
