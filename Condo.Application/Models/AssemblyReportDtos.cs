using Condo.Domain.Enums;

namespace Condo.Application.Models;

// ── Informe de asamblea ───────────────────────────────────────────────────────

public class AssemblyReportRubroDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class AssemblyReportAccountDto
{
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; }
    // Fila de los movimientos que no tienen cuenta asignada.
    public bool IsUnassigned { get; set; }
    // Saldo al comienzo del rango, lo que entro y salio en el rango, y el saldo al final.
    public decimal Opening { get; set; }
    public decimal Inflows { get; set; }
    public decimal Outflows { get; set; }
    public decimal Closing { get; set; }
}


public class AssemblyReportReserveDto
{
    public string AccountName { get; set; } = string.Empty;
    public decimal? ReserveFundPercentage { get; set; }
    public decimal Opening { get; set; }
    public decimal Contributions { get; set; }
    public decimal Uses { get; set; }
    public decimal Closing { get; set; }
}

/// <summary>
/// Datos del informe para la asamblea de propietarios de un edificio y un rango de fechas. Los movimientos van por el criterio de caja
/// (percibido) del libro de Finanzas; la morosidad y las cuentas por pagar son la situacion a la fecha de emision (<see cref="SnapshotDate"/>).
/// La morosidad va solo agregada por antiguedad: no figura ninguna unidad ni propietario.
/// </summary>
public class AssemblyReportDto
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string? Notes { get; set; }

    // Resultado del rango (criterio de caja).
    public decimal OpeningBalance { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetResult { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<AssemblyReportAccountDto> Accounts { get; set; } = [];
    public IReadOnlyList<AssemblyReportRubroDto> IncomeLines { get; set; } = [];
    public IReadOnlyList<AssemblyReportRubroDto> ExpenseLines { get; set; } = [];

    // Ejecucion presupuestaria acumulada del ejercicio hasta el mes de cierre del rango; nula si no se cargo presupuesto.
    public FinanceBudgetVsActualDto? Budget { get; set; }

    // Nulo si el edificio no tiene cuenta de fondo de reserva.
    public AssemblyReportReserveDto? Reserve { get; set; }

    // Situacion a esta fecha (la de emision, no la del cierre del rango).
    public DateOnly SnapshotDate { get; set; }
    public ReceivablesSummaryDto Receivables { get; set; } = new();
    public PayablesSummaryDto Payables { get; set; } = new();

    public IReadOnlyList<string> Warnings { get; set; } = [];
}

public class AssemblyReportRequest
{
    public Guid BuildingId { get; set; }
    // Por defecto, del comienzo del ejercicio de hoy hasta hoy.
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    // Notas del administrador (texto libre); no se guardan, van solo en el informe generado.
    public string? Notes { get; set; }
}
