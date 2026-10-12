-- ============================================================
-- Migracion EF: 20261012185825_PaymentConnections
-- Pagos online, configuracion de Bancard: tablas PaymentConnections (conexion de la empresa con el comercio del proveedor, con las claves
-- privadas CIFRADAS), BuildingPaymentConfigs (edificios a los que sirve cada conexion) y PaymentConnectionAudits (historial de cambios).
-- Solo agrega tablas e indices; no toca datos existentes ni cobra nada.
-- Va DESPUES de 20261009183045_AccountingRoles.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/PaymentConnections.sql
--
-- IMPORTANTE: ademas del SQL hace falta la clave maestra del servidor (variable Payments__MasterKey del servicio). Ver
-- docs/GUIA_PRUEBAS_PAGOS_BANCARD_CONFIG.md. Sin ella la pantalla funciona pero no deja guardar claves privadas.
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que las tablas y los indices existan de verdad; si algo falta,
-- aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012185825_PaymentConnections'
)
BEGIN
    CREATE TABLE [PaymentConnections] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [ProviderCode] nvarchar(30) NOT NULL,
        [ProductCode] nvarchar(30) NOT NULL,
        [Environment] nvarchar(20) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [PublicId] nvarchar(32) NOT NULL,
        [SettingsJson] nvarchar(2000) NULL,
        [SecretsCipher] nvarchar(4000) NULL,
        [KeyId] nvarchar(30) NULL,
        [SecretsFingerprint] nvarchar(500) NULL,
        [SecretsUpdatedAtUtc] datetime2 NULL,
        [StatusChangedAtUtc] datetime2 NULL,
        [StatusChangedByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PaymentConnections] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentConnections_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012185825_PaymentConnections'
)
BEGIN
    CREATE TABLE [BuildingPaymentConfigs] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [ConnectionId] uniqueidentifier NOT NULL,
        [IsEnabled] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BuildingPaymentConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BuildingPaymentConfigs_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingPaymentConfigs_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingPaymentConfigs_PaymentConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [PaymentConnections] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012185825_PaymentConnections'
)
BEGIN
    CREATE TABLE [PaymentConnectionAudits] (
        [Id] uniqueidentifier NOT NULL,
        [ConnectionId] uniqueidentifier NOT NULL,
        [Action] nvarchar(40) NOT NULL,
        [Summary] nvarchar(500) NOT NULL,
        [ChangesJson] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        [UserEmail] nvarchar(256) NOT NULL,
        [UserRole] nvarchar(40) NOT NULL,
        [Ip] nvarchar(64) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PaymentConnectionAudits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentConnectionAudits_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentConnectionAudits_PaymentConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [PaymentConnections] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012185825_PaymentConnections'
)
BEGIN
    CREATE INDEX [IX_BuildingPaymentConfigs_CompanyId] ON [BuildingPaymentConfigs] ([CompanyId]);
    CREATE INDEX [IX_BuildingPaymentConfigs_ConnectionId] ON [BuildingPaymentConfigs] ([ConnectionId]);
    CREATE INDEX [IX_PaymentConnectionAudits_CompanyId] ON [PaymentConnectionAudits] ([CompanyId]);
    CREATE INDEX [IX_PaymentConnectionAudits_ConnectionId_CreatedAtUtc] ON [PaymentConnectionAudits] ([ConnectionId], [CreatedAtUtc]);
    CREATE UNIQUE INDEX [IX_PaymentConnections_PublicId] ON [PaymentConnections] ([PublicId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012185825_PaymentConnections'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BuildingPaymentConfigs_BuildingId_ConnectionId] ON [BuildingPaymentConfigs] ([BuildingId], [ConnectionId]) WHERE [IsDeleted] = 0');
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentConnections_CompanyId_Name] ON [PaymentConnections] ([CompanyId], [Name]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012185825_PaymentConnections'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012185825_PaymentConnections', N'8.0.8');
END;
GO

IF OBJECT_ID(N'PaymentConnections', N'U') IS NULL
   OR OBJECT_ID(N'BuildingPaymentConfigs', N'U') IS NULL
   OR OBJECT_ID(N'PaymentConnectionAudits', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentConnections_PublicId' AND object_id = OBJECT_ID(N'PaymentConnections'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentConnections_CompanyId_Name' AND object_id = OBJECT_ID(N'PaymentConnections'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPaymentConfigs_BuildingId_ConnectionId' AND object_id = OBJECT_ID(N'BuildingPaymentConfigs'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPaymentConfigs_CompanyId' AND object_id = OBJECT_ID(N'BuildingPaymentConfigs'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPaymentConfigs_ConnectionId' AND object_id = OBJECT_ID(N'BuildingPaymentConfigs'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentConnectionAudits_CompanyId' AND object_id = OBJECT_ID(N'PaymentConnectionAudits'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentConnectionAudits_ConnectionId_CreatedAtUtc' AND object_id = OBJECT_ID(N'PaymentConnectionAudits'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261012185825_PaymentConnections')
    RAISERROR(N'PaymentConnections: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'PaymentConnections OK' AS Resultado,
       OBJECT_ID(N'PaymentConnections', N'U') AS Conexiones,
       OBJECT_ID(N'BuildingPaymentConfigs', N'U') AS Edificios,
       OBJECT_ID(N'PaymentConnectionAudits', N'U') AS Historial;
GO
