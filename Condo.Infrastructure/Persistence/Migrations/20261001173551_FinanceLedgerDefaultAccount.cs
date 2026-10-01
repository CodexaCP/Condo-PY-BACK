using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceLedgerDefaultAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultAccountId",
                table: "FinanceSettings",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultAccountId",
                table: "FinanceSettings");
        }
    }
}
