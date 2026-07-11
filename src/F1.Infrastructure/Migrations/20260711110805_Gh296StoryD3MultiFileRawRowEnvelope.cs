using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gh296StoryD3MultiFileRawRowEnvelope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MigrationImportRawRows_ImportRunId_RowNumber",
                table: "MigrationImportRawRows");

            migrationBuilder.AddColumn<string>(
                name: "SourceFileName",
                table: "MigrationImportRawRows",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRawRows_ImportRunId_SourceFileName_RowNumber",
                table: "MigrationImportRawRows",
                columns: new[] { "ImportRunId", "SourceFileName", "RowNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MigrationImportRawRows_ImportRunId_SourceFileName_RowNumber",
                table: "MigrationImportRawRows");

            migrationBuilder.DropColumn(
                name: "SourceFileName",
                table: "MigrationImportRawRows");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRawRows_ImportRunId_RowNumber",
                table: "MigrationImportRawRows",
                columns: new[] { "ImportRunId", "RowNumber" },
                unique: true);
        }
    }
}
