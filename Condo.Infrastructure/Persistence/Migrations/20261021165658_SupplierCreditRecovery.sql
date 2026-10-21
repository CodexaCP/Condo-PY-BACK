-- ============================================================
-- Migracion EF: 20261021165658_SupplierCreditRecovery
-- Cuentas por cobrar a proveedores (notas de credito de gastos ya pagados): columnas nuevas en BuildingExpenseCreditNotes
-- (RecoveryType y RecoveryStatus con valor por defecto 'None', RefundedOn, RefundReference, RecoveredAtUtc, RecoveredByUserId) y tabla
-- BuildingExpenseCreditNoteDiscounts (descuentos ya aplicados en facturas posteriores del proveedor).
-- Solo agrega columnas con valor por defecto o nulas, una tabla e indices; las notas existentes quedan sin cuenta por cobrar.
-- Va DESPUES de 20261020143213_OnlinePaymentIntents.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/SupplierCreditRecovery.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [RecoveredAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [RecoveredByUserId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [RecoveryStatus] nvarchar(20) NOT NULL DEFAULT N'None';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [RecoveryType] nvarchar(30) NOT NULL DEFAULT N'None';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [RefundReference] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [RefundedOn] date NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    CREATE TABLE [BuildingExpenseCreditNoteDiscounts] (
        [Id] uniqueidentifier NOT NULL,
        [CreditNoteId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [AppliedOn] date NOT NULL,
        [AppliedToExpenseId] uniqueidentifier NULL,
        [Note] nvarchar(500) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BuildingExpenseCreditNoteDiscounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BuildingExpenseCreditNoteDiscounts_BuildingExpenseCreditNotes_CreditNoteId] FOREIGN KEY ([CreditNoteId]) REFERENCES [BuildingExpenseCreditNotes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNoteDiscounts_BuildingExpenses_AppliedToExpenseId] FOREIGN KEY ([AppliedToExpenseId]) REFERENCES [BuildingExpenses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNoteDiscounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNotes_BuildingId_RecoveryStatus] ON [BuildingExpenseCreditNotes] ([BuildingId], [RecoveryStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteDiscounts_AppliedToExpenseId] ON [BuildingExpenseCreditNoteDiscounts] ([AppliedToExpenseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteDiscounts_CompanyId] ON [BuildingExpenseCreditNoteDiscounts] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteDiscounts_CreditNoteId] ON [BuildingExpenseCreditNoteDiscounts] ([CreditNoteId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021165658_SupplierCreditRecovery', N'8.0.8');
END;
GO

IF OBJECT_ID(N'BuildingExpenseCreditNoteDiscounts', N'U') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'RecoveryType') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'RecoveryStatus') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'RefundedOn') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'RefundReference') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'RecoveredAtUtc') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'RecoveredByUserId') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingExpenseCreditNotes_BuildingId_RecoveryStatus' AND object_id = OBJECT_ID(N'BuildingExpenseCreditNotes'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingExpenseCreditNoteDiscounts_CreditNoteId' AND object_id = OBJECT_ID(N'BuildingExpenseCreditNoteDiscounts'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261021165658_SupplierCreditRecovery')
    RAISERROR(N'SupplierCreditRecovery: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'SupplierCreditRecovery OK' AS Resultado,
       OBJECT_ID(N'BuildingExpenseCreditNoteDiscounts', N'U') AS Descuentos;
GO
