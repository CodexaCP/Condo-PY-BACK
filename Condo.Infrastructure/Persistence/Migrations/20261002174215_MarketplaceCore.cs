using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MarketplaceReservationId",
                table: "OwnerCreditMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MarketplaceEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceEvents_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HourlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceListings_ApplicationUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceListings_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceListings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceListings_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Hours = table.Column<int>(type: "int", nullable: false),
                    HourlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OwnerNetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreditStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreditedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservations_ApplicationUsers_BuyerUserId",
                        column: x => x.BuyerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservations_ApplicationUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservations_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservations_MarketplaceListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "MarketplaceListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservations_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceAccountMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Concept = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceAccountMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceAccountMovements_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceAccountMovements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceAccountMovements_MarketplaceReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "MarketplaceReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplacePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComprobanteUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReviewedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplacePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplacePayments_ApplicationUsers_BuyerUserId",
                        column: x => x.BuyerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplacePayments_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplacePayments_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplacePayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplacePayments_MarketplaceReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "MarketplaceReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceReservationSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceReservationSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservationSlots_MarketplaceListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "MarketplaceListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketplaceReservationSlots_MarketplaceReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "MarketplaceReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerCreditMovements_OneLotPerMarketplaceReservation",
                table: "OwnerCreditMovements",
                column: "MarketplaceReservationId",
                unique: true,
                filter: "[MarketplaceReservationId] IS NOT NULL AND [Kind] = 'Generated' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceAccountMovements_BuildingId_OccurredAtUtc",
                table: "MarketplaceAccountMovements",
                columns: new[] { "BuildingId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceAccountMovements_CompanyId",
                table: "MarketplaceAccountMovements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceAccountMovements_OnePerReservationKind",
                table: "MarketplaceAccountMovements",
                columns: new[] { "ReservationId", "Kind" },
                unique: true,
                filter: "[ReservationId] IS NOT NULL AND [Kind] <> 'Adjustment' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEvents_BuildingId_TimestampUtc",
                table: "MarketplaceEvents",
                columns: new[] { "BuildingId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEvents_CompanyId",
                table: "MarketplaceEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEvents_EntityType_EntityId_TimestampUtc",
                table: "MarketplaceEvents",
                columns: new[] { "EntityType", "EntityId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceListings_BuildingId_Status_WindowStartUtc",
                table: "MarketplaceListings",
                columns: new[] { "BuildingId", "Status", "WindowStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceListings_CompanyId",
                table: "MarketplaceListings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceListings_OwnerId",
                table: "MarketplaceListings",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceListings_UnitId_WindowStartUtc",
                table: "MarketplaceListings",
                columns: new[] { "UnitId", "WindowStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_BuildingId_Status_SubmittedAtUtc",
                table: "MarketplacePayments",
                columns: new[] { "BuildingId", "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_BuyerUserId",
                table: "MarketplacePayments",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_CompanyId",
                table: "MarketplacePayments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_OneApprovedPerReservation",
                table: "MarketplacePayments",
                column: "ReservationId",
                unique: true,
                filter: "[Status] = 'Approved' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_ReservationId",
                table: "MarketplacePayments",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_ReviewedByUserId",
                table: "MarketplacePayments",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_BuildingId_Status_StartsAtUtc",
                table: "MarketplaceReservations",
                columns: new[] { "BuildingId", "Status", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_BuyerUserId",
                table: "MarketplaceReservations",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_CompanyId_Reference",
                table: "MarketplaceReservations",
                columns: new[] { "CompanyId", "Reference" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_ListingId_Status",
                table: "MarketplaceReservations",
                columns: new[] { "ListingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_OnePendingPerBuyer",
                table: "MarketplaceReservations",
                column: "BuyerUserId",
                unique: true,
                filter: "[Status] = 'PendingPayment' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_OwnerId_CreditStatus",
                table: "MarketplaceReservations",
                columns: new[] { "OwnerId", "CreditStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_Status_ExpiresAtUtc",
                table: "MarketplaceReservations",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservations_UnitId",
                table: "MarketplaceReservations",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservationSlots_NoDoubleBooking",
                table: "MarketplaceReservationSlots",
                columns: new[] { "ListingId", "SlotStartUtc" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceReservationSlots_ReservationId",
                table: "MarketplaceReservationSlots",
                column: "ReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceAccountMovements");

            migrationBuilder.DropTable(
                name: "MarketplaceEvents");

            migrationBuilder.DropTable(
                name: "MarketplacePayments");

            migrationBuilder.DropTable(
                name: "MarketplaceReservationSlots");

            migrationBuilder.DropTable(
                name: "MarketplaceReservations");

            migrationBuilder.DropTable(
                name: "MarketplaceListings");

            migrationBuilder.DropIndex(
                name: "IX_OwnerCreditMovements_OneLotPerMarketplaceReservation",
                table: "OwnerCreditMovements");

            migrationBuilder.DropColumn(
                name: "MarketplaceReservationId",
                table: "OwnerCreditMovements");
        }
    }
}
