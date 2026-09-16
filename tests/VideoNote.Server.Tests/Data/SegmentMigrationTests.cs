using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VideoNote.Server.Data;
namespace VideoNote.Server.Tests.Data;

public sealed class SegmentMigrationTests
{
    [Fact]
    public async Task Existing_tasks_receive_valid_empty_segment_json_on_upgrade()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new VideoNoteDbContext(new DbContextOptionsBuilder<VideoNoteDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260909071306_SeedDeepSeekDefaults");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AnalysisTasks (Id, OriginalFileName, VideoPath, Mode, Status, ProgressPercent, CreatedAtUtc)
            VALUES ('11111111-1111-1111-1111-111111111111', 'old.mp4', 'old.mp4', 'Subtitles', 'Completed', 100, '2026-09-09 00:00:00')
            """);
        await migrator.MigrateAsync();
        Assert.Equal("[]", (await db.AnalysisTasks.SingleAsync()).SegmentResultsJson);
        Assert.Equal("[]", (await db.AnalysisTasks.SingleAsync()).LogsJson);
    }
}
