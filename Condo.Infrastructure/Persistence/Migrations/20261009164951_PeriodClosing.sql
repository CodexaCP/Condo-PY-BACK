-- ============================================================
-- Migracion EF: 20261009164951_PeriodClosing
-- Centro de configuracion del edificio, fase 2 (cierre de periodo): agrega FinanceSettings.PeriodClosingEnabled (interruptor del
-- cierre por edificio, apagado por defecto: los edificios existentes no cambian de comportamiento) y la tabla FinancePeriodClosures
-- (un cierre por edificio y mes; reabrir conserva la fila con quien, cuando y por que).
-- Va DESPUES de 20261009163132_ConfigCenterAuditLog.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/PeriodClosing.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009164951_PeriodClosing'
)
BEGIN
    ALTER TABLE [FinanceSettings] ADD [PeriodClosingEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009164951_PeriodClosing'
)
BEGIN
    CREATE TABLE [FinancePeriodClosures] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        [ClosedAtUtc] datetime2 NOT NULL,
        [ClosedByUserId] uniqueidentifier NOT NULL,
        [ReopenedAtUtc] datetime2 NULL,
        [ReopenedByUserId] uniqueidentifier NULL,
        [ReopenReason] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_FinancePeriodClosures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinancePeriodClosures_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinancePeriodClosures_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009164951_PeriodClosing'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FinancePeriodClosures_BuildingId_Year_Month] ON [FinancePeriodClosures] ([BuildingId], [Year], [Month]) WHERE [IsDeleted] = 0 AND [ReopenedAtUtc] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009164951_PeriodClosing'
)
BEGIN
    CREATE INDEX [IX_FinancePeriodClosures_BuildingId_Year_Month_ReopenedAtUtc] ON [FinancePeriodClosures] ([BuildingId], [Year], [Month], [ReopenedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009164951_PeriodClosing'
)
BEGIN
    CREATE INDEX [IX_FinancePeriodClosures_CompanyId] ON [FinancePeriodClosures] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009164951_PeriodClosing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009164951_PeriodClosing', N'8.0.8');
END;
GO

IF COL_LENGTH(N'FinanceSettings', N'PeriodClosingEnabled') IS NULL
   OR OBJECT_ID(N'FinancePeriodClosures', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinancePeriodClosures_BuildingId_Year_Month' AND object_id = OBJECT_ID(N'FinancePeriodClosures'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinancePeriodClosures_BuildingId_Year_Month_ReopenedAtUtc' AND object_id = OBJECT_ID(N'FinancePeriodClosures'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinancePeriodClosures_CompanyId' AND object_id = OBJECT_ID(N'FinancePeriodClosures'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261009164951_PeriodClosing')
    RAISERROR(N'PeriodClosing: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'PeriodClosing OK' AS Resultado,
       COL_LENGTH(N'FinanceSettings', N'PeriodClosingEnabled') AS Columna,
       OBJECT_ID(N'FinancePeriodClosures', N'U') AS Tabla;
GO
