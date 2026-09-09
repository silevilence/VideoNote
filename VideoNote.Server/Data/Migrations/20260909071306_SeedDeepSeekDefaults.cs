using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoNote.Server.Data.Migrations;

/// <inheritdoc />
public partial class SeedDeepSeekDefaults : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A one-time default, not HasData: subsequent startup must respect edits and deletions.
        // Preserve existing user configuration when its name or endpoint identifies DeepSeek.
        migrationBuilder.Sql("""
            INSERT INTO "Providers" ("Id", "Name", "Protocol", "BaseUrl", "ApiKey", "TranscriptionModel", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT '20000000-0000-0000-0000-000000000001', 'DeepSeek', 'OpenAiCompatible',
                'https://api.deepseek.com', 'env:DEEPSEEK_API_KEY', NULL, '2026-09-09 00:00:00', '2026-09-09 00:00:00'
            WHERE NOT EXISTS (
                SELECT 1 FROM "Providers"
                WHERE lower("Name") = 'deepseek'
                    OR lower(rtrim("BaseUrl", '/')) IN ('https://api.deepseek.com', 'https://api.deepseek.com/v1')
            );

            INSERT INTO "ModelConfigs" ("Id", "ProviderId", "ModelId", "ContextWindow",
                "SupportsReasoning", "SupportsToolCalling", "SupportsStreaming", "SupportsImage", "SupportsAudio", "SupportsVideo")
            SELECT '20000000-0000-0000-0000-000000000002', "Id", 'deepseek-v4-flash-vision-exp', 128000, 0, 0, 1, 1, 0, 0
            FROM "Providers"
            WHERE "Id" = '20000000-0000-0000-0000-000000000001'
                AND NOT EXISTS (SELECT 1 FROM "ModelConfigs" WHERE "ProviderId" = '20000000-0000-0000-0000-000000000001');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These are editable user settings once inserted. A schema rollback must not delete them.
    }
}
