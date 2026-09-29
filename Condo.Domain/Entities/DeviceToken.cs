using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class DeviceToken : CompanyScopedEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public ApplicationUser? User { get; set; }
}
