using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoNote.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentUnderstanding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SegmentResultsJson",
                table: "AnalysisTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SegmentResultsJson",
                table: "AnalysisTasks");
        }
    }
}
