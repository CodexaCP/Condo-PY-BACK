using System.Text.RegularExpressions;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(ICondoDbContext dbContext, IJwtTokenService jwtTokenService, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var identifier = request.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identifier))
            return Unauthorized();

        var normalizedIdentifier = identifier.ToLowerInvariant();
        bool isEmail = identifier.Contains('@');

        var baseQuery = dbContext.ApplicationUsers
            .Include(x => x.Company)
            .Include(x => x.Condominium);

        Domain.Entities.ApplicationUser? user;
        if (isEmail)
        {
            user = await baseQuery
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.Email == normalizedIdentifier, cancellationToken);
        }
        else
        {
            var matches = await baseQuery
                .Where(x => !x.IsDeleted && x.IsActive && x.Username == normalizedIdentifier)
                .ToListAsync(cancellationToken);

            if (matches.Count > 1)
                return Unauthorized(new { error = "duplicate_username", message = "Hay más de un usuario con ese nombre de usuario. Por favor ingresá con tu correo electrónico." });

            user = matches.SingleOrDefault();
        }

        if (user is null || user.PasswordHash != request.Password)
        {
            return Unauthorized();
        }

        var token = jwtTokenService.CreateToken(user);

        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(8),
            UserId = user.Id,
            CompanyId = user.CompanyId,
            CompanyName = user.Company?.Name,
            CondominiumId = user.CondominiumId,
            CondominiumName = user.Condominium?.Name,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            ScopeLabel = user.Role == Condo.Domain.Enums.UserRole.SuperAdmin
                ? "Vision global de plataforma"
                : user.Company?.Name ?? "Empresa administradora",
            MustChangePassword = user.MustChangePassword
        });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await dbContext.ApplicationUsers.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == tenantContext.UserId, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (user.PasswordHash != request.CurrentPassword)
        {
            return BadRequest("Current password is invalid.");
        }

        if (!IsValidPassword(request.NewPassword))
        {
            return BadRequest("Password must have at least 8 characters, uppercase, lowercase and special character.");
        }

        user.PasswordHash = request.NewPassword;
        user.MustChangePassword = false;
        user.LastLoginAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static bool IsValidPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) &&
        password.Length >= 8 &&
        Regex.IsMatch(password, "[A-Z]") &&
        Regex.IsMatch(password, "[a-z]") &&
        Regex.IsMatch(password, "[^A-Za-z0-9]");
}
