namespace Condo.Application.Models;

public class VoteDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal QuorumPercentage { get; set; }
    // "ByUnit" o "ByCoefficient": tambien define como se mide el quorum.
    public string WeightType { get; set; } = "ByUnit";
    // "Simple" o "DoubleAbsolute" (mayoria absoluta sobre la totalidad, por unidades y por coeficiente a la vez).
    public string MajorityRule { get; set; } = "Simple";
    // Consulta informativa: el voto en la app no reemplaza al voto formal de la asamblea.
    public bool IsInformative { get; set; }
    // La opcion que cumple la regla: Simple = la que tiene mas peso, sin empate y con quorum; DoubleAbsolute = la que supera el 50 %
    // de las unidades Y el 50 % del coeficiente totales. null si ninguna (o si la votacion fue anulada).
    public Guid? ApprovedOptionId { get; set; }
    // La opcion con mas peso, si es una sola; con empate en el primer puesto queda null e IsTie = true.
    public Guid? WinningOptionId { get; set; }
    public bool IsTie { get; set; }
    // "Draft", "Open", "Closed" o "Cancelled".
    public string Status { get; set; } = "Draft";
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string ClosedByName { get; set; } = string.Empty;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int TotalUnits { get; set; }
    public int ParticipatingUnits { get; set; }
    public decimal TotalCoefficient { get; set; }
    public decimal ParticipatingCoefficient { get; set; }
    public bool QuorumReached { get; set; }
    // true cuando el resultado sale de lo congelado al cerrar (o anular); false si se calcula con las unidades actuales (abierta, o
    // cerrada antes de que existiera el congelamiento).
    public bool ResultFrozen { get; set; }
    // SHA-256 del resultado congelado, para verificar que no cambio.
    public string? ResultHash { get; set; }
    // Lo que el usuario puede hacer con esta votacion segun su rol (crear/editar/abrir/cerrar/anular, y cargar votos presenciales).
    public bool CanManage { get; set; }
    public bool CanCast { get; set; }
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
    // "Owner" (voto del propietario) o "Staff" (cargado por el personal, p. ej. en asamblea presencial).
    public string? CastSource { get; set; }
    // Cantidad de movimientos en el historial de la unidad (voto, cambios y quitas).
    public int HistoryCount { get; set; }
}

public class VoteCastEventDto
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    // "Cast", "Change" o "Remove".
    public string Action { get; set; } = string.Empty;
    public string? PreviousOptionLabel { get; set; }
    public string? NewOptionLabel { get; set; }
    public decimal CoefficientWeight { get; set; }
    // "Owner" o "Staff".
    public string Source { get; set; } = string.Empty;
    public string ActingUserName { get; set; } = string.Empty;
    public string OwnerUserName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public class VoteUpsertRequest
{
    public Guid BuildingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal QuorumPercentage { get; set; } = 50;
    public string WeightType { get; set; } = "ByUnit";
    public string? MajorityRule { get; set; }
    // null = informativa (en todos los paises, hasta que un abogado confirme la validez del voto en la app).
    public bool? IsInformative { get; set; }
    public List<string> OptionLabels { get; set; } = [];
}

public class VoteCastRequest
{
    public Guid UnitId { get; set; }
    public Guid VoteOptionId { get; set; }
}

public class VoteCancelRequest
{
    public string Reason { get; set; } = string.Empty;
}

// Vista del propietario: sin el voto de las demás unidades; resultados por opción solo al cerrar.
public class OwnerVoteDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Open";
    public string WeightType { get; set; } = "ByUnit";
    public string MajorityRule { get; set; } = "Simple";
    public bool IsInformative { get; set; }
    public decimal QuorumPercentage { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public int TotalUnits { get; set; }
    public int ParticipatingUnits { get; set; }
    public bool QuorumReached { get; set; }
    public bool ResultsVisible { get; set; }
    public Guid? ApprovedOptionId { get; set; }
    public Guid? WinningOptionId { get; set; }
    public bool IsTie { get; set; }
    public List<OwnerVoteOptionDto> Options { get; set; } = [];
    public List<OwnerVoteUnitDto> MyUnits { get; set; } = [];
}

public class OwnerVoteOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    // null mientras no se ven resultados (abierta o anulada).
    public int? CastCount { get; set; }
    public decimal? Percentage { get; set; }
}

public class OwnerVoteUnitDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public Guid? VotedOptionId { get; set; }
    public string? VotedOptionLabel { get; set; }
    public DateTime? CastAtUtc { get; set; }
    // "Owner" | "Staff" | null
    public string? CastSource { get; set; }
    public bool CanVote { get; set; }
    // Por qué no puede votar (null si puede): texto para mostrar tal cual.
    public string? CannotVoteReason { get; set; }
}
