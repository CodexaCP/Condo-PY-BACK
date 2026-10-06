using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildingRegistrationProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdministratorName",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdministratorPhone",
                table: "Buildings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BylawsFileName",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BylawsUrl",
                table: "Buildings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CadastralAccount",
                table: "Buildings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Buildings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultDueDay",
                table: "Buildings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Buildings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EconomicActivity",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                table: "Buildings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FincaNumber",
                table: "Buildings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalAddress",
                table: "Buildings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FloorsCount",
                table: "Buildings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GraceDays",
                table: "Buildings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceEmail",
                table: "Buildings",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Buildings",
                type: "decimal(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LegalEntityDate",
                table: "Buildings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalEntityNumber",
                table: "Buildings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationReference",
                table: "Buildings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Buildings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Buildings",
                type: "decimal(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Neighborhood",
                table: "Buildings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficeHours",
                table: "Buildings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PadronNumber",
                table: "Buildings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentInstructions",
                table: "Buildings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropertyType",
                table: "Buildings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ruc",
                table: "Buildings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxpayerType",
                table: "Buildings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Buildings",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TowersCount",
                table: "Buildings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitsCount",
                table: "Buildings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatRegime",
                table: "Buildings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhone",
                table: "Buildings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearBuilt",
                table: "Buildings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuildingBankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AccountType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    HolderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HolderDocument = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Alias = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingBankAccounts_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingBankAccounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingBankAccounts_BuildingId",
                table: "BuildingBankAccounts",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingBankAccounts_CompanyId",
                table: "BuildingBankAccounts",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingBankAccounts");

            migrationBuilder.DropColumn(
                name: "AdministratorName",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "AdministratorPhone",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "BylawsFileName",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "BylawsUrl",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "CadastralAccount",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "DefaultDueDay",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "EconomicActivity",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "EmergencyContactPhone",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "FincaNumber",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "FiscalAddress",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "FloorsCount",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "GraceDays",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "InvoiceEmail",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LegalEntityDate",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LegalEntityNumber",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LocationReference",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "Neighborhood",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "OfficeHours",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PadronNumber",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PaymentInstructions",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PropertyType",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "Ruc",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "TaxpayerType",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "TowersCount",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "UnitsCount",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "VatRegime",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhone",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "YearBuilt",
                table: "Buildings");
        }
    }
}
