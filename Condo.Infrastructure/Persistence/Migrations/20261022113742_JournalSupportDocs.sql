-- ============================================================
-- Migracion EF: 20261022113742_JournalSupportDocs
-- Libro Diario, respaldo del comprobante (Ley 1034/83, art. 85): columnas JournalEntries.SupplierRuc y JournalEntries.Timbrado
-- (nvarchar(20) NOT NULL, vacias por defecto). Guardan el RUC del proveedor y el timbrado de la factura o de la nota de credito del
-- asiento. Lo asentado antes queda con ambos vacios (no se reescribe: los triggers de inmutabilidad lo impiden y no hace falta).
-- Solo agrega dos columnas; no toca datos existentes. Va DESPUES de 20261022105039_ManagerReconcile.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I y -b:
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/JournalSupportDocs.sql
--
-- Es idempotente (revisa __EFMigrationsHistory) y verifica al final que
-- las columnas existan de verdad; si faltan, aborta antes del COMMIT.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022113742_JournalSupportDocs'
)
BEGIN
    ALTER TABLE [JournalEntries] ADD [SupplierRuc] nvarchar(20) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022113742_JournalSupportDocs'
)
BEGIN
    ALTER TABLE [JournalEntries] ADD [Timbrado] nvarchar(20) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022113742_JournalSupportDocs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022113742_JournalSupportDocs', N'8.0.8');
END;
GO

IF COL_LENGTH(N'JournalEntries', N'SupplierRuc') IS NULL
   OR COL_LENGTH(N'JournalEntries', N'Timbrado') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261022113742_JournalSupportDocs')
    RAISERROR(N'JournalSupportDocs: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'JournalSupportDocs OK' AS Resultado, COL_LENGTH(N'JournalEntries', N'SupplierRuc') AS RUC, COL_LENGTH(N'JournalEntries', N'Timbrado') AS Timbrado;
GO
