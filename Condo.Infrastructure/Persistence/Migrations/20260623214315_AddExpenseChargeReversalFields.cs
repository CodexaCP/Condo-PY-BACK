using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseChargeReversalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                table: "ExpenseCharges",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalOfChargeId",
                table: "ExpenseCharges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCharges_ReversalOfChargeId",
                table: "ExpenseCharges",
                column: "ReversalOfChargeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseCharges_ExpenseCharges_ReversalOfChargeId",
                table: "ExpenseCharges",
                column: "ReversalOfChargeId",
                principalTable: "ExpenseCharges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseCharges_ExpenseCharges_ReversalOfChargeId",
                table: "ExpenseCharges");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseCharges_ReversalOfChargeId",
                table: "ExpenseCharges");

            migrationBuilder.DropColumn(
                name: "IsReversal",
                table: "ExpenseCharges");

            migrationBuilder.DropColumn(
                name: "ReversalOfChargeId",
                table: "ExpenseCharges");
        }
    }
}
