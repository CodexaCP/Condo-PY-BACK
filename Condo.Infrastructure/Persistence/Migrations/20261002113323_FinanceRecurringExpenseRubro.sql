-- ============================================================
-- Migracion EF: 20261002113323_FinanceRecurringExpenseRubro
-- Finanzas del edificio: rubro opcional en las plantillas de gasto recurrente (RecurringBuildingExpenses.LedgerCategoryId + FK + indice).
--
-- Correr en el VPS ANTES de desplegar el codigo, con -I (indices
-- filtrados) y -b (abortar al primer error):
--   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -d CondoPY -I -b -i /tmp/FinanceRecurringExpenseRubro.sql
--
-- Es idempotente (cada bloque revisa __EFMigrationsHistory) y verifica
-- al final que lo creado exista de verdad; si algo falta, aborta antes
-- del COMMIT y no queda nada a medias.
-- ============================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002113323_FinanceRecurringExpenseRubro'
)
BEGIN
    ALTER TABLE [RecurringBuildingExpenses] ADD [LedgerCategoryId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002113323_FinanceRecurringExpenseRubro'
)
BEGIN
    CREATE INDEX [IX_RecurringBuildingExpenses_LedgerCategoryId] ON [RecurringBuildingExpenses] ([LedgerCategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002113323_FinanceRecurringExpenseRubro'
)
BEGIN
    ALTER TABLE [RecurringBuildingExpenses] ADD CONSTRAINT [FK_RecurringBuildingExpenses_LedgerCategories_LedgerCategoryId] FOREIGN KEY ([LedgerCategoryId]) REFERENCES [LedgerCategories] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002113323_FinanceRecurringExpenseRubro'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002113323_FinanceRecurringExpenseRubro', N'8.0.8');
END;
GO

IF COL_LENGTH(N'RecurringBuildingExpenses', N'LedgerCategoryId') IS NULL OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RecurringBuildingExpenses_LedgerCategories_LedgerCategoryId') OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecurringBuildingExpenses_LedgerCategoryId')
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261002113323_FinanceRecurringExpenseRubro')
    RAISERROR(N'FinanceRecurringExpenseRubro: verificacion fallida, la migracion no quedo completa. Se revierte.', 16, 1);
GO

COMMIT;
GO

SELECT N'FinanceRecurringExpenseRubro OK' AS Resultado;
GO
