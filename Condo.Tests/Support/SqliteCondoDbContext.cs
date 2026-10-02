using Condo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Support;

/// <summary>
/// El modelo real de CondoDbContext con un unico ajuste para SQLite: las columnas "nvarchar(max)" (propias de SQL Server)
/// pierden su tipo explicito. Indices unicos filtrados, claves foraneas y demas reglas son las reales.
/// </summary>
internal sealed class SqliteCondoDbContext(DbContextOptions<CondoDbContext> options) : CondoDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
        {
            if (property.GetColumnType()?.Contains("(max)", StringComparison.OrdinalIgnoreCase) == true)
            {
                property.SetColumnType(null);
            }
        }
    }
}
