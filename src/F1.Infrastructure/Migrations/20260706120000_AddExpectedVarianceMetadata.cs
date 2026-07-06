using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(F1DbContext))]
    [Migration("20260706120000_AddExpectedVarianceMetadata")]
    public partial class AddExpectedVarianceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExpectedVariance",
                table: "MigrationImportPickDiffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedVarianceReasonCode",
                table: "MigrationImportPickDiffs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedVarianceRuleId",
                table: "MigrationImportPickDiffs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExpectedVariance",
                table: "MigrationImportRaceDiffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedVarianceReasonCode",
                table: "MigrationImportRaceDiffs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedVarianceRuleId",
                table: "MigrationImportRaceDiffs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExpectedVariance",
                table: "MigrationImportPickDiffs");

            migrationBuilder.DropColumn(
                name: "ExpectedVarianceReasonCode",
                table: "MigrationImportPickDiffs");

            migrationBuilder.DropColumn(
                name: "ExpectedVarianceRuleId",
                table: "MigrationImportPickDiffs");

            migrationBuilder.DropColumn(
                name: "IsExpectedVariance",
                table: "MigrationImportRaceDiffs");

            migrationBuilder.DropColumn(
                name: "ExpectedVarianceReasonCode",
                table: "MigrationImportRaceDiffs");

            migrationBuilder.DropColumn(
                name: "ExpectedVarianceRuleId",
                table: "MigrationImportRaceDiffs");
        }
    }
}