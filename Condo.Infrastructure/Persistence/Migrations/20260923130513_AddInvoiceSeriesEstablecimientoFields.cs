using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceSeriesEstablecimientoFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActividadEconomica",
                table: "InvoiceSeries",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DireccionEstablecimiento",
                table: "InvoiceSeries",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImprentaNumeroHabilitacion",
                table: "InvoiceSeries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImprentaRazonSocial",
                table: "InvoiceSeries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImprentaRuc",
                table: "InvoiceSeries",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActividadEconomica",
                table: "InvoiceSeries");

            migrationBuilder.DropColumn(
                name: "DireccionEstablecimiento",
                table: "InvoiceSeries");

            migrationBuilder.DropColumn(
                name: "ImprentaNumeroHabilitacion",
                table: "InvoiceSeries");

            migrationBuilder.DropColumn(
                name: "ImprentaRazonSocial",
                table: "InvoiceSeries");

            migrationBuilder.DropColumn(
                name: "ImprentaRuc",
                table: "InvoiceSeries");
        }
    }
}
