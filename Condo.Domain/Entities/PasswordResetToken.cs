using Condo.Domain.Common;

namespace Condo.Domain.Entities;

public class PasswordResetToken : BaseEntity
{
    public Guid ApplicationUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public string? RequestedFromIp { get; set; }

    public ApplicationUser? ApplicationUser { get; set; }
}
