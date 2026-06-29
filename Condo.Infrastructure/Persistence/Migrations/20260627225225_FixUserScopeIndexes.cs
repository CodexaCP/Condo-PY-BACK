using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixUserScopeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CompanyId_Username",
                table: "ApplicationUsers");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers",
                columns: new[] { "CompanyId", "Email" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_CompanyId_Username",
                table: "ApplicationUsers",
                columns: new[] { "CompanyId", "Username" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [CompanyId] IS NOT NULL AND [Username] != ''");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_Email",
                table: "ApplicationUsers",
                column: "Email",
                unique: true,
                filter: "[IsDeleted] = 0 AND [CompanyId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_Username",
                table: "ApplicationUsers",
                column: "Username",
                unique: true,
                filter: "[IsDeleted] = 0 AND [CompanyId] IS NULL AND [Username] != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CompanyId_Username",
                table: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_Email",
                table: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_Username",
                table: "ApplicationUsers");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers",
                columns: new[] { "CompanyId", "Email" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_CompanyId_Username",
                table: "ApplicationUsers",
                columns: new[] { "CompanyId", "Username" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Username] != ''");
        }
    }
}
