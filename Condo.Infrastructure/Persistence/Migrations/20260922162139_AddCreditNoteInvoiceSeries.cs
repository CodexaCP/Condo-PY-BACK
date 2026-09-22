using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteInvoiceSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceSeries_CompanyId_Establecimiento_PuntoExpedicion_NumeroTimbrado",
                table: "InvoiceSeries");

            // defaultValue "Invoice": todos los timbrados existentes hoy son de factura.
            migrationBuilder.AddColumn<string>(
                name: "DocumentType",
                table: "InvoiceSeries",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Invoice");

            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceSeriesId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Numero",
                table: "CreditNotes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSeries_CompanyId_Establecimiento_PuntoExpedicion_NumeroTimbrado_DocumentType",
                table: "InvoiceSeries",
                columns: new[] { "CompanyId", "Establecimiento", "PuntoExpedicion", "NumeroTimbrado", "DocumentType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceSeriesId_Numero",
                table: "CreditNotes",
                columns: new[] { "InvoiceSeriesId", "Numero" },
                unique: true,
                filter: "[Numero] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_InvoiceSeries_InvoiceSeriesId",
                table: "CreditNotes",
                column: "InvoiceSeriesId",
                principalTable: "InvoiceSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_InvoiceSeries_InvoiceSeriesId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceSeries_CompanyId_Establecimiento_PuntoExpedicion_NumeroTimbrado_DocumentType",
                table: "InvoiceSeries");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_InvoiceSeriesId_Numero",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "InvoiceSeries");

            migrationBuilder.DropColumn(
                name: "InvoiceSeriesId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "Numero",
                table: "CreditNotes");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSeries_CompanyId_Establecimiento_PuntoExpedicion_NumeroTimbrado",
                table: "InvoiceSeries",
                columns: new[] { "CompanyId", "Establecimiento", "PuntoExpedicion", "NumeroTimbrado" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
