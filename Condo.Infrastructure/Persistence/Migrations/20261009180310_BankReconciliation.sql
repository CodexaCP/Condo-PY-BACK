-- ============================================================
-- Migracion EF: 20261009180310_BankReconciliation
-- Finanzas, conciliacion bancaria manual: tablas BankReconciliations (una conciliacion por cuenta bancaria y fecha de corte, con el saldo
-- del extracto; a lo sumo una abierta por cuenta) y BankReconciledMovements (los movimientos del libro marcados como conciliados; un movimiento
-- no se concilia dos veces entre las marcas vigentes). Solo agrega tablas e indices; no toca datos existentes.
-- Va DESPUES de 20261009174212_SuppliersPayablesVat.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/BankReconciliation.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que las tablas e indices existan de verdad; si algo falta,
-- aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009180310_BankReconciliation'
)
BEGIN
    CREATE TABLE [BankReconciliations] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [AccountId] uniqueidentifier NOT NULL,
        [StatementDate] date NOT NULL,
        [StatementBalance] decimal(18,2) NOT NULL,
        [Notes] nvarchar(500) NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CompletedByUserId] uniqueidentifier NULL,
        [ReconciledBalanceAtCompletion] decimal(18,2) NULL,
        [ReopenedAtUtc] datetime2 NULL,
        [ReopenedByUserId] uniqueidentifier NULL,
        [ReopenReason] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BankReconciliations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BankReconciliations_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BankReconciliations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BankReconciliations_FinancialAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [FinancialAccounts] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009180310_BankReconciliation'
)
BEGIN
    CREATE TABLE [BankReconciledMovements] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [AccountId] uniqueidentifier NOT NULL,
        [ReconciliationId] uniqueidentifier NOT NULL,
        [SourceType] int NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [MarkedByUserId] uniqueidentifier NOT NULL,
        [MarkedAtUtc] datetime2 NOT NULL,
        [UnmarkedAtUtc] datetime2 NULL,
        [UnmarkedByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BankReconciledMovements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BankReconciledMovements_BankReconciliations_ReconciliationId] FOREIGN KEY ([ReconciliationId]) REFERENCES [BankReconciliations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BankReconciledMovements_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BankReconciledMovements_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BankReconciledMovements_FinancialAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [FinancialAccounts] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009180310_BankReconciliation'
)
BEGIN
    CREATE INDEX [IX_BankReconciledMovements_AccountId] ON [BankReconciledMovements] ([AccountId]);
    CREATE INDEX [IX_BankReconciledMovements_CompanyId] ON [BankReconciledMovements] ([CompanyId]);
    CREATE INDEX [IX_BankReconciledMovements_ReconciliationId] ON [BankReconciledMovements] ([ReconciliationId]);
    CREATE INDEX [IX_BankReconciliations_BuildingId_AccountId_StatementDate] ON [BankReconciliations] ([BuildingId], [AccountId], [StatementDate]);
    CREATE INDEX [IX_BankReconciliations_CompanyId] ON [BankReconciliations] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009180310_BankReconciliation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BankReconciledMovements_BuildingId_AccountId_SourceType_SourceId] ON [BankReconciledMovements] ([BuildingId], [AccountId], [SourceType], [SourceId]) WHERE [IsDeleted] = 0');
    EXEC(N'CREATE UNIQUE INDEX [IX_BankReconciliations_AccountId] ON [BankReconciliations] ([AccountId]) WHERE [IsDeleted] = 0 AND [Status] = ''Open''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009180310_BankReconciliation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009180310_BankReconciliation', N'8.0.8');
END;
GO

IF OBJECT_ID(N'BankReconciliations', N'U') IS NULL
   OR OBJECT_ID(N'BankReconciledMovements', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BankReconciliations_AccountId' AND object_id = OBJECT_ID(N'BankReconciliations'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BankReconciliations_BuildingId_AccountId_StatementDate' AND object_id = OBJECT_ID(N'BankReconciliations'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BankReconciliations_CompanyId' AND object_id = OBJECT_ID(N'BankReconciliations'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BankReconciledMovements_BuildingId_AccountId_SourceType_SourceId' AND object_id = OBJECT_ID(N'BankReconciledMovements'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BankReconciledMovements_ReconciliationId' AND object_id = OBJECT_ID(N'BankReconciledMovements'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BankReconciledMovements_AccountId' AND object_id = OBJECT_ID(N'BankReconciledMovements'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BankReconciledMovements_CompanyId' AND object_id = OBJECT_ID(N'BankReconciledMovements'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261009180310_BankReconciliation')
    RAISERROR(N'BankReconciliation: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'BankReconciliation OK' AS Resultado,
       OBJECT_ID(N'BankReconciliations', N'U') AS Conciliaciones,
       OBJECT_ID(N'BankReconciledMovements', N'U') AS Marcas;
GO
