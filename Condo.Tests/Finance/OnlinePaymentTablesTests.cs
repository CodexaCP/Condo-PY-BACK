using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Condo.Tests.Finance;

/// <summary>
/// Pagos online, base del cobro: las tablas de cobros, intentos y eventos, sus indices unicos y que la migracion y su .sql para el VPS
/// coincidan con el modelo. Las garantias de "un solo pago por cobro" viven en la base, no en el codigo: se prueban aca.
/// </summary>
public class OnlinePaymentTablesTests : IDisposable
{
    private const string MigrationId = "20261020143213_OnlinePaymentIntents";
    private readonly FinanceEnv _env = new();

    public void Dispose() => _env.Dispose();

    // ── La migracion coincide con el modelo ──────────────────────────────────

    // Proveedor de SQL Server sin conectarse: es el que genera las migraciones, asi que su snapshot es el que hay que comparar.
    private static CondoDbContext SqlServerModel() => new(new DbContextOptionsBuilder<CondoDbContext>()
        .UseSqlServer("Server=(localdb)\\unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options);

    [Fact]
    public void ElModeloNoTieneCambiosSinMigrar()
    {
        using var db = SqlServerModel();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshot);
        if (snapshot is IMutableModel mutable) snapshot = mutable.FinalizeModel();
        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);

        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshot.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.Empty(differences.Select(d => d.GetType().Name + ": " + d));
    }

    [Fact]
    public void LaMigracionEstaRegistradaYEsLaUltima()
    {
        using var db = SqlServerModel();
        var migrations = db.Database.GetMigrations().ToList();

        Assert.Contains(MigrationId, migrations);
        // Puede haber migraciones posteriores (otros cambios); lo que importa es que esta siga registrada despues de la anterior.
        Assert.True(string.CompareOrdinal(MigrationId, migrations[^1]) <= 0);
    }

    private static string MigrationSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Condo.Infrastructure"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "Condo.Infrastructure", "Persistence", "Migrations", $"{MigrationId}.sql"));
    }

    [Fact]
    public void ElSqlParaElVpsEsIdempotenteYTransaccional()
    {
        var sql = MigrationSql();
        var batches = sql.Split("\nGO", StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim()).Where(b => b.Length > 0).ToList();

        // Cada lote que cambia la base revisa antes el historial de migraciones.
        var changing = batches.Where(b => System.Text.RegularExpressions.Regex.IsMatch(
            b, @"^\s*(CREATE|ALTER|INSERT INTO|EXEC)\b", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && !b.StartsWith("--")).ToList();
        Assert.NotEmpty(changing);
        Assert.All(changing, b =>
        {
            Assert.Contains("IF NOT EXISTS", b);
            Assert.Contains(MigrationId, b);
        });
        Assert.Contains("BEGIN TRANSACTION", sql);
        Assert.Contains("COMMIT", sql);
        Assert.Contains("RAISERROR", sql);                                    // verificacion final antes del COMMIT
        Assert.True(sql.IndexOf("RAISERROR", StringComparison.Ordinal) < sql.LastIndexOf("COMMIT", StringComparison.Ordinal));
        // Solo agrega: nada de borrar ni modificar tablas, columnas o datos existentes.
        Assert.DoesNotMatch(@"(?i)\bDROP\s+(TABLE|COLUMN|INDEX|CONSTRAINT)\b", sql);
        Assert.DoesNotMatch(@"(?i)\bDELETE\s+FROM\b", sql);
        Assert.DoesNotMatch(@"(?i)\bUPDATE\s+\[", sql);
        Assert.DoesNotMatch(@"(?i)\bALTER\s+COLUMN\b", sql);
    }

    [Fact]
    public void ElSqlCreaTodasLasTablasColumnasEIndicesDelModelo()
    {
        var sql = MigrationSql();
        var model = _env.T.Db.Model;

        foreach (var type in new[] { typeof(PaymentIntent), typeof(PaymentIntentItem), typeof(PaymentAttempt), typeof(PaymentEvent) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Contains($"CREATE TABLE [{entity.GetTableName()}]", sql);
            foreach (var property in entity.GetProperties())
            {
                Assert.Contains($"[{property.GetColumnName()}]", sql);
            }

            foreach (var index in entity.GetIndexes())
            {
                Assert.Contains($"[{index.GetDatabaseName()}]", sql);
            }
        }

        var ownerPayments = model.FindEntityType(typeof(OwnerPayment))!;
        Assert.Contains("[IX_OwnerPayments_PaymentIntentId]", sql);
        Assert.Contains("FK_OwnerPayments_PaymentIntents_PaymentIntentId", sql);
        Assert.NotNull(ownerPayments.FindProperty(nameof(OwnerPayment.PaymentIntentId)));
        Assert.Contains("[HolderName]", sql);
        Assert.Contains("[HolderTaxId]", sql);
        Assert.Contains("[HolderBankNote]", sql);
        Assert.Contains("[IsTest] bit NOT NULL DEFAULT CAST(0 AS bit)", sql);
    }

    [Fact]
    public void ElSqlNoCreaTablasDeLiquidacionPorLaDecisionD7()
    {
        var sql = MigrationSql();

        Assert.DoesNotContain("PaymentSettlement", sql);
    }

    // ── Garantias en la base ─────────────────────────────────────────────────

    private (PaymentConnection Connection, ApplicationUser Owner, Unit Unit, ExpensePeriod Period) Arrange()
    {
        var connection = new PaymentConnection
        {
            CompanyId = _env.Company.Id, Name = "Bancard", ProviderCode = "bancard", ProductCode = "tpago", PublicId = Guid.NewGuid().ToString("N")
        };
        var unit = _env.T.AddUnit(_env.Building);
        var owner = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.Db.PaymentConnections.Add(connection);
        _env.T.Db.SaveChanges();
        return (connection, owner, unit, _env.Period);
    }

    private PaymentIntent NewIntent(PaymentConnection connection, ApplicationUser owner, long number, params (Unit Unit, ExpensePeriod Period, bool Open)[] items)
    {
        var intent = new PaymentIntent
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, OwnerId = owner.Id, ConnectionId = connection.Id, Number = number,
            Amount = 100_000m, HolderName = "Administradora Sol", HolderTaxId = "80012345-6"
        };
        foreach (var (unit, period, open) in items)
        {
            intent.Items.Add(new PaymentIntentItem
            {
                CompanyId = _env.Company.Id, UnitId = unit.Id, ExpensePeriodId = period.Id, Amount = 100_000m, IsOpen = open
            });
        }

        return intent;
    }

    private PaymentAttempt NewAttempt(PaymentIntent intent, long number, string reference, string? session = null) => new()
    {
        CompanyId = _env.Company.Id, IntentId = intent.Id, ConnectionId = intent.ConnectionId, AttemptNumber = number,
        ProviderReference = reference, ProviderSessionId = session, Amount = intent.Amount
    };

    [Fact]
    public void UnCobroSeGuardaYSeLeeConSusItemsIntentosYEstadosComoTexto()
    {
        var (connection, owner, unit, period) = Arrange();
        var intent = NewIntent(connection, owner, 1, (unit, period, true));
        intent.Attempts.Add(NewAttempt(intent, 1, "1", "PEQJN72536"));
        _env.T.Db.PaymentIntents.Add(intent);
        _env.T.Db.SaveChanges();

        var read = _env.T.NewContext().PaymentIntents.Include(x => x.Items).Include(x => x.Attempts).Single();

        Assert.Equal(PaymentIntentStatus.Open, read.Status);
        Assert.Equal("PYG", read.Currency);
        Assert.Equal("Administradora Sol", read.HolderName);
        Assert.Single(read.Items);
        Assert.Equal(PaymentAttemptStatus.Created, Assert.Single(read.Attempts).Status);
        Assert.Null(read.Attempts.Single().CommissionAmount);   // la comision no se fija todavia
        Assert.Null(read.Attempts.Single().NetAmount);
    }

    [Fact]
    public void UnComprobanteNoPuedeEstarEnDosCobrosAbiertosPeroSiDespuesDeCerrarElPrimero()
    {
        var (connection, owner, unit, period) = Arrange();
        var first = NewIntent(connection, owner, 1, (unit, period, true));
        _env.T.Db.PaymentIntents.Add(first);
        _env.T.Db.SaveChanges();

        _env.T.Db.PaymentIntents.Add(NewIntent(connection, owner, 2, (unit, period, true)));
        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
        _env.T.Db.ChangeTracker.Clear();

        // Un cobro cerrado (IsOpen = 0) no reserva el comprobante: se puede cobrar de nuevo.
        _env.T.Db.PaymentIntentItems.Single(x => x.IntentId == first.Id).IsOpen = false;
        _env.T.Db.SaveChanges();
        _env.T.Db.PaymentIntents.Add(NewIntent(connection, owner, 2, (unit, period, true)));
        _env.T.Db.SaveChanges();

        Assert.Equal(2, _env.T.NewContext().PaymentIntents.Count());
    }

    [Fact]
    public void LosNumerosDeCobroEIntentoSonUnicos()
    {
        var (connection, owner, unit, period) = Arrange();
        var first = NewIntent(connection, owner, 7, (unit, period, false));
        first.Attempts.Add(NewAttempt(first, 5, "5"));
        _env.T.Db.PaymentIntents.Add(first);
        _env.T.Db.SaveChanges();

        _env.T.Db.PaymentIntents.Add(NewIntent(connection, owner, 7, (unit, period, false)));
        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
        _env.T.Db.ChangeTracker.Clear();

        var second = NewIntent(connection, owner, 8, (unit, period, false));
        second.Attempts.Add(NewAttempt(second, 5, "6"));
        _env.T.Db.PaymentIntents.Add(second);
        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
    }

    [Fact]
    public void LaReferenciaYLaSesionDelProveedorSonUnicasPorConexionYLaSesionNulaSePuedeRepetir()
    {
        var (connection, owner, unit, period) = Arrange();
        var intent = NewIntent(connection, owner, 1, (unit, period, false));
        intent.Attempts.Add(NewAttempt(intent, 1, "A-1", "ALIAS-1"));
        intent.Attempts.Add(NewAttempt(intent, 2, "A-2"));   // sin sesion todavia
        intent.Attempts.Add(NewAttempt(intent, 3, "A-3"));   // otra sin sesion: permitido
        _env.T.Db.PaymentIntents.Add(intent);
        _env.T.Db.SaveChanges();

        _env.T.Db.PaymentAttempts.Add(NewAttempt(intent, 4, "A-1"));
        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
        _env.T.Db.ChangeTracker.Clear();

        _env.T.Db.PaymentAttempts.Add(NewAttempt(intent, 5, "A-5", "ALIAS-1"));
        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
        _env.T.Db.ChangeTracker.Clear();

        // La misma referencia en OTRA conexion no choca.
        var other = new PaymentConnection
        {
            CompanyId = _env.Company.Id, Name = "Otra", ProviderCode = "bancard", ProductCode = "vpos", PublicId = Guid.NewGuid().ToString("N")
        };
        _env.T.Db.PaymentConnections.Add(other);
        _env.T.Db.SaveChanges();
        _env.T.Db.PaymentAttempts.Add(new PaymentAttempt
        {
            CompanyId = _env.Company.Id, IntentId = intent.Id, ConnectionId = other.Id, AttemptNumber = 6, ProviderReference = "A-1",
            ProviderSessionId = "ALIAS-1", Amount = 1m
        });
        _env.T.Db.SaveChanges();

        Assert.Equal(4, _env.T.NewContext().PaymentAttempts.Count());
    }

    private PaymentEvent NewEvent(PaymentConnection connection, string? dedupe) => new()
    {
        CompanyId = _env.Company.Id, ConnectionId = connection.Id, Direction = PaymentEventDirection.Inbound, Kind = "Callback",
        DedupeKey = dedupe, Outcome = "Applied", PayloadJson = "{}"
    };

    [Fact]
    public void UnMismoAvisoNoSePuedeRegistrarDosVecesPeroLosEventosSinClavePuedenRepetirse()
    {
        var (connection, _, _, _) = Arrange();
        _env.T.Db.PaymentEvents.Add(NewEvent(connection, "bancard:2119009607:confirmed"));
        _env.T.Db.PaymentEvents.Add(NewEvent(connection, null));
        _env.T.Db.PaymentEvents.Add(NewEvent(connection, null));
        _env.T.Db.SaveChanges();

        _env.T.Db.PaymentEvents.Add(NewEvent(connection, "bancard:2119009607:confirmed"));
        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
        _env.T.Db.ChangeTracker.Clear();

        _env.T.Db.PaymentEvents.Add(NewEvent(connection, "bancard:2119009607:reversed"));
        _env.T.Db.SaveChanges();   // otro estado de la misma transaccion es otro evento

        Assert.Equal(4, _env.T.NewContext().PaymentEvents.Count());
    }

    [Fact]
    public void LosEventosDeCobrosOnlineSonDeSoloInsercion()
    {
        var (connection, _, _, _) = Arrange();
        var evt = NewEvent(connection, "k1");
        _env.T.Db.PaymentEvents.Add(evt);
        _env.T.Db.SaveChanges();

        evt.Outcome = "Failed";
        Assert.Throws<InvalidOperationException>(() => _env.T.Db.SaveChanges());
        _env.T.Db.ChangeTracker.Clear();

        var tracked = _env.T.Db.PaymentEvents.Single();
        _env.T.Db.PaymentEvents.Remove(tracked);
        Assert.Throws<InvalidOperationException>(() => _env.T.Db.SaveChanges());
        _env.T.Db.ChangeTracker.Clear();

        Assert.Equal("Applied", _env.T.NewContext().PaymentEvents.Single().Outcome);
    }

    [Fact]
    public void UnCobroProduceComoMaximoUnPagoDePropietario()
    {
        var (connection, owner, unit, period) = Arrange();
        var intent = NewIntent(connection, owner, 1, (unit, period, false));
        _env.T.Db.PaymentIntents.Add(intent);
        _env.T.Db.SaveChanges();

        OwnerPayment Pay(string reference, Guid? intentId) => new()
        {
            CompanyId = _env.Company.Id, OwnerId = owner.Id, PaymentDate = new DateOnly(2026, 10, 1), DeclaredAmount = 1m, Reference = reference,
            Channel = OwnerPaymentChannel.Online, Method = PaymentMethod.Online, PaymentIntentId = intentId
        };

        // Los pagos que no vienen de un cobro (sin PaymentIntentId) no se ven afectados: pueden ser muchos.
        _env.T.Db.OwnerPayments.Add(Pay("PAY-2026-000001", null));
        _env.T.Db.OwnerPayments.Add(Pay("PAY-2026-000002", null));
        _env.T.Db.OwnerPayments.Add(Pay("PAY-2026-000003", intent.Id));
        _env.T.Db.SaveChanges();

        _env.T.Db.OwnerPayments.Add(Pay("PAY-2026-000004", intent.Id));
        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
    }

    [Fact]
    public void ElPagoDeUnCobroInexistenteSeRechazaPorLaClaveForanea()
    {
        var (_, owner, _, _) = Arrange();
        _env.T.Db.OwnerPayments.Add(new OwnerPayment
        {
            CompanyId = _env.Company.Id, OwnerId = owner.Id, PaymentDate = new DateOnly(2026, 10, 1), DeclaredAmount = 1m,
            Reference = "PAY-2026-000009", PaymentIntentId = Guid.NewGuid()
        });

        Assert.Throws<DbUpdateException>(() => _env.T.Db.SaveChanges());
    }

    [Fact]
    public void LaConexionGuardaElTitularYElEdificioPuedeSerDePrueba()
    {
        var (connection, _, _, _) = Arrange();
        connection.HolderName = "Consorcio Edificio Sol";
        connection.HolderTaxId = "80099999-1";
        connection.HolderBankNote = "Cuenta corriente Itaú";
        var config = new BuildingPaymentConfig { CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, ConnectionId = connection.Id, IsTest = true };
        _env.T.Db.BuildingPaymentConfigs.Add(config);
        _env.T.Db.SaveChanges();

        var db = _env.T.NewContext();
        Assert.Equal("80099999-1", db.PaymentConnections.Single().HolderTaxId);
        Assert.True(db.BuildingPaymentConfigs.Single().IsTest);
        // Por defecto un edificio NO es de prueba.
        Assert.False(new BuildingPaymentConfig().IsTest);
    }
}
