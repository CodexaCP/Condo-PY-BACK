using Condo.Domain.Common;

namespace Condo.Domain.Entities;

// Una linea = un cargo del comprobante original que se reduce, y por cuanto. Al aprobarse la NC,
// cada linea genera su propio ExpenseCharge de reverso (ver ExpenseCharge.SourceCreditNoteId).
public class CreditNoteLine : CompanyScopedEntity
{
    public Guid CreditNoteId { get; set; }
    public Guid ExpenseChargeId { get; set; }
    public decimal Amount { get; set; }
    public string? Concept { get; set; }

    public Company? Company { get; set; }
    public CreditNote? CreditNote { get; set; }
    public ExpenseCharge? ExpenseCharge { get; set; }
}
