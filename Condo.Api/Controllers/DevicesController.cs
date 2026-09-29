using Condo.Application.Abstractions;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

public record RegisterDeviceRequest(string Token, string Platform);
public record UnregisterDeviceRequest(string Token);

[ApiController]
[Authorize]
[Route("api/devices")]
public class DevicesController(
    ICondoDbContext dbContext,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request, CancellationToken ct)
    {
        var token = request.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var userId = tenantContext.UserId;
        var platform = string.IsNullOrWhiteSpace(request.Platform) ? "android" : request.Platform.Trim();

        var existing = await dbContext.DeviceTokens
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Token == token, ct);

        if (existing is null)
        {
            dbContext.DeviceTokens.Add(new DeviceToken
            {
                CompanyId = companyId.Value,
                UserId = userId,
                Token = token,
                Platform = platform,
                LastSeenAtUtc = DateTime.UtcNow,
                IsActive = true
            });
        }
        else
        {
            existing.CompanyId = companyId.Value;
            existing.UserId = userId;
            existing.Platform = platform;
            existing.LastSeenAtUtc = DateTime.UtcNow;
            existing.IsActive = true;
        }

        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister([FromBody] UnregisterDeviceRequest request, CancellationToken ct)
    {
        var token = request.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var userId = tenantContext.UserId;
        var existing = await dbContext.DeviceTokens
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Token == token && x.UserId == userId, ct);

        if (existing is null) return NotFound();

        existing.IsActive = false;
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }
}
