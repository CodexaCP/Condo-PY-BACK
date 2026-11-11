using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Vote : BaseEntity
{
    public Guid BuildingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal QuorumPercentage { get; set; } = 50;
    // El quorum se mide con el mismo peso: unidades que votaron sobre el total, o coeficiente que voto sobre el coeficiente total.
    public VoteWeightType WeightType { get; set; } = VoteWeightType.ByUnit;
    public VoteMajorityRule MajorityRule { get; set; } = VoteMajorityRule.Simple;
    // Consulta informativa: el voto en la app no reemplaza al voto formal de la asamblea. Por defecto en todos los paises hasta que
    // un abogado confirme su validez (decision de Tony, 2026-11-11).
    public bool IsInformative { get; set; }
    public VoteStatus Status { get; set; } = VoteStatus.Draft;
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? OpenedByUserId { get; set; }
    public Guid? ClosedByUserId { get; set; }

    // Resultado congelado al cerrar: lo que se muestra de una votacion cerrada sale de aca, no se recalcula con las unidades actuales.
    // ResultHash = SHA-256 (hex) del ResultJson tal cual quedo guardado. Las votaciones cerradas antes de esto no lo tienen.
    public string? ResultJson { get; set; }
    public string? ResultHash { get; set; }
    public int? ResultTotalUnits { get; set; }
    public decimal? ResultTotalCoefficient { get; set; }
    public int? ResultParticipatingUnits { get; set; }
    public decimal? ResultParticipatingCoefficient { get; set; }

    // Anulacion de una votacion abierta: los votos y su historial se conservan, pero no hay resultado.
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    // Control de concurrencia (solo SQL Server): cada voto toca la votacion, asi un voto y el cierre no se pisan.
    public byte[] RowVersion { get; set; } = [];

    public Building Building { get; set; } = null!;
    public ApplicationUser? CreatedBy { get; set; }
    public ApplicationUser? ClosedBy { get; set; }
    public ICollection<VoteOption> Options { get; set; } = new List<VoteOption>();
    public ICollection<VoteCast> Casts { get; set; } = new List<VoteCast>();
}
