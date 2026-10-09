using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuppliersPayablesVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VatTreatment",
                table: "LedgerCategories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "BuildingExpenses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "BuildingExpenses",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceTimbrado",
                table: "BuildingExpenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidAt",
                table: "BuildingExpenses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidFromAccountId",
                table: "BuildingExpenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "BuildingExpenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "BuildingExpenses",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ruc = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PaymentTermDays = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suppliers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_BuildingId_DueDate",
                table: "BuildingExpenses",
                columns: new[] { "BuildingId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_PaidFromAccountId",
                table: "BuildingExpenses",
                column: "PaidFromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_SupplierId",
                table: "BuildingExpenses",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_CompanyId_Name",
                table: "Suppliers",
                columns: new[] { "CompanyId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_CompanyId_Ruc",
                table: "Suppliers",
                columns: new[] { "CompanyId", "Ruc" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Ruc] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_BuildingExpenses_FinancialAccounts_PaidFromAccountId",
                table: "BuildingExpenses",
                column: "PaidFromAccountId",
                principalTable: "FinancialAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BuildingExpenses_Suppliers_SupplierId",
                table: "BuildingExpenses",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BuildingExpenses_FinancialAccounts_PaidFromAccountId",
                table: "BuildingExpenses");

            migrationBuilder.DropForeignKey(
                name: "FK_BuildingExpenses_Suppliers_SupplierId",
                table: "BuildingExpenses");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_BuildingExpenses_BuildingId_DueDate",
                table: "BuildingExpenses");

            migrationBuilder.DropIndex(
                name: "IX_BuildingExpenses_PaidFromAccountId",
                table: "BuildingExpenses");

            migrationBuilder.DropIndex(
                name: "IX_BuildingExpenses_SupplierId",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "LedgerCategories");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "InvoiceTimbrado",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "PaidFromAccountId",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "BuildingExpenses");
        }
    }
}
