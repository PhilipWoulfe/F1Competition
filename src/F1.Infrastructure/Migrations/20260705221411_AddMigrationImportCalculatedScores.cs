using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportCalculatedScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportCalculatedScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    RaceCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PickType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PredictedValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ActualValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportCalculatedScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportCalculatedScores_MigrationImportRuns_ImportR~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportCalculatedScores_ImportRunId_RaceCode_PickTy~",
                table: "MigrationImportCalculatedScores",
                columns: new[] { "ImportRunId", "RaceCode", "PickType", "Subject", "RowNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportCalculatedScores");
        }
    }
}
