using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoNote.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogsJson",
                table: "AnalysisTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogsJson",
                table: "AnalysisTasks");
        }
    }
}
