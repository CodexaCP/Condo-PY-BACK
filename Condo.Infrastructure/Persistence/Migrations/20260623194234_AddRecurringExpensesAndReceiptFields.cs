using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringExpensesAndReceiptFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptFileName",
                table: "BuildingExpenses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptStoredName",
                table: "BuildingExpenses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringBuildingExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DistributionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringBuildingExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringBuildingExpenses_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringBuildingExpenses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringBuildingExpenses_Units_TargetUnitId",
                        column: x => x.TargetUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringBuildingExpenses_BuildingId",
                table: "RecurringBuildingExpenses",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringBuildingExpenses_CompanyId",
                table: "RecurringBuildingExpenses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringBuildingExpenses_TargetUnitId",
                table: "RecurringBuildingExpenses",
                column: "TargetUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringBuildingExpenses");

            migrationBuilder.DropColumn(
                name: "ReceiptFileName",
                table: "BuildingExpenses");

            migrationBuilder.DropColumn(
                name: "ReceiptStoredName",
                table: "BuildingExpenses");
        }
    }
}
