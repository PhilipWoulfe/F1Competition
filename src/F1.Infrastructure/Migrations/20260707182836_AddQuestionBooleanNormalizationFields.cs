using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionBooleanNormalizationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NormalizedAnswerBoolean",
                table: "QuestionAnswers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NormalizedAnswerBoolean",
                table: "QuestionActuals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NormalizedAnswerBoolean",
                table: "MigrationImportPreseasonAnswers",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizedAnswerBoolean",
                table: "QuestionAnswers");

            migrationBuilder.DropColumn(
                name: "NormalizedAnswerBoolean",
                table: "QuestionActuals");

            migrationBuilder.DropColumn(
                name: "NormalizedAnswerBoolean",
                table: "MigrationImportPreseasonAnswers");
        }
    }
}
