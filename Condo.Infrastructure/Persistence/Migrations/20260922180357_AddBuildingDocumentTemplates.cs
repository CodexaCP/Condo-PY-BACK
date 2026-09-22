using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingDocumentTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreditNoteTemplateFileName",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditNoteTemplateUrl",
                table: "Buildings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceTemplateFileName",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceTemplateUrl",
                table: "Buildings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptTemplateFileName",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptTemplateUrl",
                table: "Buildings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseStandardTemplates",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: true); // los edificios existentes siguen con el modelo estandar
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditNoteTemplateFileName",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "CreditNoteTemplateUrl",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "InvoiceTemplateFileName",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "InvoiceTemplateUrl",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "ReceiptTemplateFileName",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "ReceiptTemplateUrl",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "UseStandardTemplates",
                table: "Buildings");
        }
    }
}
