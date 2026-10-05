-- ============================================================
-- Migracion EF: 20261005123940_OwnerCreditMovementOnHold
-- Cambio de propietario de una unidad: agrega OnHold a OwnerCreditMovements (lote de saldo a favor retenido mientras la unidad no tiene
-- propietario principal; no se consume ni cuenta en el saldo de nadie hasta que se asigne el nuevo). Los lotes existentes quedan en 0
-- (no retenidos). Va DESPUES de 20261005121441_BuildingExpenseCreditNoteAllocations. No toca otros datos.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/OwnerCreditMovementOnHold.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005123940_OwnerCreditMovementOnHold'
)
BEGIN
    ALTER TABLE [OwnerCreditMovements] ADD [OnHold] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005123940_OwnerCreditMovementOnHold'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005123940_OwnerCreditMovementOnHold', N'8.0.8');
END;
GO

IF COL_LENGTH(N'OwnerCreditMovements', N'OnHold') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261005123940_OwnerCreditMovementOnHold')
    RAISERROR(N'OwnerCreditMovementOnHold: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'OwnerCreditMovementOnHold OK' AS Resultado;
GO
