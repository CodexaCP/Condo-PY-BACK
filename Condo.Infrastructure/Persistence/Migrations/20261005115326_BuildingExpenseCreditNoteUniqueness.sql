-- ============================================================
-- Migracion EF: 20261005115326_BuildingExpenseCreditNoteUniqueness
-- Nota de credito del proveedor, control de duplicados: agrega a BuildingExpenseCreditNotes el proveedor (SupplierName) y las llaves
-- normalizadas (SupplierKey, NumeroKey, TimbradoKey) y un indice unico filtrado para que una misma nota (proveedor + timbrado + numero)
-- no este aplicada dos veces en la empresa. Va DESPUES de 20261005114334_BuildingExpenseCreditNotes. La tabla es nueva (vacia), asi
-- que no hay datos que rellenar.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/BuildingExpenseCreditNoteUniqueness.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness'
)
BEGIN
    DROP INDEX [IX_BuildingExpenseCreditNotes_CompanyId] ON [BuildingExpenseCreditNotes];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [NumeroKey] nvarchar(50) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [SupplierKey] nvarchar(200) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [SupplierName] nvarchar(200) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness'
)
BEGIN
    ALTER TABLE [BuildingExpenseCreditNotes] ADD [TimbradoKey] nvarchar(20) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BuildingExpenseCreditNotes_CompanyId_SupplierKey_TimbradoKey_NumeroKey] ON [BuildingExpenseCreditNotes] ([CompanyId], [SupplierKey], [TimbradoKey], [NumeroKey]) WHERE [IsDeleted] = 0 AND [Status] = ''Applied''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005115326_BuildingExpenseCreditNoteUniqueness', N'8.0.8');
END;
GO

IF COL_LENGTH(N'BuildingExpenseCreditNotes', N'SupplierName') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'SupplierKey') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'NumeroKey') IS NULL
   OR COL_LENGTH(N'BuildingExpenseCreditNotes', N'TimbradoKey') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingExpenseCreditNotes_CompanyId_SupplierKey_TimbradoKey_NumeroKey' AND is_unique = 1)
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261005115326_BuildingExpenseCreditNoteUniqueness')
    RAISERROR(N'BuildingExpenseCreditNoteUniqueness: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'BuildingExpenseCreditNoteUniqueness OK' AS Resultado;
GO
