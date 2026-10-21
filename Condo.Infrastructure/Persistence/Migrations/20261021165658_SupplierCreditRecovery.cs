using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierCreditRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveredAtUtc",
                table: "BuildingExpenseCreditNotes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecoveredByUserId",
                table: "BuildingExpenseCreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryStatus",
                table: "BuildingExpenseCreditNotes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "RecoveryType",
                table: "BuildingExpenseCreditNotes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "BuildingExpenseCreditNotes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RefundedOn",
                table: "BuildingExpenseCreditNotes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuildingExpenseCreditNoteDiscounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AppliedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    AppliedToExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingExpenseCreditNoteDiscounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteDiscounts_BuildingExpenseCreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "BuildingExpenseCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteDiscounts_BuildingExpenses_AppliedToExpenseId",
                        column: x => x.AppliedToExpenseId,
                        principalTable: "BuildingExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteDiscounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNotes_BuildingId_RecoveryStatus",
                table: "BuildingExpenseCreditNotes",
                columns: new[] { "BuildingId", "RecoveryStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteDiscounts_AppliedToExpenseId",
                table: "BuildingExpenseCreditNoteDiscounts",
                column: "AppliedToExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteDiscounts_CompanyId",
                table: "BuildingExpenseCreditNoteDiscounts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteDiscounts_CreditNoteId",
                table: "BuildingExpenseCreditNoteDiscounts",
                column: "CreditNoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingExpenseCreditNoteDiscounts");

            migrationBuilder.DropIndex(
                name: "IX_BuildingExpenseCreditNotes_BuildingId_RecoveryStatus",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "RecoveredAtUtc",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "RecoveredByUserId",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "RecoveryStatus",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "RecoveryType",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "RefundedOn",
                table: "BuildingExpenseCreditNotes");
        }
    }
}
