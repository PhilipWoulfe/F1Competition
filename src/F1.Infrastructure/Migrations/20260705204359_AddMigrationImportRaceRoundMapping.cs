using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportRaceRoundMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportJolpicaRaceSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    RaceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CircuitName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StartTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportJolpicaRaceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportJolpicaRaceSnapshots_MigrationImportRuns_Imp~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportRaceRoundMappings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaceSequence = table.Column<int>(type: "integer", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceRaceCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: true),
                    Round = table.Column<int>(type: "integer", nullable: true),
                    MappedRaceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Warning = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportRaceRoundMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportRaceRoundMappings_MigrationImportRuns_Import~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportJolpicaRaceSnapshots_ImportRunId_Season_Round",
                table: "MigrationImportJolpicaRaceSnapshots",
                columns: new[] { "ImportRunId", "Season", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportRaceRoundMappings_ImportRunId_RaceSequence",
                table: "MigrationImportRaceRoundMappings",
                columns: new[] { "ImportRunId", "RaceSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportJolpicaRaceSnapshots");

            migrationBuilder.DropTable(
                name: "MigrationImportRaceRoundMappings");
        }
    }
}
