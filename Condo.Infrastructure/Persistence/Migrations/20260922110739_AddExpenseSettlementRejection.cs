using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseSettlementRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAtUtc",
                table: "ExpenseSettlements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                table: "ExpenseSettlements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "ExpenseSettlements",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_RejectedByUserId",
                table: "ExpenseSettlements",
                column: "RejectedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_RejectedByUserId",
                table: "ExpenseSettlements",
                column: "RejectedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_RejectedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseSettlements_RejectedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "ExpenseSettlements");
        }
    }
}
