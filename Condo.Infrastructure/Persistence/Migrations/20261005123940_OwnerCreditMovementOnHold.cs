using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnerCreditMovementOnHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OnHold",
                table: "OwnerCreditMovements",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnHold",
                table: "OwnerCreditMovements");
        }
    }
}
