-- ============================================================
-- Migracion EF: 20261002185716_MarketplacePaymentAlerts
-- Marketplace de espacios temporales, fase 5: cuenta de las alertas al revisor de pagos (cuantas se mandaron y cuando fue la
-- ultima) en MarketplacePayments. Las alertas salen al subir el comprobante, a los 15 minutos y luego cada hora.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/MarketplacePaymentAlerts.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts'
)
BEGIN
    ALTER TABLE [MarketplacePayments] ADD [AlertCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts'
)
BEGIN
    ALTER TABLE [MarketplacePayments] ADD [LastAlertAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002185716_MarketplacePaymentAlerts', N'8.0.8');
END;
GO

IF COL_LENGTH(N'MarketplacePayments', N'AlertCount') IS NULL OR COL_LENGTH(N'MarketplacePayments', N'LastAlertAtUtc') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002185716_MarketplacePaymentAlerts')
    RAISERROR(N'MarketplacePaymentAlerts: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'MarketplacePaymentAlerts OK' AS Resultado;
GO
