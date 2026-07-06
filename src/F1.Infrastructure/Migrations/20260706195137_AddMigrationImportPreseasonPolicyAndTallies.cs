using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationImportPreseasonPolicyAndTallies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonImportedTallies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    QuestionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawPoints = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ImportedPoints = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportPreseasonImportedTallies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonImportedTallies_MigrationImportRuns~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationImportPreseasonPolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    ColumnIndex = table.Column<int>(type: "integer", nullable: false),
                    CellReference = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RawPointsPerQuestion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PointsPerQuestion = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationImportPreseasonPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationImportPreseasonPolicies_MigrationImportRuns_Import~",
                        column: x => x.ImportRunId,
                        principalTable: "MigrationImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonImportedTallies_ImportRunId_RowNumb~",
                table: "MigrationImportPreseasonImportedTallies",
                columns: new[] { "ImportRunId", "RowNumber", "QuestionKey", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationImportPreseasonPolicies_ImportRunId",
                table: "MigrationImportPreseasonPolicies",
                column: "ImportRunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonImportedTallies");

            migrationBuilder.DropTable(
                name: "MigrationImportPreseasonPolicies");
        }
    }
}
