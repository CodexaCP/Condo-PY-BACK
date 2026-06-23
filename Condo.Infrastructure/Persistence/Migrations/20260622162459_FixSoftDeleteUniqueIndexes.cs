using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixSoftDeleteUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums");

            migrationBuilder.DropIndex(
                name: "IX_Companies_Slug",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_CondominiumId_Code",
                table: "Buildings");

            migrationBuilder.CreateIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Slug",
                table: "Companies",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CondominiumId_Code",
                table: "Buildings",
                columns: new[] { "CondominiumId", "Code" },
                unique: true,
                filter: "[CondominiumId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums");

            migrationBuilder.DropIndex(
                name: "IX_Companies_Slug",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_CondominiumId_Code",
                table: "Buildings");

            migrationBuilder.CreateIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Slug",
                table: "Companies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CondominiumId_Code",
                table: "Buildings",
                columns: new[] { "CondominiumId", "Code" },
                unique: true,
                filter: "[CondominiumId] IS NOT NULL");
        }
    }
}
