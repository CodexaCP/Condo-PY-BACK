using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

public class Notification : CompanyScopedEntity
{
    public Guid RecipientId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }

    public ApplicationUser? Recipient { get; set; }
    public Company? Company { get; set; }
}
