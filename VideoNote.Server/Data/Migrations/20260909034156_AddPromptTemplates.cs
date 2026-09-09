using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VideoNote.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromptContentSnapshot",
                table: "AnalysisTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.InsertData(
                table: "PromptTemplates",
                columns: new[] { "Id", "Content", "CreatedAtUtc", "IsBuiltIn", "Name", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "根据视频内容生成层次清晰的大纲。使用 Markdown 标题和列表，按主题或时间顺序组织。仅在物料提供时间信息时标注时间戳；不编造视频未提供的信息。", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), true, "大纲提取", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "提取视频的关键观点、结论和支持依据。使用 Markdown 列表，区分事实、观点与不确定内容；仅在物料提供时标注相关时间戳。", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), true, "重点提取", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "用中文概述视频主题、主要内容和结论。输出简洁的 Markdown 摘要，保留必要背景和限制，不添加视频未支持的信息。", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), true, "摘要", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "从视频提取核心关键词、术语及简短解释。使用 Markdown 表格，按重要程度排序，解释应以视频内容为依据。", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), true, "关键词", new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PromptTemplates",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "PromptTemplates",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "PromptTemplates",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "PromptTemplates",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"));

            migrationBuilder.DropColumn(
                name: "PromptContentSnapshot",
                table: "AnalysisTasks");
        }
    }
}
