using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Condo.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Condo.Api.Services;

public class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public string CreateToken(Condo.Domain.Entities.ApplicationUser user)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration.");
        var issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Missing Jwt:Issuer configuration.");
        var audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Missing Jwt:Audience configuration.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("companyId", user.CompanyId?.ToString() ?? string.Empty),
            new Claim("condominiumId", user.CondominiumId?.ToString() ?? string.Empty)
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
