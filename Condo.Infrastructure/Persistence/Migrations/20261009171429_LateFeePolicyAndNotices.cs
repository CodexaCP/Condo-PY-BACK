using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LateFeePolicyAndNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LateFeeExempt",
                table: "Units",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LateFeeExemptAtUtc",
                table: "Units",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LateFeeExemptByUserId",
                table: "Units",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateFeeExemptReason",
                table: "Units",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BudgetWarnPercent",
                table: "FinanceSettings",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "FundPolicyConfirmed",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LateFeeAppliesToExtraordinary",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "LateFeeAppliesToIndividual",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "LateFeeAppliesToReserve",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LateFeeCapPercentage",
                table: "Buildings",
                type: "decimal(7,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LateFeeMinAmount",
                table: "Buildings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LateFeePolicyConfirmed",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReserveUsePolicy",
                table: "Buildings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "FreeUse");

            migrationBuilder.AddColumn<decimal>(
                name: "ReserveUseThreshold",
                table: "Buildings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuildingNoticeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OffsetDays = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingNoticeRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingNoticeRules_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingNoticeRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingNoticeRules_BuildingId_Kind",
                table: "BuildingNoticeRules",
                columns: new[] { "BuildingId", "Kind" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingNoticeRules_CompanyId",
                table: "BuildingNoticeRules",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingNoticeRules");

            migrationBuilder.DropColumn(
                name: "LateFeeExempt",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "LateFeeExemptAtUtc",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "LateFeeExemptByUserId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "LateFeeExemptReason",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "BudgetWarnPercent",
                table: "FinanceSettings");

            migrationBuilder.DropColumn(
                name: "FundPolicyConfirmed",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LateFeeAppliesToExtraordinary",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LateFeeAppliesToIndividual",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LateFeeAppliesToReserve",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LateFeeCapPercentage",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LateFeeMinAmount",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LateFeePolicyConfirmed",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "ReserveUsePolicy",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "ReserveUseThreshold",
                table: "Buildings");
        }
    }
}
