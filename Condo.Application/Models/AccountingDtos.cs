using Condo.Application.Services;
using Condo.Domain.Enums;

namespace Condo.Application.Models;

// ── Asientos sugeridos y contrapartidas contables ─────────────────────────────

/// <summary>Una cuenta del plan que se puede elegir como contrapartida (cuenta final de activo o de fondos).</summary>
public class AccountingPlanOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public LedgerCategoryType Type { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>Una cuenta financiera del edificio con la cuenta del plan que la representa.</summary>
public class AccountingRoleAccountDto
{
    public Guid FinancialAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; }
    public bool IsActive { get; set; }
    public Guid? LedgerCategoryId { get; set; }
    public string? Code { get; set; }
    public string? CategoryName { get; set; }
    // Sugerencia por el nombre y tipo de la cuenta; no se aplica sola.
    public Guid? SuggestedCategoryId { get; set; }
}

public class AccountingRolesDto
{
    public Guid BuildingId { get; set; }
    public bool FinanceAvailable { get; set; } = true;
    public bool CanEdit { get; set; }
    public IReadOnlyList<AccountingRoleAccountDto> Accounts { get; set; } = [];
    public Guid? VatCreditCategoryId { get; set; }
    public string? VatCreditCode { get; set; }
    public string? VatCreditName { get; set; }
    public Guid? SuggestedVatCreditCategoryId { get; set; }
    public IReadOnlyList<AccountingPlanOptionDto> Options { get; set; } = [];
    // Todas las cuentas financieras activas y el IVA credito tienen su cuenta del plan.
    public bool IsComplete { get; set; }
}

public class AccountingRoleItemRequest
{
    public Guid FinancialAccountId { get; set; }
    // Nula = quitar la asignacion.
    public Guid? LedgerCategoryId { get; set; }
}

public class UpdateAccountingRolesRequest
{
    // Solo las cuentas que vienen se modifican; las demas quedan como estaban.
    public List<AccountingRoleItemRequest>? Accounts { get; set; }
    // Se aplica solo cuando viene SetVatCredit: nula = quitar la asignacion.
    public bool SetVatCredit { get; set; }
    public Guid? VatCreditCategoryId { get; set; }
}

public class AccountingLineDto
{
    // Cuenta del plan; nula cuando falta la asignacion (renglon marcado).
    public Guid? CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    // Financial (banco, caja o fondo), Rubro (ingreso o gasto) o VatCredit.
    public string Kind { get; set; } = string.Empty;
    public bool Missing { get; set; }
}

public class AccountingEntryDto
{
    public int Number { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ThirdParty { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public LedgerSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    // Cuenta financiera del movimiento (parte de la identidad del asiento al asentarlo).
    public Guid? SourceAccountId { get; set; }
    public IReadOnlyList<AccountingLineDto> Lines { get; set; } = [];
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    // Todos los renglones tienen cuenta del plan. Un asiento incompleto no se da por cuadrado para el contador.
    public bool IsComplete { get; set; }
    public IReadOnlyList<string> Issues { get; set; } = [];
}

public class AccountingTotalDto
{
    public Guid? CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public bool Missing { get; set; }
}

public class AccountingEntriesDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public IReadOnlyList<AccountingEntryDto> Entries { get; set; } = [];
    public IReadOnlyList<AccountingTotalDto> Totals { get; set; } = [];
    public int EntryCount { get; set; }
    public int IncompleteCount { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    // Cuentas financieras y rubros sin cuenta del plan que hicieron incompletos a algunos asientos.
    public IReadOnlyList<string> Pending { get; set; } = [];
    // Ultimo dia asentado en el Libro Diario: los sugeridos hasta esa fecha ya tienen su numero real.
    public DateOnly? PostedThrough { get; set; }
}

// ── Libros contables del edificio (titular, contador y rubrica) ───────────────

/// <summary>Datos con que se completa el encabezado del Libro Diario cuando el titular no los pisa.</summary>
public class AccountingBookDefaultsDto
{
    public string? LegalName { get; set; }
    public string? Ruc { get; set; }
    public string? Address { get; set; }
}

public class AccountingBooksDto
{
    public Guid BuildingId { get; set; }
    public bool FinanceAvailable { get; set; } = true;
    public bool CanEdit { get; set; }
    // Ya se guardo la configuracion alguna vez (sin guardar rige el consorcio con los datos de la ficha).
    public bool Configured { get; set; }
    public AccountingBookHolder Holder { get; set; } = AccountingBookHolder.Consortium;
    // Lo que cargo el usuario; vacio = se usa el dato por defecto del titular.
    public string? HolderLegalName { get; set; }
    public string? HolderRuc { get; set; }
    public string? HolderAddress { get; set; }
    public string? AccountantName { get; set; }
    public string? AccountantRegistration { get; set; }
    public string? BookNumber { get; set; }
    public DateOnly? RubricDate { get; set; }
    public int? AuthorizedFolios { get; set; }
    public int? UsedFolios { get; set; }
    // Por defecto de cada titular: ficha del edificio (consorcio) y empresa (administradora).
    public AccountingBookDefaultsDto ConsortiumDefaults { get; set; } = new();
    public AccountingBookDefaultsDto AdminCompanyDefaults { get; set; } = new();
    // Lo que saldria hoy en el encabezado del Diario (dato cargado o, si falta, el por defecto del titular).
    public AccountingBookDefaultsDto Effective { get; set; } = new();
}

public class UpdateAccountingBooksRequest
{
    public AccountingBookHolder Holder { get; set; } = AccountingBookHolder.Consortium;
    public string? HolderLegalName { get; set; }
    public string? HolderRuc { get; set; }
    public string? HolderAddress { get; set; }
    public string? AccountantName { get; set; }
    public string? AccountantRegistration { get; set; }
    public string? BookNumber { get; set; }
    public DateOnly? RubricDate { get; set; }
    public int? AuthorizedFolios { get; set; }
    public int? UsedFolios { get; set; }
}

// ── Libro Diario: asientos asentados ──────────────────────────────────────────

public class JournalEntryLineDto
{
    public Guid? LedgerCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? ExternalCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Kind { get; set; } = string.Empty;
}

public class JournalEntryDto
{
    public Guid Id { get; set; }
    public int FiscalYear { get; set; }
    public int Number { get; set; }
    // Numero completo para mostrar y citar, por ejemplo 2026-000012.
    public string Label { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateOnly OperationDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ThirdParty { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public JournalEntryKind Kind { get; set; }
    public LedgerSourceType? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? ReversesEntryId { get; set; }
    // Numero del asiento que este contraasiento invierte, y del contraasiento que anulo a este asiento (si lo hay).
    public string? ReversesLabel { get; set; }
    public string? ReversedByLabel { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public DateTime PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public IReadOnlyList<JournalEntryLineDto> Lines { get; set; } = [];
}

public class JournalDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int FiscalYear { get; set; }
    // Ejercicios que ya tienen asientos (para elegir en el filtro).
    public IReadOnlyList<int> FiscalYears { get; set; } = [];
    public DateOnly? PostedThrough { get; set; }
    public IReadOnlyList<JournalEntryDto> Entries { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    // Sumas de todo lo que cumple el filtro (no solo de la pagina).
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public bool CanPost { get; set; }
    // Primer folio sugerido para imprimir el Libro Diario: el siguiente a los folios usados de la rubrica (1 si no se cargo).
    public int SuggestedStartFolio { get; set; } = 1;
}

/// <summary>Lo que se asentaria hasta una fecha, sin asentar nada.</summary>
public class JournalPreviewDto
{
    public Guid BuildingId { get; set; }
    public DateOnly? PostedThrough { get; set; }
    public DateOnly From { get; set; }
    public DateOnly Through { get; set; }
    public int EntryCount { get; set; }
    public int IncompleteCount { get; set; }
    public IReadOnlyList<string> Pending { get; set; } = [];
    public string? FirstLabel { get; set; }
    public string? LastLabel { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    // Asientos ya asentados que cambiaron y se corrigen al asentar (contraasiento + asiento nuevo), y cuantos asientos nuevos eso crea.
    public IReadOnlyList<JournalDifferenceDto> Differences { get; set; } = [];
    public int CorrectionEntryCount { get; set; }
    // Periodos en borrador con gastos o ingresos en lo que se asentaria: mientras esten en borrador pueden cambiar, asi que no se asienta.
    public IReadOnlyList<string> DraftPeriods { get; set; } = [];
    // Hay algo para asentar y todos los asientos estan completos.
    public bool CanPost { get; set; }
}

public class PostJournalRequest
{
    // Se asienta todo lo posterior a lo ya asentado, hasta este dia inclusive.
    public DateOnly Through { get; set; }
}

public class PostJournalResultDto
{
    public int PostedCount { get; set; }
    // Cuantos de los asientos creados son correcciones (contraasientos y asientos de reemplazo).
    public int CorrectionEntryCount { get; set; }
    public string? FirstLabel { get; set; }
    public string? LastLabel { get; set; }
    public DateOnly PostedThrough { get; set; }
}

// ── Libro Diario: diferencias contra lo asentado y contraasientos ─────────────

public enum JournalDifferenceKind
{
    // El movimiento cambio (importe, IVA, cuenta o rubro) despues de asentarlo: contraasiento del asiento y asiento nuevo.
    Changed = 1,
    // Un movimiento de un dia ya asentado que no tiene asiento (se cargo o se corrigio despues): asiento nuevo.
    Missing = 2,
    // El movimiento ya no existe (se revirtio o se borro): contraasiento del asiento.
    Removed = 3
}

public class JournalDifferenceDto
{
    public JournalDifferenceKind Kind { get; set; }
    public LedgerSourceType SourceType { get; set; }
    // Que paso, en palabras («Pago revertido», «Gasto modificado»…), y el motivo si el sistema lo guardo (reversa de un pago, nota anulada).
    public string Cause { get; set; } = string.Empty;
    public string? Reason { get; set; }
    // Asiento asentado que se corrige (nulo si el movimiento nunca se asento).
    public Guid? EntryId { get; set; }
    public string? EntryLabel { get; set; }
    public DateOnly OperationDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ThirdParty { get; set; } = string.Empty;
    // Importe del asiento asentado y del que saldria hoy (nulo cuando no existe de ese lado).
    public decimal? AmountBefore { get; set; }
    public decimal? AmountAfter { get; set; }
    // El asiento nuevo estaria incompleto (falta una cuenta del plan): no se puede corregir hasta asignarla.
    public bool Incomplete { get; set; }
}

public class JournalDifferencesDto
{
    public Guid BuildingId { get; set; }
    public DateOnly? PostedThrough { get; set; }
    public IReadOnlyList<JournalDifferenceDto> Items { get; set; } = [];
    public int ChangedCount { get; set; }
    public int MissingCount { get; set; }
    public int RemovedCount { get; set; }
    public int IncompleteCount { get; set; }
    public IReadOnlyList<string> Pending { get; set; } = [];
}

/// <summary>Cuantos asientos asentados (y vigentes) toca un hecho que se esta por hacer: revertir un pago o deshacer la publicacion de un periodo.</summary>
public class JournalImpactDto
{
    public bool Posted { get; set; }
    public int PostedCount { get; set; }
    // Numeros de los primeros asientos afectados (hasta cinco).
    public IReadOnlyList<string> Labels { get; set; } = [];
    public DateOnly? PostedThrough { get; set; }
}
