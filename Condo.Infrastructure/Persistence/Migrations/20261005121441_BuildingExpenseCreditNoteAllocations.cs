using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildingExpenseCreditNoteAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BuildingId",
                table: "OwnerCreditMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierCreditNoteId",
                table: "OwnerCreditMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                table: "OwnerCreditMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuildingExpenseCreditNoteAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OwnerCreditMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingExpenseCreditNoteAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteAllocations_ApplicationUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteAllocations_BuildingExpenseCreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "BuildingExpenseCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteAllocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteAllocations_OwnerCreditMovements_OwnerCreditMovementId",
                        column: x => x.OwnerCreditMovementId,
                        principalTable: "OwnerCreditMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingExpenseCreditNoteAllocations_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteAllocations_CompanyId",
                table: "BuildingExpenseCreditNoteAllocations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteAllocations_CreditNoteId",
                table: "BuildingExpenseCreditNoteAllocations",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteAllocations_OwnerCreditMovementId",
                table: "BuildingExpenseCreditNoteAllocations",
                column: "OwnerCreditMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteAllocations_OwnerId",
                table: "BuildingExpenseCreditNoteAllocations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingExpenseCreditNoteAllocations_UnitId",
                table: "BuildingExpenseCreditNoteAllocations",
                column: "UnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingExpenseCreditNoteAllocations");

            migrationBuilder.DropColumn(
                name: "BuildingId",
                table: "OwnerCreditMovements");

            migrationBuilder.DropColumn(
                name: "SupplierCreditNoteId",
                table: "OwnerCreditMovements");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "OwnerCreditMovements");
        }
    }
}
