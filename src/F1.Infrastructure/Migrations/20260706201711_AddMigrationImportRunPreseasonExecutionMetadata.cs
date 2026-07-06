using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportRunPreseasonExecutionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MappingWarningCount",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreseasonAnswerCount",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreseasonErrorCount",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PreseasonIsolationGuardPassed",
                table: "MigrationImportRuns",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PreseasonParseStatus",
                table: "MigrationImportRuns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDetected");

            migrationBuilder.AddColumn<int>(
                name: "PreseasonQuestionDiffCount",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreseasonScoredQuestionCount",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PreseasonScoringStatus",
                table: "MigrationImportRuns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDetected");

            migrationBuilder.AddColumn<int>(
                name: "PreseasonTotalDeltaPoints",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreseasonWarningCount",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnresolvedTokenCount",
                table: "MigrationImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MappingWarningCount",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonAnswerCount",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonErrorCount",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonIsolationGuardPassed",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonParseStatus",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonQuestionDiffCount",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonScoredQuestionCount",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonScoringStatus",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonTotalDeltaPoints",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "PreseasonWarningCount",
                table: "MigrationImportRuns");

            migrationBuilder.DropColumn(
                name: "UnresolvedTokenCount",
                table: "MigrationImportRuns");
        }
    }
}
