using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoNote.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelConfigId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationSettings_ModelConfigs_ModelConfigId",
                        column: x => x.ModelConfigId,
                        principalTable: "ModelConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "ConversationSettings",
                columns: new[] { "Id", "ModelConfigId" },
                values: new object[] { 1, null });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSettings_ModelConfigId",
                table: "ConversationSettings",
                column: "ModelConfigId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationSettings");
        }
    }
}
