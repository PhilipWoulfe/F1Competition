using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportLegacyScoresAndTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportCalculatedTotals",
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
                    table.PrimaryKey("PK_MigrationImportCalculatedTotals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportCalculatedTotals_MigrationImportRuns_ImportR~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportImportedTotals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawTotal = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ImportedTotalPoints = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportImportedTotals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportImportedTotals_MigrationImportRuns_ImportRun~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportLegacyPickScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    RaceCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PickType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawLegacyPoints = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LegacyPoints = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportLegacyPickScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportLegacyPickScores_MigrationImportRuns_ImportR~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportCalculatedTotals_ImportRunId_Subject",
                table: "MigrationImportCalculatedTotals",
                columns: new[] { "ImportRunId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportImportedTotals_ImportRunId_Subject",
                table: "MigrationImportImportedTotals",
                columns: new[] { "ImportRunId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportLegacyPickScores_ImportRunId_RaceCode_PickTy~",
                table: "MigrationImportLegacyPickScores",
                columns: new[] { "ImportRunId", "RaceCode", "PickType", "Subject", "RowNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportCalculatedTotals");

            migrationBuilder.DropTable(
                name: "MigrationImportImportedTotals");

            migrationBuilder.DropTable(
                name: "MigrationImportLegacyPickScores");
        }
    }
}
