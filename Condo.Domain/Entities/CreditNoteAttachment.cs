using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Referencia a un archivo ya subido via /api/uploads (PDF, foto/escaneo o XML del documento fiscal
// oficial de la NC). Esta tabla no guarda el archivo en si, solo la URL y de que tipo es.
public class CreditNoteAttachment : CompanyScopedEntity
{
    public Guid CreditNoteId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public CreditNoteAttachmentKind Kind { get; set; } = CreditNoteAttachmentKind.Other;
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    public Company? Company { get; set; }
    public CreditNote? CreditNote { get; set; }
    public ApplicationUser? UploadedByUser { get; set; }
}
