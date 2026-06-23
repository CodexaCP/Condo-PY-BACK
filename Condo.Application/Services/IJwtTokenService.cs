using Condo.Domain.Entities;

namespace Condo.Application.Services;

public interface IJwtTokenService
{
    string CreateToken(ApplicationUser user);
}
