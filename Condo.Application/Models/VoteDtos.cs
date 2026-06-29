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
    public List<string> OptionLabels { get; set; } = [];
}

public class VoteCastRequest
{
    public Guid UnitId { get; set; }
    public Guid VoteOptionId { get; set; }
}
