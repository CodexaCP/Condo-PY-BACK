using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerCreditMovementCreditNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreditNoteId",
                table: "OwnerCreditMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerCreditMovements_CreditNoteId",
                table: "OwnerCreditMovements",
                column: "CreditNoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_OwnerCreditMovements_CreditNotes_CreditNoteId",
                table: "OwnerCreditMovements",
                column: "CreditNoteId",
                principalTable: "CreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OwnerCreditMovements_CreditNotes_CreditNoteId",
                table: "OwnerCreditMovements");

            migrationBuilder.DropIndex(
                name: "IX_OwnerCreditMovements_CreditNoteId",
                table: "OwnerCreditMovements");

            migrationBuilder.DropColumn(
                name: "CreditNoteId",
                table: "OwnerCreditMovements");
        }
    }
}
