using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class gh283_story89_question_reconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompetitionId = table.Column<int>(type: "integer", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionTemplates_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionActuals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ActualAnswer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    NormalizedAnswer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SourceRow = table.Column<int>(type: "integer", nullable: false),
                    SourceColumn = table.Column<int>(type: "integer", nullable: false),
                    NormalizationDiagnosticsJson = table.Column<string>(type: "text", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionActuals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionActuals_MigrationImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionActuals_QuestionTemplates_QuestionTemplateId",
                        column: x => x.QuestionTemplateId,
                        principalTable: "QuestionTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionAnswers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImportedAnswer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    NormalizedAnswer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SourceRow = table.Column<int>(type: "integer", nullable: false),
                    SourceColumn = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionAnswers_MigrationImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionAnswers_QuestionTemplates_QuestionTemplateId",
                        column: x => x.QuestionTemplateId,
                        principalTable: "QuestionTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImportedPoints = table.Column<int>(type: "integer", nullable: true),
                    CalculatedPoints = table.Column<int>(type: "integer", nullable: false),
                    DeltaPoints = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionScores_MigrationImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionScores_QuestionTemplates_QuestionTemplateId",
                        column: x => x.QuestionTemplateId,
                        principalTable: "QuestionTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionActuals_ImportRunId_QuestionTemplateId",
                table: "QuestionActuals",
                columns: new[] { "ImportRunId", "QuestionTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionActuals_ImportRunId_SourceRow_SourceColumn",
                table: "QuestionActuals",
                columns: new[] { "ImportRunId", "SourceRow", "SourceColumn" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionActuals_QuestionTemplateId",
                table: "QuestionActuals",
                column: "QuestionTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswers_ImportRunId_QuestionTemplateId_ParticipantId",
                table: "QuestionAnswers",
                columns: new[] { "ImportRunId", "QuestionTemplateId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswers_ImportRunId_SourceRow_SourceColumn",
                table: "QuestionAnswers",
                columns: new[] { "ImportRunId", "SourceRow", "SourceColumn" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswers_QuestionTemplateId",
                table: "QuestionAnswers",
                column: "QuestionTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionScores_ImportRunId_DeltaPoints",
                table: "QuestionScores",
                columns: new[] { "ImportRunId", "DeltaPoints" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionScores_ImportRunId_QuestionTemplateId_ParticipantId",
                table: "QuestionScores",
                columns: new[] { "ImportRunId", "QuestionTemplateId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionScores_QuestionTemplateId",
                table: "QuestionScores",
                column: "QuestionTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionTemplates_CompetitionId_Season_Category_SortOrder",
                table: "QuestionTemplates",
                columns: new[] { "CompetitionId", "Season", "Category", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionTemplates_CompetitionId_Season_QuestionId",
                table: "QuestionTemplates",
                columns: new[] { "CompetitionId", "Season", "QuestionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionActuals");

            migrationBuilder.DropTable(
                name: "QuestionAnswers");

            migrationBuilder.DropTable(
                name: "QuestionScores");

            migrationBuilder.DropTable(
                name: "QuestionTemplates");
        }
    }
}
