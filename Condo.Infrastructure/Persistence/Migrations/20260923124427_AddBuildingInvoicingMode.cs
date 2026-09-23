using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingInvoicingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Edificios existentes: se asumen Preimpresa (valor 1) hasta que el SuperAdmin lo configure.
            migrationBuilder.AddColumn<int>(
                name: "InvoicingMode",
                table: "Buildings",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoicingMode",
                table: "Buildings");
        }
    }
}
