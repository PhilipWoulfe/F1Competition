using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gh286QuestionScoreOverrideFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OverrideReasonCode",
                table: "QuestionScores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverrideScore",
                table: "QuestionScores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideSourceRunId",
                table: "QuestionScores",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionScores_OverrideSourceRunId",
                table: "QuestionScores",
                column: "OverrideSourceRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionScores_OverrideSourceRunId",
                table: "QuestionScores");

            migrationBuilder.DropColumn(
                name: "OverrideReasonCode",
                table: "QuestionScores");

            migrationBuilder.DropColumn(
                name: "OverrideScore",
                table: "QuestionScores");

            migrationBuilder.DropColumn(
                name: "OverrideSourceRunId",
                table: "QuestionScores");
        }
    }
}
