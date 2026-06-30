namespace Condo.Application.Models;

public class NotificationDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class UnreadCountDto
{
    public int Count { get; set; }
}
