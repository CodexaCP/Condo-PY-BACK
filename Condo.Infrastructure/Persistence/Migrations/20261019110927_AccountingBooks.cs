using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingBookSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Holder = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HolderLegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HolderRuc = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HolderAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AccountantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccountantRegistration = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BookNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RubricDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AuthorizedFolios = table.Column<int>(type: "int", nullable: true),
                    UsedFolios = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingBookSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingBookSettings_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingBookSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingBookSettings_BuildingId",
                table: "AccountingBookSettings",
                column: "BuildingId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingBookSettings_CompanyId",
                table: "AccountingBookSettings",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingBookSettings");
        }
    }
}
