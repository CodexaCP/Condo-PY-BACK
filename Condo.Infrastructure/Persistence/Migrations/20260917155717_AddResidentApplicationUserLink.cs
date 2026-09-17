using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentApplicationUserLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationUserId",
                table: "Residents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Residents_ApplicationUserId",
                table: "Residents",
                column: "ApplicationUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Residents_ApplicationUsers_ApplicationUserId",
                table: "Residents",
                column: "ApplicationUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill: vincula cada Resident existente al ApplicationUser de la misma empresa
            // que tenga el mismo email (mismo criterio que antes usaba el acceso por email,
            // ahora fijado por Id de forma permanente).
            migrationBuilder.Sql(@"
                UPDATE r
                SET r.ApplicationUserId = u.Id
                FROM [Residents] r
                INNER JOIN [ApplicationUsers] u
                    ON u.CompanyId = r.CompanyId
                   AND u.Email = r.Email
                   AND u.IsDeleted = 0
                WHERE r.IsDeleted = 0 AND r.ApplicationUserId IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Residents_ApplicationUsers_ApplicationUserId",
                table: "Residents");

            migrationBuilder.DropIndex(
                name: "IX_Residents_ApplicationUserId",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "ApplicationUserId",
                table: "Residents");
        }
    }
}
