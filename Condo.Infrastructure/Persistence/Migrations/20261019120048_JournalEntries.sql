-- ============================================================
-- Migracion EF: 20261019120048_JournalEntries
-- Finanzas, Libro Diario: tablas JournalEntries (asientos asentados con numero correlativo por edificio y ejercicio), JournalEntryLines
-- (sus renglones, con copia del codigo y nombre de la cuenta) y JournalSequences (ultimo numero usado), y la columna
-- FinanceSettings.JournalPostedThrough («asentado hasta»). Solo agrega tablas, indices y una columna nula; no toca datos existentes.
-- Va DESPUES de 20261019110927_AccountingBooks.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/JournalEntries.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que las tablas, la columna y los indices existan de verdad;
-- si algo falta, aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019120048_JournalEntries'
)
BEGIN
    ALTER TABLE [FinanceSettings] ADD [JournalPostedThrough] date NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019120048_JournalEntries'
)
BEGIN
    CREATE TABLE [JournalEntries] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [FiscalYear] int NOT NULL,
        [Number] int NOT NULL,
        [Date] date NOT NULL,
        [OperationDate] date NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [ThirdParty] nvarchar(200) NOT NULL,
        [Reference] nvarchar(200) NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [SourceType] int NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceAccountId] uniqueidentifier NULL,
        [Revision] int NOT NULL,
        [ReversesEntryId] uniqueidentifier NULL,
        [TotalDebit] decimal(18,2) NOT NULL,
        [TotalCredit] decimal(18,2) NOT NULL,
        [ContentHash] nvarchar(64) NOT NULL,
        [PostedAtUtc] datetime2 NOT NULL,
        [PostedByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_JournalEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JournalEntries_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_JournalEntries_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_JournalEntries_JournalEntries_ReversesEntryId] FOREIGN KEY ([ReversesEntryId]) REFERENCES [JournalEntries] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019120048_JournalEntries'
)
BEGIN
    CREATE TABLE [JournalSequences] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [FiscalYear] int NOT NULL,
        [LastNumber] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_JournalSequences] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JournalSequences_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_JournalSequences_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019120048_JournalEntries'
)
BEGIN
    CREATE TABLE [JournalEntryLines] (
        [Id] uniqueidentifier NOT NULL,
        [EntryId] uniqueidentifier NOT NULL,
        [LineNo] int NOT NULL,
        [LedgerCategoryId] uniqueidentifier NULL,
        [Code] nvarchar(50) NOT NULL,
        [ExternalCode] nvarchar(50) NULL,
        [Name] nvarchar(300) NOT NULL,
        [Debit] decimal(18,2) NOT NULL,
        [Credit] decimal(18,2) NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_JournalEntryLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JournalEntryLines_JournalEntries_EntryId] FOREIGN KEY ([EntryId]) REFERENCES [JournalEntries] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019120048_JournalEntries'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_BuildingId_Date] ON [JournalEntries] ([BuildingId], [Date]);
    CREATE UNIQUE INDEX [IX_JournalEntries_BuildingId_FiscalYear_Number] ON [JournalEntries] ([BuildingId], [FiscalYear], [Number]);
    CREATE INDEX [IX_JournalEntries_CompanyId] ON [JournalEntries] ([CompanyId]);
    CREATE INDEX [IX_JournalEntries_ReversesEntryId] ON [JournalEntries] ([ReversesEntryId]);
    CREATE UNIQUE INDEX [IX_JournalEntryLines_EntryId_LineNo] ON [JournalEntryLines] ([EntryId], [LineNo]);
    CREATE UNIQUE INDEX [IX_JournalSequences_BuildingId_FiscalYear] ON [JournalSequences] ([BuildingId], [FiscalYear]);
    CREATE INDEX [IX_JournalSequences_CompanyId] ON [JournalSequences] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019120048_JournalEntries'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_JournalEntries_BuildingId_SourceType_SourceId_Date_SourceAccountId_Revision] ON [JournalEntries] ([BuildingId], [SourceType], [SourceId], [Date], [SourceAccountId], [Revision]) WHERE [SourceType] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019120048_JournalEntries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019120048_JournalEntries', N'8.0.8');
END;
GO

IF OBJECT_ID(N'JournalEntries', N'U') IS NULL
   OR OBJECT_ID(N'JournalEntryLines', N'U') IS NULL
   OR OBJECT_ID(N'JournalSequences', N'U') IS NULL
   OR COL_LENGTH(N'FinanceSettings', N'JournalPostedThrough') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_BuildingId_FiscalYear_Number' AND object_id = OBJECT_ID(N'JournalEntries'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_BuildingId_SourceType_SourceId_Date_SourceAccountId_Revision' AND object_id = OBJECT_ID(N'JournalEntries'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_BuildingId_Date' AND object_id = OBJECT_ID(N'JournalEntries'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntryLines_EntryId_LineNo' AND object_id = OBJECT_ID(N'JournalEntryLines'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalSequences_BuildingId_FiscalYear' AND object_id = OBJECT_ID(N'JournalSequences'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261019120048_JournalEntries')
    RAISERROR(N'JournalEntries: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'JournalEntries OK' AS Resultado,
       OBJECT_ID(N'JournalEntries', N'U') AS Asientos,
       OBJECT_ID(N'JournalEntryLines', N'U') AS Renglones,
       OBJECT_ID(N'JournalSequences', N'U') AS Secuencias;
GO
