using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gh296MigrationHalfPointScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TotalDeltaPoints",
                table: "MigrationImportReasonCategorySummaries",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "DeltaPoints",
                table: "MigrationImportRaceDiffs",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "CalculatedPoints",
                table: "MigrationImportRaceDiffs",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "DeltaPoints",
                table: "MigrationImportPickDiffs",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "CalculatedPoints",
                table: "MigrationImportPickDiffs",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "NetDeltaPoints",
                table: "MigrationImportParticipantDeltaSummaries",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "CalculatedTotalPoints",
                table: "MigrationImportParticipantDeltaSummaries",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "CalculatedTotalPoints",
                table: "MigrationImportCalculatedTotals",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "Points",
                table: "MigrationImportCalculatedScores",
                type: "numeric(10,1)",
                precision: 10,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TotalDeltaPoints",
                table: "MigrationImportReasonCategorySummaries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);

            migrationBuilder.AlterColumn<int>(
                name: "DeltaPoints",
                table: "MigrationImportRaceDiffs",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CalculatedPoints",
                table: "MigrationImportRaceDiffs",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);

            migrationBuilder.AlterColumn<int>(
                name: "DeltaPoints",
                table: "MigrationImportPickDiffs",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CalculatedPoints",
                table: "MigrationImportPickDiffs",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "NetDeltaPoints",
                table: "MigrationImportParticipantDeltaSummaries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CalculatedTotalPoints",
                table: "MigrationImportParticipantDeltaSummaries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CalculatedTotalPoints",
                table: "MigrationImportCalculatedTotals",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);

            migrationBuilder.AlterColumn<int>(
                name: "Points",
                table: "MigrationImportCalculatedScores",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,1)",
                oldPrecision: 10,
                oldScale: 1);
        }
    }
}
