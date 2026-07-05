using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingLateFeeConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoLateFeeIntervalIndex",
                table: "ExpenseCharges",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateFeeFrequency",
                table: "Buildings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LateFeeRatePercentage",
                table: "Buildings",
                type: "decimal(5,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoLateFeeIntervalIndex",
                table: "ExpenseCharges");

            migrationBuilder.DropColumn(
                name: "LateFeeFrequency",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LateFeeRatePercentage",
                table: "Buildings");
        }
    }
}
