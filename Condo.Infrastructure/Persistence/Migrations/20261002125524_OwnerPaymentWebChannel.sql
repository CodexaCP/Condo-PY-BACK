-- ============================================================
-- Migracion EF: 20261002125524_OwnerPaymentWebChannel
-- Pagos de propietarios: canal (App/Web), metodo, referencia bancaria, notas y fecha de reversa en OwnerPayments.
-- Los pagos existentes quedan como canal App / BankTransfer.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/OwnerPaymentWebChannel.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002125524_OwnerPaymentWebChannel'
)
BEGIN
    ALTER TABLE [OwnerPayments] ADD [Channel] nvarchar(10) NOT NULL DEFAULT N'App';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002125524_OwnerPaymentWebChannel'
)
BEGIN
    ALTER TABLE [OwnerPayments] ADD [ExternalReference] nvarchar(100) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002125524_OwnerPaymentWebChannel'
)
BEGIN
    ALTER TABLE [OwnerPayments] ADD [Method] nvarchar(30) NOT NULL DEFAULT N'BankTransfer';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002125524_OwnerPaymentWebChannel'
)
BEGIN
    ALTER TABLE [OwnerPayments] ADD [Notes] nvarchar(500) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002125524_OwnerPaymentWebChannel'
)
BEGIN
    ALTER TABLE [OwnerPayments] ADD [ReversedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002125524_OwnerPaymentWebChannel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002125524_OwnerPaymentWebChannel', N'8.0.8');
END;
GO

IF COL_LENGTH(N'OwnerPayments', N'Channel') IS NULL OR COL_LENGTH(N'OwnerPayments', N'ExternalReference') IS NULL OR COL_LENGTH(N'OwnerPayments', N'Method') IS NULL OR COL_LENGTH(N'OwnerPayments', N'Notes') IS NULL OR COL_LENGTH(N'OwnerPayments', N'ReversedAt') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002125524_OwnerPaymentWebChannel')
    RAISERROR(N'OwnerPaymentWebChannel: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'OwnerPaymentWebChannel OK' AS Resultado;
GO
