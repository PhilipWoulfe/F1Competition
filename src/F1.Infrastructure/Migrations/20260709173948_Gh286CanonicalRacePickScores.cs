using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gh286CanonicalRacePickScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RacePickScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RaceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RaceCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PickType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PredictedValue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActualValue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ImportedPoints = table.Column<int>(type: "integer", nullable: true),
                    CalculatedPoints = table.Column<int>(type: "integer", nullable: false),
                    OverrideScore = table.Column<int>(type: "integer", nullable: true),
                    OverrideReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeltaPoints = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RacePickScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RacePickScores_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RacePickScores_RaceId_ParticipantId",
                table: "RacePickScores",
                columns: new[] { "RaceId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_RacePickScores_RaceId_PickType_ParticipantId",
                table: "RacePickScores",
                columns: new[] { "RaceId", "PickType", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RacePickScores_SourceRunId",
                table: "RacePickScores",
                column: "SourceRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RacePickScores");
        }
    }
}
