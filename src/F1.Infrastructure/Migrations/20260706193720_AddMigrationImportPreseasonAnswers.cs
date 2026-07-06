using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportPreseasonAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonAnswers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    QuestionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawAnswer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    NormalizedAnswer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActualOutcome = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportPreseasonAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonAnswers_MigrationImportRuns_ImportR~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonAnswers_ImportRunId_RowNumber_Quest~",
                table: "MigrationImportPreseasonAnswers",
                columns: new[] { "ImportRunId", "RowNumber", "QuestionKey", "Subject", "IsActualOutcome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonAnswers");
        }
    }
}
