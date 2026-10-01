using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceModuleBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludesFinanceModule",
                table: "Plans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FinanceModuleEnabled",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FinanceSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinanceStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FiscalYearStartMonth = table.Column<int>(type: "int", nullable: false),
                    SetupCompleted = table.Column<bool>(type: "bit", nullable: false),
                    SetupCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SetupCompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EnabledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EnabledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceSettings_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialAccounts_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialAccounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SystemKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerCategories_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCategories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCategories_LedgerCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "LedgerCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"),
                column: "IncludesFinanceModule",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceSettings_BuildingId",
                table: "FinanceSettings",
                column: "BuildingId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceSettings_CompanyId",
                table: "FinanceSettings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccounts_BuildingId_Name",
                table: "FinancialAccounts",
                columns: new[] { "BuildingId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccounts_BuildingId_Type",
                table: "FinancialAccounts",
                columns: new[] { "BuildingId", "Type" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Type] <> 'Bank'");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccounts_CompanyId",
                table: "FinancialAccounts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCategories_BuildingId_Code",
                table: "LedgerCategories",
                columns: new[] { "BuildingId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCategories_BuildingId_SystemKey",
                table: "LedgerCategories",
                columns: new[] { "BuildingId", "SystemKey" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [SystemKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCategories_CompanyId",
                table: "LedgerCategories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCategories_ParentId",
                table: "LedgerCategories",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceSettings");

            migrationBuilder.DropTable(
                name: "FinancialAccounts");

            migrationBuilder.DropTable(
                name: "LedgerCategories");

            migrationBuilder.DropColumn(
                name: "IncludesFinanceModule",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "FinanceModuleEnabled",
                table: "Buildings");
        }
    }
}
