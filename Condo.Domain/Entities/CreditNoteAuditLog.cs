using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class CreditNoteAuditLog : CompanyScopedEntity
{
    public Guid? CreditNoteId { get; set; }
    public CreditNoteAuditAction Action { get; set; }
    public Guid UserId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? DatosAntesJson { get; set; }
    public string? DatosDespuesJson { get; set; }
    public string? Detalle { get; set; }

    public Company? Company { get; set; }
    public CreditNote? CreditNote { get; set; }
}
