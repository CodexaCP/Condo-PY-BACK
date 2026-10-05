using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildingExpenseCreditNoteUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuildingExpenseCreditNotes_CompanyId",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.AddColumn<string>(
                name: "NumeroKey",
                table: "BuildingExpenseCreditNotes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupplierKey",
                table: "BuildingExpenseCreditNotes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupplierName",
                table: "BuildingExpenseCreditNotes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TimbradoKey",
                table: "BuildingExpenseCreditNotes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNotes_CompanyId_SupplierKey_TimbradoKey_NumeroKey",
                table: "BuildingExpenseCreditNotes",
                columns: new[] { "CompanyId", "SupplierKey", "TimbradoKey", "NumeroKey" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 'Applied'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuildingExpenseCreditNotes_CompanyId_SupplierKey_TimbradoKey_NumeroKey",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "NumeroKey",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "SupplierKey",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "SupplierName",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.DropColumn(
                name: "TimbradoKey",
                table: "BuildingExpenseCreditNotes");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNotes_CompanyId",
                table: "BuildingExpenseCreditNotes",
                column: "CompanyId");
        }
    }
}
