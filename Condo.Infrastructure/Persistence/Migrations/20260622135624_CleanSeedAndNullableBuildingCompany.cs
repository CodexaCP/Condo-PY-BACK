using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CleanSeedAndNullableBuildingCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings");

            migrationBuilder.DeleteData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000003"));

            migrationBuilder.DeleteData(
                table: "BuildingExpenses",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000014"));

            migrationBuilder.DeleteData(
                table: "BuildingIncomes",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000015"));

            migrationBuilder.DeleteData(
                table: "ExpenseCharges",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000012"));

            migrationBuilder.DeleteData(
                table: "ExpenseSettlements",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000016"));

            migrationBuilder.DeleteData(
                table: "Payments",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000013"));

            migrationBuilder.DeleteData(
                table: "UnitResidents",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000008"));

            migrationBuilder.DeleteData(
                table: "UserBuildingAccesses",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000010"));

            migrationBuilder.DeleteData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000009"));

            migrationBuilder.DeleteData(
                table: "ExpensePeriods",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"));

            migrationBuilder.DeleteData(
                table: "Residents",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000007"));

            migrationBuilder.DeleteData(
                table: "Units",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"));

            migrationBuilder.DeleteData(
                table: "Buildings",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"));

            migrationBuilder.DeleteData(
                table: "Condominiums",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000004"));

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"));

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "Buildings",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000001"),
                column: "FullName",
                value: "Super Admin");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "Buildings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000001"),
                column: "FullName",
                value: "Super Admin Codexa");

            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "CreatedAtUtc", "IsActive", "IsDeleted", "Name", "Slug", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Codexa Administradora Demo", "codexa-admin-demo", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ApplicationUsers",
                columns: new[] { "Id", "CompanyId", "CreatedAtUtc", "Email", "FullName", "IsActive", "IsDeleted", "LastLoginAtUtc", "MustChangePassword", "PasswordHash", "Role", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000003"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "admin@codexa.local", "Admin Empresa Demo", true, false, null, true, "123456", "CompanyAdmin", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000009"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "manager@codexa.local", "Manager Martinica", true, false, null, true, "123456", "BuildingManager", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "Condominiums",
                columns: new[] { "Id", "Address", "Code", "CompanyId", "CreatedAtUtc", "IsActive", "IsDeleted", "Name", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000004"), "Asuncion, Paraguay", "CMZ-01", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Condominio Martinez", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Residents",
                columns: new[] { "Id", "CompanyId", "CreatedAtUtc", "DocumentNumber", "Email", "FullName", "IsActive", "IsDeleted", "IsOwner", "PhoneNumber", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000007"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1234567", "residente@codexa.local", "Residente Demo", true, false, true, "0991000000", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Buildings",
                columns: new[] { "Id", "Address", "Code", "CompanyId", "CondominiumId", "CreatedAtUtc", "IsActive", "IsDeleted", "Name", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "Asuncion, Paraguay", "MART-01", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000004"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Edificio Martinica", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ExpensePeriods",
                columns: new[] { "Id", "BuildingId", "CompanyId", "CreatedAtUtc", "DueDate", "EndDate", "IsDeleted", "Month", "Name", "Notes", "StartDate", "Status", "UpdatedAtUtc", "Year" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 7, 10), new DateOnly(2026, 6, 30), false, 6, "Junio 2026", "Periodo inicial de expensas para pruebas del modulo financiero.", new DateOnly(2026, 6, 1), "Draft", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2026 });

            migrationBuilder.InsertData(
                table: "Units",
                columns: new[] { "Id", "BuildingId", "Code", "Coefficient", "CompanyId", "CreatedAtUtc", "Floor", "IsActive", "IsDeleted", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "A-101", 1.000000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1", true, false, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "UserBuildingAccesses",
                columns: new[] { "Id", "ApplicationUserId", "BuildingId", "CompanyId", "CreatedAtUtc", "IsActive", "IsDeleted", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000010"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000009"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "BuildingExpenses",
                columns: new[] { "Id", "Amount", "BuildingId", "Category", "CompanyId", "CreatedAtUtc", "Description", "DistributionType", "ExpenseDate", "ExpensePeriodId", "IsDeleted", "Notes", "SupplierName", "TargetUnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000014"), 1200000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "Cleaning", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Servicio mensual de limpieza de areas comunes", "ByCoefficient", new DateOnly(2026, 6, 5), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), false, "Gasto semilla para pruebas del modulo de gastos del edificio.", "Limpieza Integral SA", null, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "BuildingIncomes",
                columns: new[] { "Id", "Amount", "BuildingId", "Category", "CompanyId", "CreatedAtUtc", "Description", "ExpensePeriodId", "IncomeDate", "IsDeleted", "Notes", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000015"), 350000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "OperationalFund", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Compensacion de fondo operativo del periodo", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), new DateOnly(2026, 6, 8), false, "Ingreso semilla para pruebas del modulo de ingresos del edificio.", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ExpenseCharges",
                columns: new[] { "Id", "Amount", "ChargeType", "CompanyId", "Concept", "CreatedAtUtc", "ExpensePeriodId", "IsDeleted", "IsLateFee", "Notes", "SourceBuildingExpenseId", "SourceSettlementId", "UnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000012"), 850000m, "Ordinary", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), "Expensa ordinaria", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), false, false, "Cargo semilla para pruebas del modulo financiero.", null, null, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ExpenseSettlements",
                columns: new[] { "Id", "ApprovedAtUtc", "ApprovedByUserId", "BuildingId", "CompanyId", "CreatedAtUtc", "ExpensePeriodId", "ExtraordinaryAmount", "GeneratedAtUtc", "GeneratedByUserId", "IsDeleted", "NetCommonAmount", "PublishedAtUtc", "PublishedByUserId", "ReserveFundAmount", "Status", "TotalBuildingExpenses", "TotalBuildingIncomes", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000016"), null, null, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), 0m, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000009"), false, 850000m, null, null, 0m, "Calculated", 1200000m, 350000m, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Payments",
                columns: new[] { "Id", "Amount", "CompanyId", "CreatedAtUtc", "ExpensePeriodId", "IsDeleted", "Method", "Notes", "PaymentDate", "Reference", "UnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000013"), 250000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), false, "BankTransfer", "Pago parcial semilla para pruebas del modulo financiero.", new DateOnly(2026, 6, 15), "TRX-0001", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "UnitResidents",
                columns: new[] { "Id", "CompanyId", "CreatedAtUtc", "EndDate", "IsDeleted", "IsPrimary", "ResidentId", "StartDate", "UnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000008"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000007"), new DateOnly(2026, 1, 1), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }
    }
}
