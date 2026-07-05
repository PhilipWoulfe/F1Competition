using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportRunAndRawRowStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFilePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceFileChecksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawRowCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportRawRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    SectionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportRawRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportRawRows_MigrationImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRawRows_ImportRunId_RowNumber",
                table: "MigrationImportRawRows",
                columns: new[] { "ImportRunId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRuns_SourceFileChecksum",
                table: "MigrationImportRuns",
                column: "SourceFileChecksum");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRuns_StartedAtUtc",
                table: "MigrationImportRuns",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportRawRows");

            migrationBuilder.DropTable(
                name: "MigrationImportRuns");
        }
    }
}
