-- ============================================================
-- Migracion EF: 20261005114334_BuildingExpenseCreditNotes
-- Nota de credito del proveedor sobre un gasto del edificio, fase 1 (periodo sin publicar): agrega la columna OriginalAmount a
-- BuildingExpenses (monto facturado por el proveedor antes de las notas de credito; null mientras no tenga ninguna) y crea
-- BuildingExpenseCreditNotes. No toca datos existentes.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/BuildingExpenseCreditNotes.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes'
)
BEGIN
    ALTER TABLE [BuildingExpenses] ADD [OriginalAmount] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes'
)
BEGIN
    CREATE TABLE [BuildingExpenseCreditNotes] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [BuildingExpenseId] uniqueidentifier NOT NULL,
        [ExpensePeriodId] uniqueidentifier NOT NULL,
        [Numero] nvarchar(50) NOT NULL,
        [Timbrado] nvarchar(20) NULL,
        [IssueDate] date NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [DocumentUrl] nvarchar(500) NULL,
        [Mode] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [VoidReason] nvarchar(500) NOT NULL,
        [VoidedAtUtc] datetime2 NULL,
        [VoidedByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BuildingExpenseCreditNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BuildingExpenseCreditNotes_BuildingExpenses_BuildingExpenseId] FOREIGN KEY ([BuildingExpenseId]) REFERENCES [BuildingExpenses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNotes_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNotes_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNotes_ExpensePeriods_ExpensePeriodId] FOREIGN KEY ([ExpensePeriodId]) REFERENCES [ExpensePeriods] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNotes_BuildingExpenseId_Status] ON [BuildingExpenseCreditNotes] ([BuildingExpenseId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNotes_BuildingId_ExpensePeriodId] ON [BuildingExpenseCreditNotes] ([BuildingId], [ExpensePeriodId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNotes_CompanyId] ON [BuildingExpenseCreditNotes] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNotes_ExpensePeriodId] ON [BuildingExpenseCreditNotes] ([ExpensePeriodId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005114334_BuildingExpenseCreditNotes', N'8.0.8');
END;
GO

IF COL_LENGTH(N'BuildingExpenses', N'OriginalAmount') IS NULL
   OR OBJECT_ID(N'BuildingExpenseCreditNotes') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'Numero') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'Mode') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingExpenseCreditNotes_BuildingExpenseId_Status')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261005114334_BuildingExpenseCreditNotes')
    RAISERROR(N'BuildingExpenseCreditNotes: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'BuildingExpenseCreditNotes OK' AS Resultado;
GO
