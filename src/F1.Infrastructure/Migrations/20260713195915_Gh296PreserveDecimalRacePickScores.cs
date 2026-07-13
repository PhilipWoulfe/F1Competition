using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gh296PreserveDecimalRacePickScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "OverrideScore",
                table: "RacePickScores",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DeltaPoints",
                table: "RacePickScores",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "CalculatedPoints",
                table: "RacePickScores",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "OverrideScore",
                table: "RacePickScores",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DeltaPoints",
                table: "RacePickScores",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<int>(
                name: "CalculatedPoints",
                table: "RacePickScores",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");
        }
    }
}
