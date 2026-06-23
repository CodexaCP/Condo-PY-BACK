using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "ApplicationUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CondominiumId",
                table: "ApplicationUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "ApplicationUsers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "ApplicationUsers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "ApplicationUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhonePrefix",
                table: "ApplicationUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "ApplicationUsers",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000001"),
                columns: new[] { "Address", "CondominiumId", "FirstName", "LastName", "Phone", "PhonePrefix", "Username" },
                values: new object[] { null, null, "Super", "Admin", null, null, "superadmin" });

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

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_CondominiumId",
                table: "ApplicationUsers",
                column: "CondominiumId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationUsers_Condominiums_CondominiumId",
                table: "ApplicationUsers",
                column: "CondominiumId",
                principalTable: "Condominiums",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationUsers_Condominiums_CondominiumId",
                table: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CompanyId_Username",
                table: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_CondominiumId",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "CondominiumId",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "PhonePrefix",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "ApplicationUsers");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers",
                columns: new[] { "CompanyId", "Email" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");
        }
    }
}
