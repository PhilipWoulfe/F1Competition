using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalWriteFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyOutcome",
                table: "MigrationImportRuns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyScopeKey",
                table: "MigrationImportRuns",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParityComparedChecksum",
                table: "MigrationImportRuns",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParityComparedRunId",
                table: "MigrationImportRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParitySnapshotChecksum",
                table: "MigrationImportRuns",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParityStatus",
                table: "MigrationImportRuns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MigrationImportConflictDiagnostics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConflictType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyFields = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PolicyOutcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecommendedAction = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportConflictDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportConflictDiagnostics_MigrationImportRuns_Impo~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportRollbackAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AffectedRaceCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedSelectionCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedSelectionPositionCount = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportRollbackAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportRollbackAudits_MigrationImportRuns_ImportRun~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportConflictDiagnostics_ImportRunId_EntityType_K~",
                table: "MigrationImportConflictDiagnostics",
                columns: new[] { "ImportRunId", "EntityType", "KeyFields" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRollbackAudits_ImportRunId_RequestedAtUtc",
                table: "MigrationImportRollbackAudits",
                columns: new[] { "ImportRunId", "RequestedAtUtc" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportConflictDiagnostics");

            migrationBuilder.DropTable(
                name: "MigrationImportRollbackAudits");

            migrationBuilder.DropColumn(
                name: "IdempotencyOutcome",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "IdempotencyScopeKey",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "ParityComparedChecksum",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "ParityComparedRunId",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "ParitySnapshotChecksum",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "ParityStatus",
                table: "MigrationImportRuns");

        }
    }
}
