namespace Condo.Application.Models;

public enum VoiceCommandIntent { Unknown, CreateEmptyPeriod, ClonePreviousPeriod, ListDelinquents }
public enum OverdueAgeUnit { Days, Weeks, Months, Years }

/// <summary>Resultado puro del analizador de frases (sin base de datos).</summary>
public sealed class ParsedVoiceCommand
{
    public VoiceCommandIntent Intent { get; init; }
    /// <summary>La frase habla de un período pero no dice si vacío o igual al anterior.</summary>
    public bool MentionsPeriod { get; init; }
    public int? AgeAmount { get; init; }
    public OverdueAgeUnit? AgeUnit { get; init; }
    /// <summary>Corte de vencimiento: unidades cuyo comprobante más viejo venció en esta fecha o antes.</summary>
    public DateOnly? DueOnOrBefore { get; init; }
}

public class VoiceCommandInterpretationDto
{
    public string Text { get; set; } = string.Empty;
    public VoiceCommandIntent Intent { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string Summary { get; set; } = string.Empty;
    /// <summary>Si no es null, el cliente lo muestra y NO ejecuta nada.</summary>
    public string? Message { get; set; }
    public Guid? BuildingId { get; set; }
    public string? BuildingName { get; set; }
    public Guid? SourcePeriodId { get; set; }
    public string? SourcePeriodName { get; set; }
    public string? TargetPeriodName { get; set; }
    /// <summary>Solo para CreateEmptyPeriod: el cuerpo listo para POST api/expense-periods.</summary>
    public ExpensePeriodUpsertRequest? ProposedPeriod { get; set; }
    public int? AgeAmount { get; set; }
    public OverdueAgeUnit? AgeUnit { get; set; }
    public DateOnly? DueOnOrBefore { get; set; }
}

public class MorosityUnitDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string ResponsibleName { get; set; } = string.Empty;
    public string ResponsiblePhone { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;
    public DateOnly OldestDueDate { get; set; }
    public int DaysOverdue { get; set; }
    public int OverduePeriods { get; set; }
    public decimal TotalBalance { get; set; }
}
