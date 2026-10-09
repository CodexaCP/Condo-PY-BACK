-- ============================================================
-- Migracion EF: 20261009163132_ConfigCenterAuditLog
-- Centro de configuracion del edificio, fase 1: tabla FinanceAuditLogs (historial de cambios de la configuracion: quien cambio
-- que, cuando y con que valores antes y despues). Solo agrega una tabla nueva y sus indices; no toca ningun dato existente.
-- Va DESPUES de 20261006175413_AddAdsRotationSeconds.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/ConfigCenterAuditLog.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que la tabla y los indices existan de verdad; si algo falta,
-- aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009163132_ConfigCenterAuditLog'
)
BEGIN
    CREATE TABLE [FinanceAuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [Section] nvarchar(40) NOT NULL,
        [Action] nvarchar(40) NOT NULL,
        [Summary] nvarchar(500) NOT NULL,
        [EntityType] nvarchar(60) NULL,
        [EntityId] uniqueidentifier NULL,
        [ChangesJson] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        [UserEmail] nvarchar(256) NOT NULL,
        [UserRole] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_FinanceAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinanceAuditLogs_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinanceAuditLogs_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009163132_ConfigCenterAuditLog'
)
BEGIN
    CREATE INDEX [IX_FinanceAuditLogs_BuildingId_CreatedAtUtc] ON [FinanceAuditLogs] ([BuildingId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009163132_ConfigCenterAuditLog'
)
BEGIN
    CREATE INDEX [IX_FinanceAuditLogs_BuildingId_Section_CreatedAtUtc] ON [FinanceAuditLogs] ([BuildingId], [Section], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009163132_ConfigCenterAuditLog'
)
BEGIN
    CREATE INDEX [IX_FinanceAuditLogs_CompanyId] ON [FinanceAuditLogs] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009163132_ConfigCenterAuditLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009163132_ConfigCenterAuditLog', N'8.0.8');
END;
GO

IF OBJECT_ID(N'FinanceAuditLogs', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinanceAuditLogs_BuildingId_CreatedAtUtc' AND object_id = OBJECT_ID(N'FinanceAuditLogs'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinanceAuditLogs_BuildingId_Section_CreatedAtUtc' AND object_id = OBJECT_ID(N'FinanceAuditLogs'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinanceAuditLogs_CompanyId' AND object_id = OBJECT_ID(N'FinanceAuditLogs'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261009163132_ConfigCenterAuditLog')
    RAISERROR(N'ConfigCenterAuditLog: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'ConfigCenterAuditLog OK' AS Resultado,
       OBJECT_ID(N'FinanceAuditLogs', N'U') AS FinanceAuditLogs;
GO
