-- ============================================================
-- Migracion EF: 20261002103244_FinanceRubroOnMovements
-- Finanzas del edificio: rubro opcional en gastos e ingresos (LedgerCategoryId + FK + indices) y categoria de liquidacion de los rubros propios (LedgerCategories.ExpenseCategory / IncomeCategory).
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/FinanceRubroOnMovements.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    ALTER TABLE [LedgerCategories] ADD [ExpenseCategory] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    ALTER TABLE [LedgerCategories] ADD [IncomeCategory] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    ALTER TABLE [BuildingIncomes] ADD [LedgerCategoryId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    ALTER TABLE [BuildingExpenses] ADD [LedgerCategoryId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    CREATE INDEX [IX_BuildingIncomes_LedgerCategoryId] ON [BuildingIncomes] ([LedgerCategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    CREATE INDEX [IX_BuildingExpenses_LedgerCategoryId] ON [BuildingExpenses] ([LedgerCategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    ALTER TABLE [BuildingExpenses] ADD CONSTRAINT [FK_BuildingExpenses_LedgerCategories_LedgerCategoryId] FOREIGN KEY ([LedgerCategoryId]) REFERENCES [LedgerCategories] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    ALTER TABLE [BuildingIncomes] ADD CONSTRAINT [FK_BuildingIncomes_LedgerCategories_LedgerCategoryId] FOREIGN KEY ([LedgerCategoryId]) REFERENCES [LedgerCategories] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002103244_FinanceRubroOnMovements', N'8.0.8');
END;
GO

IF COL_LENGTH(N'BuildingExpenses', N'LedgerCategoryId') IS NULL OR COL_LENGTH(N'BuildingIncomes', N'LedgerCategoryId') IS NULL OR COL_LENGTH(N'LedgerCategories', N'ExpenseCategory') IS NULL OR COL_LENGTH(N'LedgerCategories', N'IncomeCategory') IS NULL OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingExpenses_LedgerCategories_LedgerCategoryId') OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingIncomes_LedgerCategories_LedgerCategoryId')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002103244_FinanceRubroOnMovements')
    RAISERROR(N'FinanceRubroOnMovements: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'FinanceRubroOnMovements OK' AS Resultado;
GO
