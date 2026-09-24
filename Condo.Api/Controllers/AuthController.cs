using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Condo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    ICondoDbContext dbContext,
    IJwtTokenService jwtTokenService,
    ITenantContext tenantContext,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IConfiguration configuration) : ControllerBase
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
            // El email es único solo dentro de cada empresa (no globalmente) — si dos empresas
            // distintas dieron de alta el mismo correo, no se puede autenticar silenciosamente
            // contra el primero que devuelva la consulta, porque eso mezclaría cuentas de
            // empresas distintas. Se aplica la misma detección de ambigüedad que ya existe
            // para el nombre de usuario.
            var emailMatches = await baseQuery
                .Where(x => !x.IsDeleted && x.IsActive && x.Email == normalizedIdentifier)
                .ToListAsync(cancellationToken);

            if (emailMatches.Count > 1)
                return Unauthorized(new { error = "duplicate_email", message = "Hay más de un usuario con ese correo. Por favor ingresá con tu nombre de usuario, o contactá a tu administrador." });

            user = emailMatches.SingleOrDefault();
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

        if (user is null || !VerifyPassword(request.Password, user.PasswordHash))
            return Unauthorized();

        // Auto-rehash plain-text passwords on first login after migration
        if (!user.PasswordHash.StartsWith("$2"))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
            await dbContext.SaveChangesAsync(cancellationToken);
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

        if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            return BadRequest("Current password is invalid.");
        }

        if (!IsValidPassword(request.NewPassword))
        {
            return BadRequest("Password must have at least 8 characters, uppercase, lowercase and special character.");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.LastLoginAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Pedido de recuperacion de contraseña. Respuesta siempre generica (200, sin cuerpo) para no
    // revelar si un correo/usuario existe. Si hay coincidencias (puede haber mas de una: el mismo
    // correo puede pertenecer a cuentas de distintas empresas), se manda un correo por cada una,
    // cada uno con su propio link — asi el dueño real de la bandeja ve a que empresa/usuario
    // corresponde cada link, sin que el sistema tenga que adivinar cual eligio.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var identifier = request.Identifier?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Ok();
        }

        var normalizedIdentifier = identifier.ToLowerInvariant();
        var isEmail = identifier.Contains('@');

        var users = await dbContext.ApplicationUsers
            .Include(x => x.Company)
            .Where(x => !x.IsDeleted && x.IsActive &&
                        (isEmail ? x.Email == normalizedIdentifier : x.Username == normalizedIdentifier))
            .ToListAsync(cancellationToken);

        var frontendBaseUrl = (configuration["Frontend:BaseUrl"] ?? "https://tramiya.com.py").TrimEnd('/');
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        foreach (var user in users)
        {
            // Limite simple para que esto no se pueda usar para spamear de correos a alguien.
            var recentCount = await dbContext.PasswordResetTokens.CountAsync(
                x => x.ApplicationUserId == user.Id && x.CreatedAtUtc > DateTime.UtcNow.AddHours(-1), cancellationToken);
            if (recentCount >= 3)
            {
                continue;
            }

            var rawToken = GenerateToken();

            dbContext.PasswordResetTokens.Add(new PasswordResetToken
            {
                ApplicationUserId = user.Id,
                TokenHash = HashToken(rawToken),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(45),
                RequestedFromIp = clientIp
            });

            var resetUrl = $"{frontendBaseUrl}/reset-password?token={rawToken}";
            var scopeLabel = user.Role == Domain.Enums.UserRole.SuperAdmin ? "Superadministrador" : user.Company?.Name;
            var scopeHtml = string.IsNullOrWhiteSpace(scopeLabel) ? "" : $" en <strong>{WebUtility.HtmlEncode(scopeLabel)}</strong>";

            var html = $"""
                <p>Hola {WebUtility.HtmlEncode(user.FirstName)},</p>
                <p>Recibimos un pedido para restablecer tu contraseña{scopeHtml} (usuario: {WebUtility.HtmlEncode(user.Username)}).</p>
                <p><a href="{resetUrl}">Restablecer contraseña</a></p>
                <p>Este link vence en 45 minutos y sirve una sola vez. Si no lo pediste vos, podés ignorar este correo.</p>
                """;

            await emailSender.SendAsync(user.Email, "Restablecer tu contraseña — CONDOPY", html, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var rawToken = request.Token?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return BadRequest("El link no es válido.");
        }

        if (!IsValidPassword(request.NewPassword))
        {
            return BadRequest("La contraseña debe tener al menos 8 caracteres, mayúscula, minúscula y un carácter especial.");
        }

        var tokenHash = HashToken(rawToken);
        var resetToken = await dbContext.PasswordResetTokens
            .Include(x => x.ApplicationUser)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (resetToken?.ApplicationUser is null
            || resetToken.UsedAtUtc.HasValue
            || resetToken.ExpiresAtUtc < DateTime.UtcNow
            || resetToken.ApplicationUser.IsDeleted
            || !resetToken.ApplicationUser.IsActive)
        {
            return BadRequest("El link venció o ya fue usado. Pedí uno nuevo.");
        }

        resetToken.ApplicationUser.PasswordHash = passwordHasher.Hash(request.NewPassword);
        resetToken.ApplicationUser.MustChangePassword = false;
        resetToken.UsedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    private static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    private bool VerifyPassword(string password, string storedHash) =>
        storedHash.StartsWith("$2")
            ? passwordHasher.Verify(password, storedHash)
            : password == storedHash;

    private static bool IsValidPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) &&
        password.Length >= 8 &&
        Regex.IsMatch(password, "[A-Z]") &&
        Regex.IsMatch(password, "[a-z]") &&
        Regex.IsMatch(password, "[^A-Za-z0-9]");
}
