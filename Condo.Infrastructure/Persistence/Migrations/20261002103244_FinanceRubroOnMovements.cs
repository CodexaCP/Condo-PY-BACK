using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceRubroOnMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpenseCategory",
                table: "LedgerCategories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncomeCategory",
                table: "LedgerCategories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LedgerCategoryId",
                table: "BuildingIncomes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LedgerCategoryId",
                table: "BuildingExpenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildingIncomes_LedgerCategoryId",
                table: "BuildingIncomes",
                column: "LedgerCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_LedgerCategoryId",
                table: "BuildingExpenses",
                column: "LedgerCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_BuildingExpenses_LedgerCategories_LedgerCategoryId",
                table: "BuildingExpenses",
                column: "LedgerCategoryId",
                principalTable: "LedgerCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BuildingIncomes_LedgerCategories_LedgerCategoryId",
                table: "BuildingIncomes",
                column: "LedgerCategoryId",
                principalTable: "LedgerCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BuildingExpenses_LedgerCategories_LedgerCategoryId",
                table: "BuildingExpenses");

            migrationBuilder.DropForeignKey(
                name: "FK_BuildingIncomes_LedgerCategories_LedgerCategoryId",
                table: "BuildingIncomes");

            migrationBuilder.DropIndex(
                name: "IX_BuildingIncomes_LedgerCategoryId",
                table: "BuildingIncomes");

            migrationBuilder.DropIndex(
                name: "IX_BuildingExpenses_LedgerCategoryId",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "ExpenseCategory",
                table: "LedgerCategories");

            migrationBuilder.DropColumn(
                name: "IncomeCategory",
                table: "LedgerCategories");

            migrationBuilder.DropColumn(
                name: "LedgerCategoryId",
                table: "BuildingIncomes");

            migrationBuilder.DropColumn(
                name: "LedgerCategoryId",
                table: "BuildingExpenses");
        }
    }
}
