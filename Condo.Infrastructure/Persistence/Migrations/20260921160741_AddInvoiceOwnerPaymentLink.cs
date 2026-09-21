using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceOwnerPaymentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerPaymentId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OwnerPaymentId",
                table: "Invoices",
                column: "OwnerPaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_OwnerPayments_OwnerPaymentId",
                table: "Invoices",
                column: "OwnerPaymentId",
                principalTable: "OwnerPayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_OwnerPayments_OwnerPaymentId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OwnerPaymentId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "OwnerPaymentId",
                table: "Invoices");
        }
    }
}
