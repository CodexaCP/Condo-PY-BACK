-- ============================================================
-- Migracion EF: 20261005121441_BuildingExpenseCreditNoteAllocations
-- Nota de credito del proveedor, fase 2 (periodo publicado): crea BuildingExpenseCreditNoteAllocations (lo acreditado a cada unidad) y
-- agrega a OwnerCreditMovements el edificio, la unidad y la nota de credito de origen del lote de saldo a favor (solo trazabilidad).
-- Va DESPUES de 20261005115326_BuildingExpenseCreditNoteUniqueness. No toca datos existentes.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/BuildingExpenseCreditNoteAllocations.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    ALTER TABLE [OwnerCreditMovements] ADD [BuildingId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    ALTER TABLE [OwnerCreditMovements] ADD [SupplierCreditNoteId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    ALTER TABLE [OwnerCreditMovements] ADD [UnitId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    CREATE TABLE [BuildingExpenseCreditNoteAllocations] (
        [Id] uniqueidentifier NOT NULL,
        [CreditNoteId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [OwnerCreditMovementId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BuildingExpenseCreditNoteAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BuildingExpenseCreditNoteAllocations_ApplicationUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNoteAllocations_BuildingExpenseCreditNotes_CreditNoteId] FOREIGN KEY ([CreditNoteId]) REFERENCES [BuildingExpenseCreditNotes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNoteAllocations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNoteAllocations_OwnerCreditMovements_OwnerCreditMovementId] FOREIGN KEY ([OwnerCreditMovementId]) REFERENCES [OwnerCreditMovements] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BuildingExpenseCreditNoteAllocations_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteAllocations_CompanyId] ON [BuildingExpenseCreditNoteAllocations] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteAllocations_CreditNoteId] ON [BuildingExpenseCreditNoteAllocations] ([CreditNoteId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteAllocations_OwnerCreditMovementId] ON [BuildingExpenseCreditNoteAllocations] ([OwnerCreditMovementId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteAllocations_OwnerId] ON [BuildingExpenseCreditNoteAllocations] ([OwnerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenseCreditNoteAllocations_UnitId] ON [BuildingExpenseCreditNoteAllocations] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005121441_BuildingExpenseCreditNoteAllocations', N'8.0.8');
END;
GO

IF COL_LENGTH(N'OwnerCreditMovements', N'BuildingId') IS NULL
   OR COL_LENGTH(N'OwnerCreditMovements', N'UnitId') IS NULL
   OR COL_LENGTH(N'OwnerCreditMovements', N'SupplierCreditNoteId') IS NULL
   OR OBJECT_ID(N'BuildingExpenseCreditNoteAllocations') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNoteAllocations', N'OwnerCreditMovementId') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261005121441_BuildingExpenseCreditNoteAllocations')
    RAISERROR(N'BuildingExpenseCreditNoteAllocations: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'BuildingExpenseCreditNoteAllocations OK' AS Resultado;
GO
