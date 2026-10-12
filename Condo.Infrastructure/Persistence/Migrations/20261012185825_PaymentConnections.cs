using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PublicId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SecretsCipher = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    KeyId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SecretsFingerprint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SecretsUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentConnections_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuildingPaymentConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingPaymentConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingPaymentConfigs_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPaymentConfigs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPaymentConfigs_PaymentConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "PaymentConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentConnectionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentConnectionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentConnectionAudits_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentConnectionAudits_PaymentConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "PaymentConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPaymentConfigs_BuildingId_ConnectionId",
                table: "BuildingPaymentConfigs",
                columns: new[] { "BuildingId", "ConnectionId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPaymentConfigs_CompanyId",
                table: "BuildingPaymentConfigs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPaymentConfigs_ConnectionId",
                table: "BuildingPaymentConfigs",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentConnectionAudits_CompanyId",
                table: "PaymentConnectionAudits",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentConnectionAudits_ConnectionId_CreatedAtUtc",
                table: "PaymentConnectionAudits",
                columns: new[] { "ConnectionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentConnections_CompanyId_Name",
                table: "PaymentConnections",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentConnections_PublicId",
                table: "PaymentConnections",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingPaymentConfigs");

            migrationBuilder.DropTable(
                name: "PaymentConnectionAudits");

            migrationBuilder.DropTable(
                name: "PaymentConnections");
        }
    }
}
