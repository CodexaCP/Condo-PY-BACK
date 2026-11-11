using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VotingIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Votes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Votes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Votes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                table: "Votes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OpenedByUserId",
                table: "Votes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultHash",
                table: "Votes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "Votes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ResultParticipatingCoefficient",
                table: "Votes",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultParticipatingUnits",
                table: "Votes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ResultTotalCoefficient",
                table: "Votes",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultTotalUnits",
                table: "Votes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Votes",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "VoteCasts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Staff");

            migrationBuilder.CreateTable(
                name: "VoteCastEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreviousOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CoefficientWeight = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActingUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoteCastEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoteCastEvents_ApplicationUsers_ActingUserId",
                        column: x => x.ActingUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoteCastEvents_ApplicationUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoteCastEvents_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoteCastEvents_VoteOptions_NewOptionId",
                        column: x => x.NewOptionId,
                        principalTable: "VoteOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoteCastEvents_VoteOptions_PreviousOptionId",
                        column: x => x.PreviousOptionId,
                        principalTable: "VoteOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoteCastEvents_Votes_VoteId",
                        column: x => x.VoteId,
                        principalTable: "Votes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Votes_CancelledByUserId",
                table: "Votes",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_ClosedByUserId",
                table: "Votes",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_OpenedByUserId",
                table: "Votes",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteCastEvents_ActingUserId",
                table: "VoteCastEvents",
                column: "ActingUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteCastEvents_NewOptionId",
                table: "VoteCastEvents",
                column: "NewOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteCastEvents_OwnerUserId",
                table: "VoteCastEvents",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteCastEvents_PreviousOptionId",
                table: "VoteCastEvents",
                column: "PreviousOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteCastEvents_UnitId",
                table: "VoteCastEvents",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteCastEvents_VoteId_UnitId_OccurredAtUtc",
                table: "VoteCastEvents",
                columns: new[] { "VoteId", "UnitId", "OccurredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Votes_ApplicationUsers_CancelledByUserId",
                table: "Votes",
                column: "CancelledByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Votes_ApplicationUsers_ClosedByUserId",
                table: "Votes",
                column: "ClosedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Votes_ApplicationUsers_OpenedByUserId",
                table: "Votes",
                column: "OpenedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Votes_ApplicationUsers_CancelledByUserId",
                table: "Votes");

            migrationBuilder.DropForeignKey(
                name: "FK_Votes_ApplicationUsers_ClosedByUserId",
                table: "Votes");

            migrationBuilder.DropForeignKey(
                name: "FK_Votes_ApplicationUsers_OpenedByUserId",
                table: "Votes");

            migrationBuilder.DropTable(
                name: "VoteCastEvents");

            migrationBuilder.DropIndex(
                name: "IX_Votes_CancelledByUserId",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "IX_Votes_ClosedByUserId",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "IX_Votes_OpenedByUserId",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "OpenedByUserId",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ResultHash",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ResultParticipatingCoefficient",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ResultParticipatingUnits",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ResultTotalCoefficient",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ResultTotalUnits",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "VoteCasts");
        }
    }
}
