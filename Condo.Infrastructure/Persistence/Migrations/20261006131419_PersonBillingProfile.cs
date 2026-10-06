using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonBillingProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "UnitOwners",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnershipPercentage",
                table: "UnitOwners",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferReason",
                table: "UnitOwners",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "Residents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "Residents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                table: "Residents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeaseEndDate",
                table: "Residents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseFileName",
                table: "Residents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseUrl",
                table: "Residents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "Residents",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Relationship",
                table: "Residents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientAddress",
                table: "Invoices",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientDocument",
                table: "Invoices",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientDocumentType",
                table: "Invoices",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientEmail",
                table: "Invoices",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientName",
                table: "Invoices",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ClientReconstructed",
                table: "Invoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "ApplicationUsers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceAddress",
                table: "ApplicationUsers",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceDocument",
                table: "ApplicationUsers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceDocumentType",
                table: "ApplicationUsers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceEmail",
                table: "ApplicationUsers",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceName",
                table: "ApplicationUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "ApplicationUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "ApplicationUsers",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonType",
                table: "ApplicationUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryPhone",
                table: "ApplicationUsers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhone",
                table: "ApplicationUsers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000001"),
                columns: new[] { "BirthDate", "InvoiceAddress", "InvoiceDocument", "InvoiceDocumentType", "InvoiceEmail", "InvoiceName", "LegalName", "Nationality", "PersonType", "SecondaryPhone", "WhatsAppPhone" },
                values: new object[] { null, null, null, null, null, null, null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "UnitOwners");

            migrationBuilder.DropColumn(
                name: "OwnershipPercentage",
                table: "UnitOwners");

            migrationBuilder.DropColumn(
                name: "TransferReason",
                table: "UnitOwners");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "EmergencyContactPhone",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "LeaseEndDate",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "LeaseFileName",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "LeaseUrl",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "Relationship",
                table: "Residents");

            migrationBuilder.DropColumn(
                name: "ClientAddress",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ClientDocument",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ClientDocumentType",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ClientEmail",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ClientName",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ClientReconstructed",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "InvoiceAddress",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "InvoiceDocument",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "InvoiceDocumentType",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "InvoiceEmail",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "InvoiceName",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "PersonType",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "SecondaryPhone",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhone",
                table: "ApplicationUsers");
        }
    }
}
