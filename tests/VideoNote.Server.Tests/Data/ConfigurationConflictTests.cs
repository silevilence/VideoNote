using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;

namespace VideoNote.Server.Tests.Data;

public sealed class ConfigurationConflictTests
{
    [Fact]
    public async Task Only_unique_and_foreign_key_failures_become_configuration_conflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new VideoNoteDbContext(new DbContextOptionsBuilder<VideoNoteDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var provider = new Provider { Name = "test", BaseUrl = "https://example.com" };
        db.Providers.Add(provider);
        Assert.Null(await db.SaveConfigurationAsync(default));
        db.ChangeTracker.Clear();

        db.Providers.Add(new Provider { Name = "test", BaseUrl = "https://other.example.com" });
        Assert.Equal(ConfigurationConflict.Duplicate, await db.SaveConfigurationAsync(default));
        db.ChangeTracker.Clear();

        db.ModelConfigs.Add(new ModelConfig { ProviderId = Guid.NewGuid(), ModelId = "missing", ContextWindow = 1 });
        Assert.Equal(ConfigurationConflict.MissingReference, await db.SaveConfigurationAsync(default));
        db.ChangeTracker.Clear();

        db.ModelConfigs.Add(new ModelConfig { ProviderId = provider.Id, ModelId = "invalid", ContextWindow = 0 });
        var check = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveConfigurationAsync(default));
        Assert.Equal(275, Assert.IsType<SqliteException>(check.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();

        db.Providers.Add(new Provider { Name = null!, BaseUrl = "https://example.com" });
        var required = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveConfigurationAsync(default));
        Assert.Equal(1299, Assert.IsType<SqliteException>(required.InnerException).SqliteExtendedErrorCode);
    }
}
