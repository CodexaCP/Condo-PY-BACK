-- ============================================================
-- Migracion EF: 20261009183045_AccountingRoles
-- Finanzas, asientos sugeridos: tabla LedgerAccountRoles (a que cuenta del plan de cuentas corresponde cada cuenta financiera del edificio y
-- donde se asienta el IVA credito). Es solo una asignacion: no mueve saldos ni cambia el libro. Solo agrega una tabla y sus indices; no toca
-- datos existentes.
-- Va DESPUES de 20261009180310_BankReconciliation.
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/AccountingRoles.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que la tabla y los indices existan de verdad; si algo falta,
-- aborta antes del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183045_AccountingRoles'
)
BEGIN
    CREATE TABLE [LedgerAccountRoles] (
        [Id] uniqueidentifier NOT NULL,
        [BuildingId] uniqueidentifier NOT NULL,
        [Role] nvarchar(30) NOT NULL,
        [FinancialAccountId] uniqueidentifier NULL,
        [LedgerCategoryId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_LedgerAccountRoles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LedgerAccountRoles_Buildings_BuildingId] FOREIGN KEY ([BuildingId]) REFERENCES [Buildings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LedgerAccountRoles_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LedgerAccountRoles_FinancialAccounts_FinancialAccountId] FOREIGN KEY ([FinancialAccountId]) REFERENCES [FinancialAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LedgerAccountRoles_LedgerCategories_LedgerCategoryId] FOREIGN KEY ([LedgerCategoryId]) REFERENCES [LedgerCategories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183045_AccountingRoles'
)
BEGIN
    CREATE INDEX [IX_LedgerAccountRoles_CompanyId] ON [LedgerAccountRoles] ([CompanyId]);
    CREATE INDEX [IX_LedgerAccountRoles_FinancialAccountId] ON [LedgerAccountRoles] ([FinancialAccountId]);
    CREATE INDEX [IX_LedgerAccountRoles_LedgerCategoryId] ON [LedgerAccountRoles] ([LedgerCategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183045_AccountingRoles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LedgerAccountRoles_BuildingId_Role_FinancialAccountId] ON [LedgerAccountRoles] ([BuildingId], [Role], [FinancialAccountId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183045_AccountingRoles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009183045_AccountingRoles', N'8.0.8');
END;
GO

IF OBJECT_ID(N'LedgerAccountRoles', N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LedgerAccountRoles_BuildingId_Role_FinancialAccountId' AND object_id = OBJECT_ID(N'LedgerAccountRoles'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LedgerAccountRoles_CompanyId' AND object_id = OBJECT_ID(N'LedgerAccountRoles'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LedgerAccountRoles_FinancialAccountId' AND object_id = OBJECT_ID(N'LedgerAccountRoles'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LedgerAccountRoles_LedgerCategoryId' AND object_id = OBJECT_ID(N'LedgerAccountRoles'))
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261009183045_AccountingRoles')
    RAISERROR(N'AccountingRoles: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'AccountingRoles OK' AS Resultado,
       OBJECT_ID(N'LedgerAccountRoles', N'U') AS Roles;
GO
