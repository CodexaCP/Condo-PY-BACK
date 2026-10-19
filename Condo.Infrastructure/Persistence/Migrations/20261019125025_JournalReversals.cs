using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_BuildingId_SourceType_SourceId_Date_SourceAccountId_Revision",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_ReversesEntryId",
                table: "JournalEntries");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_BuildingId_SourceType_SourceId_OperationDate_SourceAccountId_Revision",
                table: "JournalEntries",
                columns: new[] { "BuildingId", "SourceType", "SourceId", "OperationDate", "SourceAccountId", "Revision" },
                unique: true,
                filter: "[SourceType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversesEntryId",
                table: "JournalEntries",
                column: "ReversesEntryId",
                unique: true,
                filter: "[ReversesEntryId] IS NOT NULL");

            // Bloqueo a nivel de base de datos: el Libro Diario no se modifica ni se borra (Ley 1034/83, art. 79).
            migrationBuilder.Sql(JournalImmutability.CreateSql("JournalEntries", JournalImmutability.EntriesTrigger));
            migrationBuilder.Sql(JournalImmutability.CreateSql("JournalEntryLines", JournalImmutability.LinesTrigger));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(JournalImmutability.DropSql(JournalImmutability.LinesTrigger));
            migrationBuilder.Sql(JournalImmutability.DropSql(JournalImmutability.EntriesTrigger));

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_BuildingId_SourceType_SourceId_OperationDate_SourceAccountId_Revision",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_ReversesEntryId",
                table: "JournalEntries");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_BuildingId_SourceType_SourceId_Date_SourceAccountId_Revision",
                table: "JournalEntries",
                columns: new[] { "BuildingId", "SourceType", "SourceId", "Date", "SourceAccountId", "Revision" },
                unique: true,
                filter: "[SourceType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversesEntryId",
                table: "JournalEntries",
                column: "ReversesEntryId");
        }
    }
}
