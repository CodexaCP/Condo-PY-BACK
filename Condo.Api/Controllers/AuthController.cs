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

        var password = request.Password ?? string.Empty;

        if (string.IsNullOrEmpty(password))
            return Unauthorized();

        // Usuario y correo son únicos solo dentro de cada empresa, así que puede haber varias cuentas candidatas.
        // Solo cuentan las activas y con la empresa activa (el SuperAdmin no tiene empresa). Un usuario que quedó
        // sin empresa (empresa eliminada) tampoco entra.
        var candidates = await dbContext.ApplicationUsers
            .Include(x => x.Company)
            .Include(x => x.Condominium)
            .Where(x => !x.IsDeleted && x.IsActive
                        && (isEmail ? x.Email == normalizedIdentifier : x.Username == normalizedIdentifier)
                        && (x.Role == Condo.Domain.Enums.UserRole.SuperAdmin
                            || (x.Company != null && x.Company.IsActive && !x.Company.IsDeleted)))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            // Mismo costo que un intento real: el tiempo de respuesta no delata si el usuario existe.
            VerifyPassword(password, GetDummyHash());
            return Unauthorized();
        }

        // Se decide por la contraseña, no por el nombre: antes, con dos cuentas homónimas de empresas distintas
        // el login se rechazaba para AMBAS (otra empresa podía bloquear a un usuario creando un duplicado, y el
        // mensaje delataba que el correo existía en otra empresa). Ahora entra la única cuenta cuya contraseña
        // coincide; si coinciden varias, recién ahí se pide otro dato (quien lo ve ya conoce esa contraseña).
        var matching = candidates.Where(c => VerifyPassword(password, c.PasswordHash)).ToList();

        if (matching.Count == 0)
            return Unauthorized();

        if (matching.Count > 1)
        {
            return Unauthorized(new
            {
                error = "duplicate_username",
                message = isEmail
                    ? "Tu correo y contraseña coinciden con más de una cuenta. Ingresá con tu nombre de usuario, o pedile a tu administrador que cambie tu contraseña."
                    : "Tu usuario y contraseña coinciden con más de una cuenta. Ingresá con tu correo electrónico, o pedile a tu administrador que cambie tu contraseña."
            });
        }

        var user = matching[0];

        // Auto-rehash plain-text passwords on first login after migration
        if (!user.PasswordHash.StartsWith("$2"))
        {
            user.PasswordHash = passwordHasher.Hash(password);
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

    private bool VerifyPassword(string password, string storedHash)
    {
        // Una contraseña vacía nunca es válida (evita que un hash vacío deje entrar sin contraseña).
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        if (storedHash.StartsWith("$2"))
            return passwordHasher.Verify(password, storedHash);

        // Contraseña heredada en texto plano: comparación de tiempo constante.
        var provided = System.Text.Encoding.UTF8.GetBytes(password);
        var stored = System.Text.Encoding.UTF8.GetBytes(storedHash);
        return provided.Length == stored.Length && CryptographicOperations.FixedTimeEquals(provided, stored);
    }

    // Hash bcrypt "de relleno" para igualar el tiempo de respuesta cuando el usuario no existe.
    private static string? dummyHash;
    private string GetDummyHash() => dummyHash ??= passwordHasher.Hash("relleno-para-igualar-tiempos");

    private static bool IsValidPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) &&
        password.Length >= 8 &&
        Regex.IsMatch(password, "[A-Z]") &&
        Regex.IsMatch(password, "[a-z]") &&
        Regex.IsMatch(password, "[^A-Za-z0-9]");
}
