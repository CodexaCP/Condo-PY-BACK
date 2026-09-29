using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnerPaymentReferencePerCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OwnerPayments_Reference",
                table: "OwnerPayments");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayments_CompanyId_Reference",
                table: "OwnerPayments",
                columns: new[] { "CompanyId", "Reference" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OwnerPayments_CompanyId_Reference",
                table: "OwnerPayments");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayments_Reference",
                table: "OwnerPayments",
                column: "Reference",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
