using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnerPaymentWebChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "OwnerPayments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "App");

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "OwnerPayments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "OwnerPayments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "BankTransfer");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "OwnerPayments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "OwnerPayments",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channel",
                table: "OwnerPayments");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "OwnerPayments");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "OwnerPayments");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "OwnerPayments");

            migrationBuilder.DropColumn(
                name: "ReversedAt",
                table: "OwnerPayments");
        }
    }
}
