using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyQuestionAnswerActualAndScoreFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionAnswers_SourceRow_SourceColumn",
                table: "QuestionAnswers");

            migrationBuilder.DropIndex(
                name: "IX_QuestionActuals_SourceRow_SourceColumn",
                table: "QuestionActuals");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "QuestionScores");

            migrationBuilder.DropColumn(
                name: "NormalizedAnswerBoolean",
                table: "QuestionAnswers");

            migrationBuilder.DropColumn(
                name: "SourceColumn",
                table: "QuestionAnswers");

            migrationBuilder.DropColumn(
                name: "SourceRow",
                table: "QuestionAnswers");

            migrationBuilder.DropColumn(
                name: "NormalizationDiagnosticsJson",
                table: "QuestionActuals");

            migrationBuilder.DropColumn(
                name: "NormalizedAnswerBoolean",
                table: "QuestionActuals");

            migrationBuilder.DropColumn(
                name: "SourceColumn",
                table: "QuestionActuals");

            migrationBuilder.DropColumn(
                name: "SourceRow",
                table: "QuestionActuals");

            migrationBuilder.RenameColumn(
                name: "NormalizedAnswer",
                table: "QuestionAnswers",
                newName: "OverrideAnswer");

            migrationBuilder.RenameColumn(
                name: "NormalizedAnswer",
                table: "QuestionActuals",
                newName: "OverrideAnswer");

            migrationBuilder.RenameColumn(
                name: "ActualAnswer",
                table: "QuestionActuals",
                newName: "ImportedAnswer");

            migrationBuilder.Sql(
                @"
UPDATE ""QuestionAnswers""
SET ""ImportedAnswer"" = CASE lower(trim(""ImportedAnswer""))
    WHEN 'alpine' THEN 'alpine'
    WHEN 'alpine f1 team' THEN 'alpine'
    WHEN 'amr' THEN 'aston_martin'
    WHEN 'aston martin' THEN 'aston_martin'
    WHEN 'aston martin f1 team' THEN 'aston_martin'
    WHEN 'fer' THEN 'ferrari'
    WHEN 'ferrari' THEN 'ferrari'
    WHEN 'haas' THEN 'haas'
    WHEN 'haas f1 team' THEN 'haas'
    WHEN 'mcl' THEN 'mclaren'
    WHEN 'mclaren' THEN 'mclaren'
    WHEN 'mercedes' THEN 'mercedes'
    WHEN 'rb' THEN 'rb'
    WHEN 'rbpt' THEN 'red_bull'
    WHEN 'rb f1 team' THEN 'rb'
    WHEN 'racing bulls' THEN 'rb'
    WHEN 'red bull' THEN 'red_bull'
    WHEN 'red bull racing' THEN 'red_bull'
    WHEN 'sauber' THEN 'sauber'
    WHEN 'williams' THEN 'williams'
    ELSE ""ImportedAnswer""
END;

UPDATE ""QuestionAnswers""
SET ""OverrideAnswer"" = CASE lower(trim(""OverrideAnswer""))
    WHEN 'alpine' THEN 'alpine'
    WHEN 'alpine f1 team' THEN 'alpine'
    WHEN 'amr' THEN 'aston_martin'
    WHEN 'aston martin' THEN 'aston_martin'
    WHEN 'aston martin f1 team' THEN 'aston_martin'
    WHEN 'fer' THEN 'ferrari'
    WHEN 'ferrari' THEN 'ferrari'
    WHEN 'haas' THEN 'haas'
    WHEN 'haas f1 team' THEN 'haas'
    WHEN 'mcl' THEN 'mclaren'
    WHEN 'mclaren' THEN 'mclaren'
    WHEN 'mercedes' THEN 'mercedes'
    WHEN 'rb' THEN 'rb'
    WHEN 'rbpt' THEN 'red_bull'
    WHEN 'rb f1 team' THEN 'rb'
    WHEN 'racing bulls' THEN 'rb'
    WHEN 'red bull' THEN 'red_bull'
    WHEN 'red bull racing' THEN 'red_bull'
    WHEN 'sauber' THEN 'sauber'
    WHEN 'williams' THEN 'williams'
    ELSE ""OverrideAnswer""
END;

UPDATE ""QuestionActuals""
SET ""ImportedAnswer"" = CASE lower(trim(""ImportedAnswer""))
    WHEN 'alpine' THEN 'alpine'
    WHEN 'alpine f1 team' THEN 'alpine'
    WHEN 'amr' THEN 'aston_martin'
    WHEN 'aston martin' THEN 'aston_martin'
    WHEN 'aston martin f1 team' THEN 'aston_martin'
    WHEN 'fer' THEN 'ferrari'
    WHEN 'ferrari' THEN 'ferrari'
    WHEN 'haas' THEN 'haas'
    WHEN 'haas f1 team' THEN 'haas'
    WHEN 'mcl' THEN 'mclaren'
    WHEN 'mclaren' THEN 'mclaren'
    WHEN 'mercedes' THEN 'mercedes'
    WHEN 'rb' THEN 'rb'
    WHEN 'rbpt' THEN 'red_bull'
    WHEN 'rb f1 team' THEN 'rb'
    WHEN 'racing bulls' THEN 'rb'
    WHEN 'red bull' THEN 'red_bull'
    WHEN 'red bull racing' THEN 'red_bull'
    WHEN 'sauber' THEN 'sauber'
    WHEN 'williams' THEN 'williams'
    ELSE ""ImportedAnswer""
END;

UPDATE ""QuestionActuals""
SET ""OverrideAnswer"" = CASE lower(trim(""OverrideAnswer""))
    WHEN 'alpine' THEN 'alpine'
    WHEN 'alpine f1 team' THEN 'alpine'
    WHEN 'amr' THEN 'aston_martin'
    WHEN 'aston martin' THEN 'aston_martin'
    WHEN 'aston martin f1 team' THEN 'aston_martin'
    WHEN 'fer' THEN 'ferrari'
    WHEN 'ferrari' THEN 'ferrari'
    WHEN 'haas' THEN 'haas'
    WHEN 'haas f1 team' THEN 'haas'
    WHEN 'mcl' THEN 'mclaren'
    WHEN 'mclaren' THEN 'mclaren'
    WHEN 'mercedes' THEN 'mercedes'
    WHEN 'rb' THEN 'rb'
    WHEN 'rbpt' THEN 'red_bull'
    WHEN 'rb f1 team' THEN 'rb'
    WHEN 'racing bulls' THEN 'rb'
    WHEN 'red bull' THEN 'red_bull'
    WHEN 'red bull racing' THEN 'red_bull'
    WHEN 'sauber' THEN 'sauber'
    WHEN 'williams' THEN 'williams'
    ELSE ""OverrideAnswer""
END;

UPDATE ""MigrationImportPreseasonAnswers""
SET ""NormalizedAnswer"" = CASE lower(trim(""NormalizedAnswer""))
    WHEN 'alpine' THEN 'alpine'
    WHEN 'alpine f1 team' THEN 'alpine'
    WHEN 'amr' THEN 'aston_martin'
    WHEN 'aston martin' THEN 'aston_martin'
    WHEN 'aston martin f1 team' THEN 'aston_martin'
    WHEN 'fer' THEN 'ferrari'
    WHEN 'ferrari' THEN 'ferrari'
    WHEN 'haas' THEN 'haas'
    WHEN 'haas f1 team' THEN 'haas'
    WHEN 'mcl' THEN 'mclaren'
    WHEN 'mclaren' THEN 'mclaren'
    WHEN 'mercedes' THEN 'mercedes'
    WHEN 'rb' THEN 'rb'
    WHEN 'rbpt' THEN 'red_bull'
    WHEN 'rb f1 team' THEN 'rb'
    WHEN 'racing bulls' THEN 'rb'
    WHEN 'red bull' THEN 'red_bull'
    WHEN 'red bull racing' THEN 'red_bull'
    WHEN 'sauber' THEN 'sauber'
    WHEN 'williams' THEN 'williams'
    ELSE ""NormalizedAnswer""
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OverrideAnswer",
                table: "QuestionAnswers",
                newName: "NormalizedAnswer");

            migrationBuilder.RenameColumn(
                name: "OverrideAnswer",
                table: "QuestionActuals",
                newName: "NormalizedAnswer");

            migrationBuilder.RenameColumn(
                name: "ImportedAnswer",
                table: "QuestionActuals",
                newName: "ActualAnswer");

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "QuestionScores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "NormalizedAnswerBoolean",
                table: "QuestionAnswers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceColumn",
                table: "QuestionAnswers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceRow",
                table: "QuestionAnswers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NormalizationDiagnosticsJson",
                table: "QuestionActuals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NormalizedAnswerBoolean",
                table: "QuestionActuals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceColumn",
                table: "QuestionActuals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceRow",
                table: "QuestionActuals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswers_SourceRow_SourceColumn",
                table: "QuestionAnswers",
                columns: new[] { "SourceRow", "SourceColumn" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionActuals_SourceRow_SourceColumn",
                table: "QuestionActuals",
                columns: new[] { "SourceRow", "SourceColumn" });
        }
    }
}
