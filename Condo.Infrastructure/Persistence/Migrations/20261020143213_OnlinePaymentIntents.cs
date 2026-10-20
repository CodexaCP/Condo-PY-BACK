using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlinePaymentIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HolderBankNote",
                table: "PaymentConnections",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HolderName",
                table: "PaymentConnections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HolderTaxId",
                table: "PaymentConnections",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentIntentId",
                table: "OwnerPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "BuildingPaymentConfigs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PaymentIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CloseReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OwnerPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HolderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HolderTaxId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsSimulated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentIntents_ApplicationUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentIntents_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentIntents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentIntents_PaymentConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "PaymentConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<long>(type: "bigint", nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderSessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AuthorizationCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderStatusRaw = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CardBrand = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CardLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ReconciledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReconciledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettlementReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_PaymentConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "PaymentConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_PaymentIntents_IntentId",
                        column: x => x.IntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentIntentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpensePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentIntentItems_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentIntentItems_ExpensePeriods_ExpensePeriodId",
                        column: x => x.ExpensePeriodId,
                        principalTable: "ExpensePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentIntentItems_PaymentIntents_IntentId",
                        column: x => x.IntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentIntentItems_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DedupeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SignatureOk = table.Column<bool>(type: "bit", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_PaymentAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_PaymentConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "PaymentConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayments_PaymentIntentId",
                table: "OwnerPayments",
                column: "PaymentIntentId",
                unique: true,
                filter: "[PaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_AttemptNumber",
                table: "PaymentAttempts",
                column: "AttemptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_CompanyId",
                table: "PaymentAttempts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_ConnectionId_ProviderReference",
                table: "PaymentAttempts",
                columns: new[] { "ConnectionId", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_ConnectionId_ProviderSessionId",
                table: "PaymentAttempts",
                columns: new[] { "ConnectionId", "ProviderSessionId" },
                unique: true,
                filter: "[ProviderSessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_IntentId",
                table: "PaymentAttempts",
                column: "IntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Status_ExpiresAtUtc",
                table: "PaymentAttempts",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_AttemptId",
                table: "PaymentEvents",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_CompanyId",
                table: "PaymentEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ConnectionId_ReceivedAtUtc",
                table: "PaymentEvents",
                columns: new[] { "ConnectionId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_DedupeKey",
                table: "PaymentEvents",
                column: "DedupeKey",
                unique: true,
                filter: "[DedupeKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntentItems_CompanyId",
                table: "PaymentIntentItems",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntentItems_ExpensePeriodId",
                table: "PaymentIntentItems",
                column: "ExpensePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntentItems_IntentId",
                table: "PaymentIntentItems",
                column: "IntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntentItems_UnitId_ExpensePeriodId",
                table: "PaymentIntentItems",
                columns: new[] { "UnitId", "ExpensePeriodId" },
                unique: true,
                filter: "[IsOpen] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_BuildingId_CreatedAtUtc",
                table: "PaymentIntents",
                columns: new[] { "BuildingId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_CompanyId",
                table: "PaymentIntents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_ConnectionId",
                table: "PaymentIntents",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Number",
                table: "PaymentIntents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_OwnerId_Status",
                table: "PaymentIntents",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_OwnerPayments_PaymentIntents_PaymentIntentId",
                table: "OwnerPayments",
                column: "PaymentIntentId",
                principalTable: "PaymentIntents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OwnerPayments_PaymentIntents_PaymentIntentId",
                table: "OwnerPayments");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "PaymentIntentItems");

            migrationBuilder.DropTable(
                name: "PaymentAttempts");

            migrationBuilder.DropTable(
                name: "PaymentIntents");

            migrationBuilder.DropIndex(
                name: "IX_OwnerPayments_PaymentIntentId",
                table: "OwnerPayments");

            migrationBuilder.DropColumn(
                name: "HolderBankNote",
                table: "PaymentConnections");

            migrationBuilder.DropColumn(
                name: "HolderName",
                table: "PaymentConnections");

            migrationBuilder.DropColumn(
                name: "HolderTaxId",
                table: "PaymentConnections");

            migrationBuilder.DropColumn(
                name: "PaymentIntentId",
                table: "OwnerPayments");

            migrationBuilder.DropColumn(
                name: "IsTest",
                table: "BuildingPaymentConfigs");
        }
    }
}
