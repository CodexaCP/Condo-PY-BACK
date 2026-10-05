using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildingExpenseCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "BuildingExpenses",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuildingExpenseCreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpensePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Timbrado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DocumentUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingExpenseCreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNotes_BuildingExpenses_BuildingExpenseId",
                        column: x => x.BuildingExpenseId,
                        principalTable: "BuildingExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNotes_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNotes_ExpensePeriods_ExpensePeriodId",
                        column: x => x.ExpensePeriodId,
                        principalTable: "ExpensePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNotes_BuildingExpenseId_Status",
                table: "BuildingExpenseCreditNotes",
                columns: new[] { "BuildingExpenseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNotes_BuildingId_ExpensePeriodId",
                table: "BuildingExpenseCreditNotes",
                columns: new[] { "BuildingId", "ExpensePeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNotes_CompanyId",
                table: "BuildingExpenseCreditNotes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNotes_ExpensePeriodId",
                table: "BuildingExpenseCreditNotes",
                column: "ExpensePeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "BuildingExpenses");
        }
    }
}
