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
