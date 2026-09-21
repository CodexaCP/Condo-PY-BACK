using System.Text.Json.Serialization;
using System.Text;
using System.Reflection;
using Condo.Api.Services;
using QuestPDF.Infrastructure;
using Condo.Application.Services;
using Condo.Infrastructure;
using Condo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new Condo.Api.Serialization.UtcDateTimeConverter());
        options.JsonSerializerOptions.Converters.Add(new Condo.Api.Serialization.UtcNullableDateTimeConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCondoInfrastructure(builder.Configuration);
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IUnitOverdueService, UnitOverdueService>();
builder.Services.AddScoped<IOwnerResidencySyncService, OwnerResidencySyncService>();
builder.Services.AddScoped<Condo.Api.Services.OwnerCreditService>();
builder.Services.AddHostedService<LateFeeAccrualService>();
builder.Services.AddHostedService<PlanExpiryService>();
builder.Services.AddHostedService<OverdueAmenityReservationEnforcementService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");
// app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CondoDbContext>();
    var resetDatabaseOnStartup = builder.Configuration.GetValue("Development:ResetDatabaseOnStartup", false);

    if (app.Environment.IsDevelopment() && resetDatabaseOnStartup)
    {
        await dbContext.Database.EnsureDeletedAsync();
    }

    await ApplyDatabaseMigrationsAsync(dbContext);
}

app.Run();

async Task ApplyDatabaseMigrationsAsync(CondoDbContext dbContext)
{
    var allMigrations = dbContext.Database.GetMigrations().ToList();
    if (allMigrations.Count == 0)
    {
        return;
    }

    var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
    if (pendingMigrations.Count == 0)
    {
        return;
    }

    var hasUserTables = await HasUserTablesAsync(dbContext);
    var hasMigrationHistory = await HasMigrationHistoryAsync(dbContext);

    if (hasUserTables && !hasMigrationHistory)
    {
        await CreateMigrationHistoryBaselineAsync(dbContext, allMigrations);
    }

    await dbContext.Database.MigrateAsync();
}

async Task<bool> HasUserTablesAsync(CondoDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;
    if (shouldClose)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT CASE
            WHEN EXISTS (
                SELECT 1
                FROM sys.tables
                WHERE is_ms_shipped = 0
                  AND name <> '__EFMigrationsHistory')
            THEN 1
            ELSE 0
        END
        """;

    var result = await command.ExecuteScalarAsync();
    if (shouldClose)
    {
        await connection.CloseAsync();
    }

    return Convert.ToInt32(result) == 1;
}

async Task<bool> HasMigrationHistoryAsync(CondoDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;
    if (shouldClose)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT CASE
            WHEN OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NOT NULL
            THEN 1
            ELSE 0
        END
        """;

    var result = await command.ExecuteScalarAsync();
    if (shouldClose)
    {
        await connection.CloseAsync();
    }

    return Convert.ToInt32(result) == 1;
}

async Task CreateMigrationHistoryBaselineAsync(CondoDbContext dbContext, IReadOnlyCollection<string> migrationIds)
{
    if (migrationIds.Count == 0)
    {
        return;
    }

    var productVersion = typeof(DbContext).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion
        .Split('+')[0] ?? "8.0.0";

    await dbContext.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL
        BEGIN
            CREATE TABLE [__EFMigrationsHistory] (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
            );
        END
        """);

    foreach (var migrationId in migrationIds)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            IF NOT EXISTS (
                SELECT 1
                FROM [__EFMigrationsHistory]
                WHERE [MigrationId] = {migrationId})
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ({migrationId}, {productVersion});
            END
            """);
    }
}
