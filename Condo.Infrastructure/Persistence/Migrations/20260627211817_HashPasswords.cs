using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HashPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000001"),
                column: "PasswordHash",
                value: "$2a$11$Y7ysfENQB6qvGrf5rxRPuer0C8iSxyLeVCbqBJw2jADBZb/wVqbrO");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000001"),
                column: "PasswordHash",
                value: "Condo*2026");
        }
    }
}
