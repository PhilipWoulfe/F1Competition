using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportReconciliationDiffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportParticipantDeltaSummaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImportedTotalPoints = table.Column<int>(type: "integer", nullable: false),
                    CalculatedTotalPoints = table.Column<int>(type: "integer", nullable: false),
                    NetDeltaPoints = table.Column<int>(type: "integer", nullable: false),
                    TopReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TopReasonCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportParticipantDeltaSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportParticipantDeltaSummaries_MigrationImportRun~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportPickDiffs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaceCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PickType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImportedPoints = table.Column<int>(type: "integer", nullable: true),
                    CalculatedPoints = table.Column<int>(type: "integer", nullable: true),
                    DeltaPoints = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportPickDiffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPickDiffs_MigrationImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportRaceDiffs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaceCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImportedPoints = table.Column<int>(type: "integer", nullable: false),
                    CalculatedPoints = table.Column<int>(type: "integer", nullable: false),
                    DeltaPoints = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportRaceDiffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportRaceDiffs_MigrationImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportReasonCategorySummaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDeltaPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportReasonCategorySummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportReasonCategorySummaries_MigrationImportRuns_~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportParticipantDeltaSummaries_ImportRunId_Subject",
                table: "MigrationImportParticipantDeltaSummaries",
                columns: new[] { "ImportRunId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPickDiffs_ImportRunId_RaceCode_Subject_PickT~",
                table: "MigrationImportPickDiffs",
                columns: new[] { "ImportRunId", "RaceCode", "Subject", "PickType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRaceDiffs_ImportRunId_RaceCode_Subject",
                table: "MigrationImportRaceDiffs",
                columns: new[] { "ImportRunId", "RaceCode", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportReasonCategorySummaries_ImportRunId_ReasonCo~",
                table: "MigrationImportReasonCategorySummaries",
                columns: new[] { "ImportRunId", "ReasonCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportParticipantDeltaSummaries");

            migrationBuilder.DropTable(
                name: "MigrationImportPickDiffs");

            migrationBuilder.DropTable(
                name: "MigrationImportRaceDiffs");

            migrationBuilder.DropTable(
                name: "MigrationImportReasonCategorySummaries");
        }
    }
}
