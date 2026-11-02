using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiCountryCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInformative",
                table: "Votes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MajorityRule",
                table: "Votes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Simple");

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Companies",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "PY");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Companies",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "PYG");

            migrationBuilder.AddColumn<string>(
                name: "LocaleCode",
                table: "Companies",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "es-PY");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxGeneralRate",
                table: "Companies",
                type: "decimal(5,2)",
                nullable: true,
                defaultValue: 10m);

            migrationBuilder.AddColumn<string>(
                name: "TaxName",
                table: "Companies",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "IVA");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxReducedRate",
                table: "Companies",
                type: "decimal(5,2)",
                nullable: true,
                defaultValue: 5m);

            migrationBuilder.AddColumn<string>(
                name: "AdministratorRegistryNumber",
                table: "Buildings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FiscalYearEndMonth",
                table: "Buildings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Jurisdiction",
                table: "Buildings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementApprovalMode",
                table: "Buildings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ManagerPresidentAdmin");

            // En SQL Server un DEFAULT sobre una columna que admite NULL no rellena las filas que ya existen: las empresas actuales son todas
            // de Paraguay, asi que se les fija el IVA 10 % / 5 % de siempre.
            migrationBuilder.Sql("UPDATE [Companies] SET [TaxGeneralRate] = 10.0, [TaxReducedRate] = 5.0 WHERE [TaxGeneralRate] IS NULL AND [TaxReducedRate] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsInformative",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "MajorityRule",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "LocaleCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TaxGeneralRate",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TaxName",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TaxReducedRate",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AdministratorRegistryNumber",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "FiscalYearEndMonth",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "Jurisdiction",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "SettlementApprovalMode",
                table: "Buildings");
        }
    }
}
