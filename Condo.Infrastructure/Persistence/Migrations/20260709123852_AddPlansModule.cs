using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlansModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GracePeriodDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsAssigned = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BuildingPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentScope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScopeEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RenewalStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    AssignedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingPlans_ApplicationUsers_AssignedById",
                        column: x => x.AssignedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPlans_ApplicationUsers_PaidById",
                        column: x => x.PaidById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPlans_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPlans_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuildingPlanPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentScope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScopeEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeclaredAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ComprobanteUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SubmittedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingPlanPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingPlanPayments_ApplicationUsers_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPlanPayments_ApplicationUsers_SubmittedById",
                        column: x => x.SubmittedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPlanPayments_BuildingPlans_BuildingPlanId",
                        column: x => x.BuildingPlanId,
                        principalTable: "BuildingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildingPlanPayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "BillingCycle", "CreatedAtUtc", "Description", "GracePeriodDays", "IsActive", "IsAssigned", "IsDefault", "IsDeleted", "Name", "Price", "UpdatedAtUtc" },
                values: new object[] { new Guid("a0000000-0000-0000-0000-000000000001"), "Monthly", new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Plan de prueba gratuito incluido al registrar un nuevo edificio en la plataforma.", 5, true, false, true, false, "Plan Gratuito 45 días", 0m, new DateTime(2026, 6, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlanPayments_BuildingPlanId",
                table: "BuildingPlanPayments",
                column: "BuildingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlanPayments_CompanyId",
                table: "BuildingPlanPayments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlanPayments_ReviewedById",
                table: "BuildingPlanPayments",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlanPayments_ScopeEntityId_Status",
                table: "BuildingPlanPayments",
                columns: new[] { "ScopeEntityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlanPayments_SubmittedById",
                table: "BuildingPlanPayments",
                column: "SubmittedById");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlans_AssignedById",
                table: "BuildingPlans",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlans_BuildingId_IsArchived",
                table: "BuildingPlans",
                columns: new[] { "BuildingId", "IsArchived" },
                filter: "[IsArchived] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlans_PaidById",
                table: "BuildingPlans",
                column: "PaidById");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlans_PlanId",
                table: "BuildingPlans",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingPlans_ScopeEntityId_AssignmentScope",
                table: "BuildingPlans",
                columns: new[] { "ScopeEntityId", "AssignmentScope" });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_IsDefault",
                table: "Plans",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingPlanPayments");

            migrationBuilder.DropTable(
                name: "BuildingPlans");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
