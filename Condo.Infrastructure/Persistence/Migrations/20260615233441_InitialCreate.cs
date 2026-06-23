using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationUsers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Condominiums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Condominiums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Condominiums_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Residents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsOwner = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Residents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Residents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CondominiumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Buildings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Buildings_Condominiums_CondominiumId",
                        column: x => x.CondominiumId,
                        principalTable: "Condominiums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpensePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensePeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpensePeriods_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpensePeriods_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Floor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Coefficient = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Units_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Units_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBuildingAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBuildingAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBuildingAccesses_ApplicationUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserBuildingAccesses_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuildingIncomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpensePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncomeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingIncomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingIncomes_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingIncomes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingIncomes_ExpensePeriods_ExpensePeriodId",
                        column: x => x.ExpensePeriodId,
                        principalTable: "ExpensePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpensePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalBuildingExpenses = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalBuildingIncomes = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReserveFundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExtraordinaryAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetCommonAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseSettlements_ApplicationUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseSettlements_ApplicationUsers_GeneratedByUserId",
                        column: x => x.GeneratedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseSettlements_ApplicationUsers_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseSettlements_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseSettlements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseSettlements_ExpensePeriods_ExpensePeriodId",
                        column: x => x.ExpensePeriodId,
                        principalTable: "ExpensePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuildingExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpensePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpenseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DistributionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingExpenses_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenses_ExpensePeriods_ExpensePeriodId",
                        column: x => x.ExpensePeriodId,
                        principalTable: "ExpensePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenses_Units_TargetUnitId",
                        column: x => x.TargetUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpensePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_ExpensePeriods_ExpensePeriodId",
                        column: x => x.ExpensePeriodId,
                        principalTable: "ExpensePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitResidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitResidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitResidents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitResidents_Residents_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Residents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitResidents_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpensePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceBuildingExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceSettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsLateFee = table.Column<bool>(type: "bit", nullable: false),
                    Concept = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseCharges_BuildingExpenses_SourceBuildingExpenseId",
                        column: x => x.SourceBuildingExpenseId,
                        principalTable: "BuildingExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseCharges_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseCharges_ExpensePeriods_ExpensePeriodId",
                        column: x => x.ExpensePeriodId,
                        principalTable: "ExpensePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseCharges_ExpenseSettlements_SourceSettlementId",
                        column: x => x.SourceSettlementId,
                        principalTable: "ExpenseSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseCharges_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ApplicationUsers",
                columns: new[] { "Id", "CompanyId", "CreatedAtUtc", "Email", "FullName", "IsActive", "IsDeleted", "LastLoginAtUtc", "MustChangePassword", "PasswordHash", "Role", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000001"), null, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "superadmin@codexa.local", "Super Admin Codexa", true, false, null, true, "123456", "SuperAdmin", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "CreatedAtUtc", "IsActive", "IsDeleted", "Name", "Slug", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Codexa Administradora Demo", "codexa-admin-demo", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ApplicationUsers",
                columns: new[] { "Id", "CompanyId", "CreatedAtUtc", "Email", "FullName", "IsActive", "IsDeleted", "LastLoginAtUtc", "MustChangePassword", "PasswordHash", "Role", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000003"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "admin@codexa.local", "Admin Empresa Demo", true, false, null, true, "123456", "CompanyAdmin", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000009"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "manager@codexa.local", "Manager Martinica", true, false, null, true, "123456", "BuildingManager", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "Condominiums",
                columns: new[] { "Id", "Address", "Code", "CompanyId", "CreatedAtUtc", "IsActive", "IsDeleted", "Name", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000004"), "Asuncion, Paraguay", "CMZ-01", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Condominio Martinez", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Residents",
                columns: new[] { "Id", "CompanyId", "CreatedAtUtc", "DocumentNumber", "Email", "FullName", "IsActive", "IsDeleted", "IsOwner", "PhoneNumber", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000007"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1234567", "residente@codexa.local", "Residente Demo", true, false, true, "0991000000", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Buildings",
                columns: new[] { "Id", "Address", "Code", "CompanyId", "CondominiumId", "CreatedAtUtc", "IsActive", "IsDeleted", "Name", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "Asuncion, Paraguay", "MART-01", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000004"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Edificio Martinica", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ExpensePeriods",
                columns: new[] { "Id", "BuildingId", "CompanyId", "CreatedAtUtc", "DueDate", "EndDate", "IsDeleted", "Month", "Name", "Notes", "StartDate", "Status", "UpdatedAtUtc", "Year" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 7, 10), new DateOnly(2026, 6, 30), false, 6, "Junio 2026", "Periodo inicial de expensas para pruebas del modulo financiero.", new DateOnly(2026, 6, 1), "Draft", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2026 });

            migrationBuilder.InsertData(
                table: "Units",
                columns: new[] { "Id", "BuildingId", "Code", "Coefficient", "CompanyId", "CreatedAtUtc", "Floor", "IsActive", "IsDeleted", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "A-101", 1.000000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1", true, false, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "UserBuildingAccesses",
                columns: new[] { "Id", "ApplicationUserId", "BuildingId", "CompanyId", "CreatedAtUtc", "IsActive", "IsDeleted", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000010"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000009"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "BuildingExpenses",
                columns: new[] { "Id", "Amount", "BuildingId", "Category", "CompanyId", "CreatedAtUtc", "Description", "DistributionType", "ExpenseDate", "ExpensePeriodId", "IsDeleted", "Notes", "SupplierName", "TargetUnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000014"), 1200000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "Cleaning", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Servicio mensual de limpieza de areas comunes", "ByCoefficient", new DateOnly(2026, 6, 5), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), false, "Gasto semilla para pruebas del modulo de gastos del edificio.", "Limpieza Integral SA", null, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "BuildingIncomes",
                columns: new[] { "Id", "Amount", "BuildingId", "Category", "CompanyId", "CreatedAtUtc", "Description", "ExpensePeriodId", "IncomeDate", "IsDeleted", "Notes", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000015"), 350000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), "OperationalFund", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Compensacion de fondo operativo del periodo", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), new DateOnly(2026, 6, 8), false, "Ingreso semilla para pruebas del modulo de ingresos del edificio.", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ExpenseCharges",
                columns: new[] { "Id", "Amount", "ChargeType", "CompanyId", "Concept", "CreatedAtUtc", "ExpensePeriodId", "IsDeleted", "IsLateFee", "Notes", "SourceBuildingExpenseId", "SourceSettlementId", "UnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000012"), 850000m, "Ordinary", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), "Expensa ordinaria", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), false, false, "Cargo semilla para pruebas del modulo financiero.", null, null, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ExpenseSettlements",
                columns: new[] { "Id", "ApprovedAtUtc", "ApprovedByUserId", "BuildingId", "CompanyId", "CreatedAtUtc", "ExpensePeriodId", "ExtraordinaryAmount", "GeneratedAtUtc", "GeneratedByUserId", "IsDeleted", "NetCommonAmount", "PublishedAtUtc", "PublishedByUserId", "ReserveFundAmount", "Status", "TotalBuildingExpenses", "TotalBuildingIncomes", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000016"), null, null, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000005"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), 0m, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000009"), false, 850000m, null, null, 0m, "Calculated", 1200000m, 350000m, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Payments",
                columns: new[] { "Id", "Amount", "CompanyId", "CreatedAtUtc", "ExpensePeriodId", "IsDeleted", "Method", "Notes", "PaymentDate", "Reference", "UnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000013"), 250000m, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000011"), false, "BankTransfer", "Pago parcial semilla para pruebas del modulo financiero.", new DateOnly(2026, 6, 15), "TRX-0001", new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "UnitResidents",
                columns: new[] { "Id", "CompanyId", "CreatedAtUtc", "EndDate", "IsDeleted", "IsPrimary", "ResidentId", "StartDate", "UnitId", "UpdatedAtUtc" },
                values: new object[] { new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000008"), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000002"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000007"), new DateOnly(2026, 1, 1), new Guid("b781a1aa-6f3d-46d3-8f78-72e7a0000006"), new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_CompanyId_Email",
                table: "ApplicationUsers",
                columns: new[] { "CompanyId", "Email" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_BuildingId_ExpensePeriodId_ExpenseDate",
                table: "BuildingExpenses",
                columns: new[] { "BuildingId", "ExpensePeriodId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_CompanyId",
                table: "BuildingExpenses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_ExpensePeriodId",
                table: "BuildingExpenses",
                column: "ExpensePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenses_TargetUnitId",
                table: "BuildingExpenses",
                column: "TargetUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingIncomes_BuildingId_ExpensePeriodId_IncomeDate",
                table: "BuildingIncomes",
                columns: new[] { "BuildingId", "ExpensePeriodId", "IncomeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingIncomes_CompanyId",
                table: "BuildingIncomes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingIncomes_ExpensePeriodId",
                table: "BuildingIncomes",
                column: "ExpensePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CompanyId_Code",
                table: "Buildings",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CondominiumId",
                table: "Buildings",
                column: "CondominiumId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Slug",
                table: "Companies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Condominiums_CompanyId_Code",
                table: "Condominiums",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCharges_CompanyId",
                table: "ExpenseCharges",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCharges_ExpensePeriodId",
                table: "ExpenseCharges",
                column: "ExpensePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCharges_SourceBuildingExpenseId",
                table: "ExpenseCharges",
                column: "SourceBuildingExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCharges_SourceSettlementId",
                table: "ExpenseCharges",
                column: "SourceSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCharges_UnitId",
                table: "ExpenseCharges",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePeriods_BuildingId_Year_Month",
                table: "ExpensePeriods",
                columns: new[] { "BuildingId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePeriods_CompanyId",
                table: "ExpensePeriods",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_ApprovedByUserId",
                table: "ExpenseSettlements",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_BuildingId",
                table: "ExpenseSettlements",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_CompanyId",
                table: "ExpenseSettlements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_ExpensePeriodId",
                table: "ExpenseSettlements",
                column: "ExpensePeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_GeneratedByUserId",
                table: "ExpenseSettlements",
                column: "GeneratedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettlements_PublishedByUserId",
                table: "ExpenseSettlements",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CompanyId",
                table: "Payments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ExpensePeriodId",
                table: "Payments",
                column: "ExpensePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UnitId",
                table: "Payments",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Residents_CompanyId_DocumentNumber",
                table: "Residents",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitResidents_CompanyId",
                table: "UnitResidents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitResidents_ResidentId",
                table: "UnitResidents",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitResidents_UnitId_ResidentId_StartDate",
                table: "UnitResidents",
                columns: new[] { "UnitId", "ResidentId", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Units_BuildingId_Code",
                table: "Units",
                columns: new[] { "BuildingId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Units_CompanyId",
                table: "Units",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBuildingAccesses_ApplicationUserId_BuildingId",
                table: "UserBuildingAccesses",
                columns: new[] { "ApplicationUserId", "BuildingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBuildingAccesses_BuildingId",
                table: "UserBuildingAccesses",
                column: "BuildingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingIncomes");

            migrationBuilder.DropTable(
                name: "ExpenseCharges");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "UnitResidents");

            migrationBuilder.DropTable(
                name: "UserBuildingAccesses");

            migrationBuilder.DropTable(
                name: "BuildingExpenses");

            migrationBuilder.DropTable(
                name: "ExpenseSettlements");

            migrationBuilder.DropTable(
                name: "Residents");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropTable(
                name: "ExpensePeriods");

            migrationBuilder.DropTable(
                name: "Buildings");

            migrationBuilder.DropTable(
                name: "Condominiums");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}
