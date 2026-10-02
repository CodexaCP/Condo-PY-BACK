using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceCancellationsAndClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StartNoticeSentAtUtc",
                table: "MarketplaceReservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartResponse",
                table: "MarketplaceReservations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartResponseAtUtc",
                table: "MarketplaceReservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartResponseReason",
                table: "MarketplaceReservations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MarketplaceClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedBy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceClaims_ApplicationUsers_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceClaims_ApplicationUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceClaims_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceClaims_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceClaims_MarketplaceReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "MarketplaceReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceOwnerDebts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceOwnerDebts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceOwnerDebts_ApplicationUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceOwnerDebts_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceOwnerDebts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceOwnerDebts_MarketplaceReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "MarketplaceReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReturnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverdueAlertSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceRefunds_ApplicationUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceRefunds_ApplicationUsers_ReturnedByUserId",
                        column: x => x.ReturnedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceRefunds_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceRefunds_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceRefunds_MarketplaceReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "MarketplaceReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_Status_StartNoticeSentAtUtc_StartsAtUtc",
                table: "MarketplaceReservations",
                columns: new[] { "Status", "StartNoticeSentAtUtc", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceClaims_BuildingId_Status_CreatedAtUtc",
                table: "MarketplaceClaims",
                columns: new[] { "BuildingId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceClaims_CompanyId",
                table: "MarketplaceClaims",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceClaims_OneOpenPerReservation",
                table: "MarketplaceClaims",
                column: "ReservationId",
                unique: true,
                filter: "[Status] = 'Open' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceClaims_OpenedByUserId",
                table: "MarketplaceClaims",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceClaims_ResolvedByUserId",
                table: "MarketplaceClaims",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOwnerDebts_BuildingId",
                table: "MarketplaceOwnerDebts",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOwnerDebts_CompanyId",
                table: "MarketplaceOwnerDebts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOwnerDebts_OnePerReservation",
                table: "MarketplaceOwnerDebts",
                column: "ReservationId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOwnerDebts_OwnerId_BuildingId_SettledAtUtc",
                table: "MarketplaceOwnerDebts",
                columns: new[] { "OwnerId", "BuildingId", "SettledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRefunds_BuildingId_Status_CreatedAtUtc",
                table: "MarketplaceRefunds",
                columns: new[] { "BuildingId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRefunds_CompanyId",
                table: "MarketplaceRefunds",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRefunds_OnePerReservation",
                table: "MarketplaceRefunds",
                column: "ReservationId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRefunds_RecipientUserId",
                table: "MarketplaceRefunds",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRefunds_ReturnedByUserId",
                table: "MarketplaceRefunds",
                column: "ReturnedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceClaims");

            migrationBuilder.DropTable(
                name: "MarketplaceOwnerDebts");

            migrationBuilder.DropTable(
                name: "MarketplaceRefunds");

            migrationBuilder.DropIndex(
                name: "IX_MarketplaceReservations_Status_StartNoticeSentAtUtc_StartsAtUtc",
                table: "MarketplaceReservations");

            migrationBuilder.DropColumn(
                name: "StartNoticeSentAtUtc",
                table: "MarketplaceReservations");

            migrationBuilder.DropColumn(
                name: "StartResponse",
                table: "MarketplaceReservations");

            migrationBuilder.DropColumn(
                name: "StartResponseAtUtc",
                table: "MarketplaceReservations");

            migrationBuilder.DropColumn(
                name: "StartResponseReason",
                table: "MarketplaceReservations");
        }
    }
}
