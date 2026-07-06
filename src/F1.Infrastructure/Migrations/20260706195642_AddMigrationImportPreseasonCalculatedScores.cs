using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportPreseasonCalculatedScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonCalculatedScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    QuestionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PredictedValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ActualValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportPreseasonCalculatedScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonCalculatedScores_MigrationImportRun~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonCalculatedTotals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CalculatedTotalPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportPreseasonCalculatedTotals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonCalculatedTotals_MigrationImportRun~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonCalculatedScores_ImportRunId_RowNum~",
                table: "MigrationImportPreseasonCalculatedScores",
                columns: new[] { "ImportRunId", "RowNumber", "QuestionKey", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonCalculatedTotals_ImportRunId_Subject",
                table: "MigrationImportPreseasonCalculatedTotals",
                columns: new[] { "ImportRunId", "Subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonCalculatedScores");

            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonCalculatedTotals");
        }
    }
}
