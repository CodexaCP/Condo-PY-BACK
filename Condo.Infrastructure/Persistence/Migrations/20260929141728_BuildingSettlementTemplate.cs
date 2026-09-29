using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildingSettlementTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReceiptTemplateUrl",
                table: "Buildings",
                newName: "SettlementTemplateUrl");

            migrationBuilder.RenameColumn(
                name: "ReceiptTemplateFileName",
                table: "Buildings",
                newName: "SettlementTemplateFileName");

            // Lo adjuntado hasta hoy era un modelo de comprobante (nunca se uso en ningun PDF), no una
            // liquidacion: se limpia para que cada edificio con modelos propios adjunte el de liquidacion.
            migrationBuilder.Sql(
                "UPDATE [Buildings] SET [SettlementTemplateUrl] = NULL, [SettlementTemplateFileName] = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SettlementTemplateUrl",
                table: "Buildings",
                newName: "ReceiptTemplateUrl");

            migrationBuilder.RenameColumn(
                name: "SettlementTemplateFileName",
                table: "Buildings",
                newName: "ReceiptTemplateFileName");
        }
    }
}
