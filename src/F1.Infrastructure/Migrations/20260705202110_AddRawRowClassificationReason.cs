using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRawRowClassificationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClassificationReason",
                table: "MigrationImportRawRows",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassificationReason",
                table: "MigrationImportRawRows");
        }
    }
}
