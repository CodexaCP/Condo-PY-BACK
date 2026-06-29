using Condo.Application.Abstractions;
using Condo.Infrastructure.Persistence;
using Condo.Infrastructure.Security;
using Condo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Condo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCondoInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CondoDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("CondoDb"),
                sqlServerOptions => sqlServerOptions.EnableRetryOnFailure()));

        services.AddScoped<ICondoDbContext>(provider => provider.GetRequiredService<CondoDbContext>());
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, JwtTenantContext>();
        services.AddScoped<IAccessScopeService, AccessScopeService>();
        services.AddScoped<IExpenseSettlementDistributionService, ExpenseSettlementDistributionService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        return services;
    }
}
