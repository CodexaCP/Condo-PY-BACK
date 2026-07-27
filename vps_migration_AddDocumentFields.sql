-- ============================================================
-- Migración: AddDocumentFieldsAndIsResident
-- Correr UNA SOLA VEZ en el VPS contra la base CondoDb
-- Es idempotente: si ya está aplicada no hace nada.
-- ============================================================

BEGIN TRANSACTION;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727133736_AddDocumentFieldsAndIsResident'
)
BEGIN
    ALTER TABLE [Residents] ADD [DocumentType] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727133736_AddDocumentFieldsAndIsResident'
)
BEGIN
    ALTER TABLE [ApplicationUsers] ADD [DocumentType] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727133736_AddDocumentFieldsAndIsResident'
)
BEGIN
    ALTER TABLE [ApplicationUsers] ADD [DocumentNumber] nvarchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727133736_AddDocumentFieldsAndIsResident'
)
BEGIN
    ALTER TABLE [ApplicationUsers] ADD [IsResident] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727133736_AddDocumentFieldsAndIsResident'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260727133736_AddDocumentFieldsAndIsResident', N'8.0.8');
END;

COMMIT;
