using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeriodClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PeriodClosingEnabled",
                table: "FinanceSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FinancePeriodClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReopenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancePeriodClosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancePeriodClosures_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancePeriodClosures_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancePeriodClosures_BuildingId_Year_Month",
                table: "FinancePeriodClosures",
                columns: new[] { "BuildingId", "Year", "Month" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ReopenedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancePeriodClosures_BuildingId_Year_Month_ReopenedAtUtc",
                table: "FinancePeriodClosures",
                columns: new[] { "BuildingId", "Year", "Month", "ReopenedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancePeriodClosures_CompanyId",
                table: "FinancePeriodClosures",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancePeriodClosures");

            migrationBuilder.DropColumn(
                name: "PeriodClosingEnabled",
                table: "FinanceSettings");
        }
    }
}
