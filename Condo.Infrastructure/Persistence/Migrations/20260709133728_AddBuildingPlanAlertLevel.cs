using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingPlanAlertLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "AlertLevel",
                table: "BuildingPlans",
                type: "tinyint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlertLevel",
                table: "BuildingPlans");
        }
    }
}
