using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> GetAll(CancellationToken ct)
    {
        var userId = tenantContext.UserId;
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.RecipientId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        return Ok(notifications.Select(ToDto).ToList());
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount(CancellationToken ct)
    {
        var userId = tenantContext.UserId;
        var count = await dbContext.Notifications
            .CountAsync(x => !x.IsDeleted && x.RecipientId == userId && !x.IsRead, ct);

        return Ok(new UnreadCountDto { Count = count });
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        var userId = tenantContext.UserId;
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.RecipientId == userId, ct);

        if (notification is null) return NotFound();

        notification.IsRead = true;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var userId = tenantContext.UserId;
        var unread = await dbContext.Notifications
            .Where(x => !x.IsDeleted && x.RecipientId == userId && !x.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread) n.IsRead = true;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private static NotificationDto ToDto(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type.ToString(),
        Title = n.Title,
        Body = n.Body,
        IsRead = n.IsRead,
        EntityType = n.EntityType,
        EntityId = n.EntityId,
        CreatedAtUtc = n.CreatedAtUtc
    };
}
