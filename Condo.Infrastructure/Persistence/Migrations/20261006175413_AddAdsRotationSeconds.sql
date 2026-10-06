-- ============================================================
-- Migracion EF: 20261006175413_AddAdsRotationSeconds
-- Publicidad: agrega Buildings.AdsRotationSeconds (segundos que la app muestra cada banner antes de pasar al siguiente,
-- 10 por defecto; el SuperAdmin lo cambia por edificio). Los edificios existentes quedan en 10. No toca otros datos.
-- Va DESPUES de 20261006131419_PersonBillingProfile.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/AddAdsRotationSeconds.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006175413_AddAdsRotationSeconds'
)
BEGIN
    ALTER TABLE [Buildings] ADD [AdsRotationSeconds] int NOT NULL DEFAULT 10;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006175413_AddAdsRotationSeconds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006175413_AddAdsRotationSeconds', N'8.0.8');
END;
GO

IF COL_LENGTH(N'Buildings', N'AdsRotationSeconds') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261006175413_AddAdsRotationSeconds')
    RAISERROR(N'AddAdsRotationSeconds: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'AddAdsRotationSeconds OK' AS Resultado;
GO
