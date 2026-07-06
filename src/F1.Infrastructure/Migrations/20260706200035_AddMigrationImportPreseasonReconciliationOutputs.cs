using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportPreseasonReconciliationOutputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonParticipantDeltaSummaries",
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
                    table.PrimaryKey("PK_MigrationImportPreseasonParticipantDeltaSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonParticipantDeltaSummaries_Migration~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonQuestionDiffs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    QuestionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImportedPoints = table.Column<int>(type: "integer", nullable: true),
                    CalculatedPoints = table.Column<int>(type: "integer", nullable: true),
                    DeltaPoints = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportPreseasonQuestionDiffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonQuestionDiffs_MigrationImportRuns_I~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonReasonCategorySummaries",
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
                    table.PrimaryKey("PK_MigrationImportPreseasonReasonCategorySummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonReasonCategorySummaries_MigrationIm~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonParticipantDeltaSummaries_ImportRun~",
                table: "MigrationImportPreseasonParticipantDeltaSummaries",
                columns: new[] { "ImportRunId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonQuestionDiffs_ImportRunId_RowNumber~",
                table: "MigrationImportPreseasonQuestionDiffs",
                columns: new[] { "ImportRunId", "RowNumber", "QuestionKey", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonReasonCategorySummaries_ImportRunId~",
                table: "MigrationImportPreseasonReasonCategorySummaries",
                columns: new[] { "ImportRunId", "ReasonCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonParticipantDeltaSummaries");

            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonQuestionDiffs");

            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonReasonCategorySummaries");
        }
    }
}
