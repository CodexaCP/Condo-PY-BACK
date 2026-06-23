using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NullableCondominiumCompanyAndBuildingCondoIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_CondominiumId",
                table: "Buildings");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "Condominiums",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_CondominiumId_Code",
                table: "Buildings");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "Condominiums",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CondominiumId",
                table: "Buildings",
                column: "CondominiumId");
        }
    }
}
