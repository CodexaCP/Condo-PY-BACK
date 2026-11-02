namespace Condo.Application.Models;

public class VoteDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal QuorumPercentage { get; set; }
    public string WeightType { get; set; } = "ByUnit";
    // "Simple" o "DoubleAbsolute" (mayoria absoluta sobre la totalidad, por unidades y por coeficiente a la vez).
    public string MajorityRule { get; set; } = "Simple";
    // Consulta informativa: el voto en la app no reemplaza al voto formal de la asamblea.
    public bool IsInformative { get; set; }
    // Con DoubleAbsolute: la opcion que supera el 50 % de las unidades Y el 50 % del coeficiente totales; null si ninguna.
    public Guid? ApprovedOptionId { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int TotalUnits { get; set; }
    public int ParticipatingUnits { get; set; }
    public bool QuorumReached { get; set; }
    public List<VoteOptionDto> Options { get; set; } = [];
    public List<VoteUnitSummaryDto> Units { get; set; } = [];
}

public class VoteOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public int CastCount { get; set; }
    public decimal CastWeight { get; set; }
    public decimal Percentage { get; set; }
    // Porcentaje sobre la TOTALIDAD de unidades y de coeficiente del edificio (no solo sobre quienes votaron).
    public decimal PercentOfTotalUnits { get; set; }
    public decimal PercentOfTotalCoefficient { get; set; }
    public bool HasAbsoluteMajority { get; set; }
}

public class VoteUnitSummaryDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public Guid? CastId { get; set; }
    public Guid? VotedOptionId { get; set; }
    public string? VotedOptionLabel { get; set; }
    public DateTime? CastAtUtc { get; set; }
}

public class VoteUpsertRequest
{
    public Guid BuildingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal QuorumPercentage { get; set; } = 50;
    public string WeightType { get; set; } = "ByUnit";
    public string? MajorityRule { get; set; }
    // null = segun el pais de la empresa: informativa fuera de Paraguay hasta que un abogado confirme su validez.
    public bool? IsInformative { get; set; }
    public List<string> OptionLabels { get; set; } = [];
}

public class VoteCastRequest
{
    public Guid UnitId { get; set; }
    public Guid VoteOptionId { get; set; }
}
