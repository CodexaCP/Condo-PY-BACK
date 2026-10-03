using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceHandoverNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketplaceHandoverNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Trigger = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ReservationIds = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ReservationCount = table.Column<int>(type: "int", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceHandoverNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceHandoverNotes_ApplicationUsers_NewOwnerId",
                        column: x => x.NewOwnerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceHandoverNotes_ApplicationUsers_PreviousOwnerId",
                        column: x => x.PreviousOwnerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceHandoverNotes_ApplicationUsers_ReadByUserId",
                        column: x => x.ReadByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceHandoverNotes_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceHandoverNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceHandoverNotes_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceHandoverNotes_BuildingId_ReadAtUtc_CreatedAtUtc",
                table: "MarketplaceHandoverNotes",
                columns: new[] { "BuildingId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceHandoverNotes_CompanyId",
                table: "MarketplaceHandoverNotes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceHandoverNotes_NewOwnerId",
                table: "MarketplaceHandoverNotes",
                column: "NewOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceHandoverNotes_PreviousOwnerId",
                table: "MarketplaceHandoverNotes",
                column: "PreviousOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceHandoverNotes_ReadByUserId",
                table: "MarketplaceHandoverNotes",
                column: "ReadByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceHandoverNotes_UnitId_CreatedAtUtc",
                table: "MarketplaceHandoverNotes",
                columns: new[] { "UnitId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceHandoverNotes");
        }
    }
}
