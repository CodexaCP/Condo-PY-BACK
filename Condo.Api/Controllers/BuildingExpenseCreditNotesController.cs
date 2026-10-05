using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

// Notas de credito que el PROVEEDOR emite sobre un gasto del edificio (no confundir con las notas de credito al propietario, en
// CreditNotesController). Fase 1: solo con el periodo en borrador. La nota baja el monto del gasto (BuildingExpense.Amount queda
// neto, OriginalAmount guarda lo facturado) y la liquidacion se calcula con el neto. Con el periodo cerrado hay que anular antes la
// liquidacion; con el periodo publicado el reparto como saldo a favor aun no esta disponible.
[ApiController]
[Authorize]
[Route("api/building-expenses")]
public class BuildingExpenseCreditNotesController(
    ICondoDbContext dbContext,
    IAccessScopeService accessScope,
    ITenantContext tenantContext) : ControllerBase
{
    private const int NumeroMaxLength = 50;
    private const int TimbradoMaxLength = 20;
    private const int ReasonMaxLength = 500;
    private const int DocumentUrlMaxLength = 500;

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

        return Ok(notes.Select(ToDto).ToList());
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

        var documentUrl = string.IsNullOrWhiteSpace(request.DocumentUrl) ? null : request.DocumentUrl.Trim();
        if (documentUrl is not null && !IsValidUploadUrl(documentUrl))
            return BadRequest("El archivo de la nota de crédito no es válido. Subilo de nuevo.");

        var expense = await dbContext.BuildingExpenses
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expenseId, cancellationToken);
        if (expense is null) return NotFound();
        if (!await accessScope.CanAccessBuildingAsync(expense.BuildingId, cancellationToken)) return Forbid();

        var period = await dbContext.ExpensePeriods.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == expense.ExpensePeriodId, cancellationToken);
        if (period is null) return BadRequest("El periodo de expensas asociado no existe.");

        var periodError = PeriodStatusError(period.Status);
        if (periodError is not null) return BadRequest(periodError);

        // El gasto tiene que seguir con monto positivo: si el proveedor anulo todo, se elimina el gasto.
        if (amount >= expense.Amount)
        {
            return BadRequest($"La nota de crédito no puede igualar ni superar el monto pendiente del gasto (Gs. {expense.Amount:N0}). " +
                              "Si el proveedor anuló el gasto completo, eliminá el gasto.");
        }

        var duplicated = await dbContext.BuildingExpenseCreditNotes.AsNoTracking().AnyAsync(
            x => !x.IsDeleted && x.Status == BuildingExpenseCreditNoteStatus.Applied && x.BuildingId == expense.BuildingId
                 && x.Numero == numero && x.BuildingExpense!.SupplierName == expense.SupplierName, cancellationToken);
        if (duplicated)
            return Conflict("Ya hay una nota de crédito de este proveedor con ese número en el edificio.");

        expense.OriginalAmount ??= expense.Amount;
        expense.Amount -= amount;

        var note = new BuildingExpenseCreditNote
        {
            CompanyId = expense.CompanyId,
            BuildingId = expense.BuildingId,
            BuildingExpenseId = expense.Id,
            ExpensePeriodId = expense.ExpensePeriodId,
            Numero = numero,
            Timbrado = timbrado,
            IssueDate = request.IssueDate,
            Amount = amount,
            Reason = reason,
            DocumentUrl = documentUrl,
            Mode = BuildingExpenseCreditNoteMode.Netted,
            Status = BuildingExpenseCreditNoteStatus.Applied,
            CreatedByUserId = tenantContext.UserId
        };
        dbContext.BuildingExpenseCreditNotes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildResultAsync(note, expense, period, cancellationToken));
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

        var periodError = PeriodStatusError(period.Status);
        if (periodError is not null) return BadRequest(periodError);

        expense.Amount += note.Amount;
        if (expense.OriginalAmount.HasValue && expense.Amount >= expense.OriginalAmount.Value)
        {
            expense.Amount = expense.OriginalAmount.Value;
            expense.OriginalAmount = null;
        }

        note.Status = BuildingExpenseCreditNoteStatus.Voided;
        note.VoidReason = reason;
        note.VoidedAtUtc = DateTime.UtcNow;
        note.VoidedByUserId = tenantContext.UserId;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildResultAsync(note, expense, period, cancellationToken));
    }

    // Solo con el periodo en borrador: es la unica etapa en la que el gasto se puede cambiar y la liquidacion recalcular.
    private static string? PeriodStatusError(ExpensePeriodStatus status) => status switch
    {
        ExpensePeriodStatus.Draft => null,
        ExpensePeriodStatus.Closed =>
            "El período está cerrado. Anulá la liquidación (Gastos y cargos › Liquidación › Anular) y registrá la nota de crédito con el período en borrador.",
        _ => "El período ya está publicado: el ajuste de una nota de crédito de proveedor sobre un período publicado (saldo a favor de las unidades) todavía no está disponible."
    };

    // Solo archivos que subio la propia plataforma (/api/uploads devuelve /uploads/...).
    private static bool IsValidUploadUrl(string url) =>
        url.Length <= DocumentUrlMaxLength
        && url.StartsWith("/uploads/", StringComparison.Ordinal)
        && !url.Contains("..", StringComparison.Ordinal)
        && !url.Contains('\\')
        && !url.Contains("//", StringComparison.Ordinal);

    private async Task<BuildingExpenseCreditNoteResultDto> BuildResultAsync(
        BuildingExpenseCreditNote note, BuildingExpense expense, ExpensePeriod period, CancellationToken cancellationToken)
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

        return new BuildingExpenseCreditNoteResultDto
        {
            CreditNote = ToDto(note),
            Expense = BuildingExpensesController.ToDto(expense, building, period, targetUnit, rubro),
            SettlementNeedsRecalculation = settlementCalculated
        };
    }

    private static BuildingExpenseCreditNoteDto ToDto(BuildingExpenseCreditNote x) => new()
    {
        Id = x.Id,
        BuildingId = x.BuildingId,
        BuildingExpenseId = x.BuildingExpenseId,
        ExpensePeriodId = x.ExpensePeriodId,
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
        VoidedAtUtc = x.VoidedAtUtc
    };
}
