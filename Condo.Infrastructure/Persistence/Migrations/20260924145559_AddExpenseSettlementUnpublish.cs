using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseSettlementUnpublish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnpublishReason",
                table: "ExpenseSettlements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UnpublishedAtUtc",
                table: "ExpenseSettlements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnpublishedByUserId",
                table: "ExpenseSettlements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_UnpublishedByUserId",
                table: "ExpenseSettlements",
                column: "UnpublishedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_UnpublishedByUserId",
                table: "ExpenseSettlements",
                column: "UnpublishedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_UnpublishedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseSettlements_UnpublishedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "UnpublishReason",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "UnpublishedAtUtc",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "UnpublishedByUserId",
                table: "ExpenseSettlements");
        }
    }
}
