using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvertisingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdsEnabled",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AdCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdvertiserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CtaText = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CtaUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NotifyBeforeExpiry = table.Column<bool>(type: "bit", nullable: false),
                    ExpiryNotificationSent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdCampaigns_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdCampaigns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdCampaignBuildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdCampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaignBuildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdCampaignBuildings_AdCampaigns_AdCampaignId",
                        column: x => x.AdCampaignId,
                        principalTable: "AdCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdCampaignBuildings_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaignBuildings_AdCampaignId_BuildingId",
                table: "AdCampaignBuildings",
                columns: new[] { "AdCampaignId", "BuildingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaignBuildings_BuildingId",
                table: "AdCampaignBuildings",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaigns_CompanyId_IsActive_StartDate_EndDate",
                table: "AdCampaigns",
                columns: new[] { "CompanyId", "IsActive", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaigns_CreatedByUserId",
                table: "AdCampaigns",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdCampaignBuildings");

            migrationBuilder.DropTable(
                name: "AdCampaigns");

            migrationBuilder.DropColumn(
                name: "AdsEnabled",
                table: "Buildings");
        }
    }
}
