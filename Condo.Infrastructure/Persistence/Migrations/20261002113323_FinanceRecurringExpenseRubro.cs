using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceRecurringExpenseRubro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LedgerCategoryId",
                table: "RecurringBuildingExpenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringBuildingExpenses_LedgerCategoryId",
                table: "RecurringBuildingExpenses",
                column: "LedgerCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringBuildingExpenses_LedgerCategories_LedgerCategoryId",
                table: "RecurringBuildingExpenses",
                column: "LedgerCategoryId",
                principalTable: "LedgerCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringBuildingExpenses_LedgerCategories_LedgerCategoryId",
                table: "RecurringBuildingExpenses");

            migrationBuilder.DropIndex(
                name: "IX_RecurringBuildingExpenses_LedgerCategoryId",
                table: "RecurringBuildingExpenses");

            migrationBuilder.DropColumn(
                name: "LedgerCategoryId",
                table: "RecurringBuildingExpenses");
        }
    }
}
