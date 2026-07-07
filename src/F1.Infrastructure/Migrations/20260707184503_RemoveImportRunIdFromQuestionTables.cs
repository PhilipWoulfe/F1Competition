using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveImportRunIdFromQuestionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestionActuals_MigrationImportRuns_ImportRunId",
                table: "QuestionActuals");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionAnswers_MigrationImportRuns_ImportRunId",
                table: "QuestionAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionScores_MigrationImportRuns_ImportRunId",
                table: "QuestionScores");

            migrationBuilder.DropIndex(
                name: "IX_QuestionScores_ImportRunId_DeltaPoints",
                table: "QuestionScores");

            migrationBuilder.DropIndex(
                name: "IX_QuestionScores_ImportRunId_QuestionTemplateId_ParticipantId",
                table: "QuestionScores");

            migrationBuilder.DropIndex(
                name: "IX_QuestionScores_QuestionTemplateId",
                table: "QuestionScores");

            migrationBuilder.DropIndex(
                name: "IX_QuestionAnswers_ImportRunId_QuestionTemplateId_ParticipantId",
                table: "QuestionAnswers");

            migrationBuilder.DropIndex(
                name: "IX_QuestionAnswers_ImportRunId_SourceRow_SourceColumn",
                table: "QuestionAnswers");

            migrationBuilder.DropIndex(
                name: "IX_QuestionAnswers_QuestionTemplateId",
                table: "QuestionAnswers");

            migrationBuilder.DropIndex(
                name: "IX_QuestionActuals_ImportRunId_QuestionTemplateId",
                table: "QuestionActuals");

            migrationBuilder.DropIndex(
                name: "IX_QuestionActuals_ImportRunId_SourceRow_SourceColumn",
                table: "QuestionActuals");

            migrationBuilder.DropIndex(
                name: "IX_QuestionActuals_QuestionTemplateId",
                table: "QuestionActuals");

            migrationBuilder.DropColumn(
                name: "ImportRunId",
                table: "QuestionScores");

            migrationBuilder.DropColumn(
                name: "ImportRunId",
                table: "QuestionAnswers");

            migrationBuilder.DropColumn(
                name: "ImportRunId",
                table: "QuestionActuals");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionScores_DeltaPoints",
                table: "QuestionScores",
                column: "DeltaPoints");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionScores_QuestionTemplateId_ParticipantId",
                table: "QuestionScores",
                columns: new[] { "QuestionTemplateId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswers_QuestionTemplateId_ParticipantId",
                table: "QuestionAnswers",
                columns: new[] { "QuestionTemplateId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswers_SourceRow_SourceColumn",
                table: "QuestionAnswers",
                columns: new[] { "SourceRow", "SourceColumn" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionActuals_QuestionTemplateId",
                table: "QuestionActuals",
                column: "QuestionTemplateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionActuals_SourceRow_SourceColumn",
                table: "QuestionActuals",
                columns: new[] { "SourceRow", "SourceColumn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionScores_DeltaPoints",
                table: "QuestionScores");

            migrationBuilder.DropIndex(
                name: "IX_QuestionScores_QuestionTemplateId_ParticipantId",
                table: "QuestionScores");

            migrationBuilder.DropIndex(
                name: "IX_QuestionAnswers_QuestionTemplateId_ParticipantId",
                table: "QuestionAnswers");

            migrationBuilder.DropIndex(
                name: "IX_QuestionAnswers_SourceRow_SourceColumn",
                table: "QuestionAnswers");

            migrationBuilder.DropIndex(
                name: "IX_QuestionActuals_QuestionTemplateId",
                table: "QuestionActuals");

            migrationBuilder.DropIndex(
                name: "IX_QuestionActuals_SourceRow_SourceColumn",
                table: "QuestionActuals");

            migrationBuilder.AddColumn<Guid>(
                name: "ImportRunId",
                table: "QuestionScores",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ImportRunId",
                table: "QuestionAnswers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ImportRunId",
                table: "QuestionActuals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

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

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionActuals_MigrationImportRuns_ImportRunId",
                table: "QuestionActuals",
                column: "ImportRunId",
                principalTable: "MigrationImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionAnswers_MigrationImportRuns_ImportRunId",
                table: "QuestionAnswers",
                column: "ImportRunId",
                principalTable: "MigrationImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionScores_MigrationImportRuns_ImportRunId",
                table: "QuestionScores",
                column: "ImportRunId",
                principalTable: "MigrationImportRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
