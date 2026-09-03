using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoNote.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    TranscriptionModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContextWindow = table.Column<int>(type: "INTEGER", nullable: false),
                    SupportsReasoning = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsToolCalling = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsStreaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsImage = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsAudio = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsVideo = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelConfigs", x => x.Id);
                    table.CheckConstraint("CK_ModelConfigs_ContextWindow_Positive", "ContextWindow > 0");
                    table.ForeignKey(
                        name: "FK_ModelConfigs_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    VideoPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    StageDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ResultText = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ModelConfigId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PromptTemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisTasks", x => x.Id);
                    table.CheckConstraint("CK_AnalysisTasks_ProgressPercent_Range", "ProgressPercent >= 0 AND ProgressPercent <= 100");
                    table.ForeignKey(
                        name: "FK_AnalysisTasks_ModelConfigs_ModelConfigId",
                        column: x => x.ModelConfigId,
                        principalTable: "ModelConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AnalysisTasks_PromptTemplates_PromptTemplateId",
                        column: x => x.PromptTemplateId,
                        principalTable: "PromptTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnalysisTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationMessages_AnalysisTasks_AnalysisTaskId",
                        column: x => x.AnalysisTaskId,
                        principalTable: "AnalysisTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisTasks_ModelConfigId",
                table: "AnalysisTasks",
                column: "ModelConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisTasks_PromptTemplateId",
                table: "AnalysisTasks",
                column: "PromptTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_AnalysisTaskId",
                table: "ConversationMessages",
                column: "AnalysisTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelConfigs_ProviderId_ModelId",
                table: "ModelConfigs",
                columns: new[] { "ProviderId", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_Name",
                table: "Providers",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "AnalysisTasks");

            migrationBuilder.DropTable(
                name: "ModelConfigs");

            migrationBuilder.DropTable(
                name: "PromptTemplates");

            migrationBuilder.DropTable(
                name: "Providers");
        }
    }
}
