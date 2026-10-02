using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class ExpensePeriodUpsertRequest
{
    public Guid BuildingId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? LateFeeDate { get; set; }
    public ExpensePeriodStatus Status { get; set; } = ExpensePeriodStatus.Draft;
    public string Notes { get; set; } = string.Empty;
}

public class ExpensePeriodDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? LateFeeDate { get; set; }
    public ExpensePeriodStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class BulkCreateExpensePeriodsRequest
{
    public List<Guid> BuildingIds { get; set; } = [];
    public int Year { get; set; }
    public int Month { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? LateFeeDate { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class BulkCreateExpensePeriodsResultDto
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<string> CreatedBuildings { get; set; } = [];
    public List<string> SkippedBuildings { get; set; } = [];
}

public class CloneExpensePeriodResultDto
{
    public ExpensePeriodDto Period { get; set; } = null!;
    public int CopiedExpenses { get; set; }
    // Ingresos copiados (todos menos el Saldo acumulado) y Saldo acumulado creado con el cierre del periodo anterior (0 si no hubo).
    public int CopiedIncomes { get; set; }
    public decimal AccumulatedBalance { get; set; }
}

// Conciliacion de un periodo: lo que deberian sumar los cargos segun los gastos, ingresos y aportes cargados,
// contra lo que realmente se emitio, mas el estado de cobro.
public class ExpensePeriodReconciliationDto
{
    public Guid ExpensePeriodId { get; set; }
    public string ExpensePeriodName { get; set; } = string.Empty;
    public string PeriodStatus { get; set; } = string.Empty;
    public string? SettlementStatus { get; set; }

    // De donde sale lo que se cobra
    public decimal TotalExpenses { get; set; }
    public decimal NonDistributedExpenses { get; set; }
    public decimal PaidByReserveFundExpenses { get; set; }
    public decimal IncomesCredited { get; set; }
    public decimal ReserveContribution { get; set; }
    public decimal ExtraordinaryContribution { get; set; }

    // Lo esperado (reparto de la liquidacion con los datos actuales) contra lo emitido
    public decimal ExpectedCharges { get; set; }
    public decimal IssuedCharges { get; set; }
    public decimal Difference { get; set; }
    // Preview (todavia no se emitieron cargos) | Reconciled | Difference | Error
    public string State { get; set; } = "Preview";
    public string? Message { get; set; }

    // Cargos manuales anteriores (legacy) que siguen en el periodo y la mora
    public int ManualChargeCount { get; set; }
    public decimal ManualChargeAmount { get; set; }
    public decimal LateFeeAmount { get; set; }

    // Cobranza del periodo (suma de todos sus cargos vigentes)
    public decimal TotalCharged { get; set; }
    public decimal Collected { get; set; }
    public decimal Pending { get; set; }
}
