using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingPresidentAndSettlementPresidentReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PresidentApprovedAtUtc",
                table: "ExpenseSettlements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PresidentApprovedByUserId",
                table: "ExpenseSettlements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PresidentRejectedAtUtc",
                table: "ExpenseSettlements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PresidentRejectedByUserId",
                table: "ExpenseSettlements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresidentRejectionReason",
                table: "ExpenseSettlements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PresidentAssignedAtUtc",
                table: "Buildings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PresidentAssignedByUserId",
                table: "Buildings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PresidentUserId",
                table: "Buildings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_PresidentApprovedByUserId",
                table: "ExpenseSettlements",
                column: "PresidentApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_PresidentRejectedByUserId",
                table: "ExpenseSettlements",
                column: "PresidentRejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_PresidentUserId",
                table: "Buildings",
                column: "PresidentUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Buildings_ApplicationUsers_PresidentUserId",
                table: "Buildings",
                column: "PresidentUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_PresidentApprovedByUserId",
                table: "ExpenseSettlements",
                column: "PresidentApprovedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_PresidentRejectedByUserId",
                table: "ExpenseSettlements",
                column: "PresidentRejectedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Buildings_ApplicationUsers_PresidentUserId",
                table: "Buildings");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_PresidentApprovedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseSettlements_ApplicationUsers_PresidentRejectedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseSettlements_PresidentApprovedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseSettlements_PresidentRejectedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_PresidentUserId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PresidentApprovedAtUtc",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "PresidentApprovedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "PresidentRejectedAtUtc",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "PresidentRejectedByUserId",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "PresidentRejectionReason",
                table: "ExpenseSettlements");

            migrationBuilder.DropColumn(
                name: "PresidentAssignedAtUtc",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PresidentAssignedByUserId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PresidentUserId",
                table: "Buildings");
        }
    }
}
