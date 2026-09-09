using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Tests.Api;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Data;

public sealed class DeepSeekDefaultsTests
{
    [Fact]
    public async Task Application_exposes_defaults_without_resolving_the_key_and_respects_deletion()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var provider = Assert.Single((await client.GetFromJsonAsync<ProviderDto[]>("/api/providers"))!);
        Assert.Equal("DeepSeek", provider.Name);
        Assert.Equal("https://api.deepseek.com", provider.BaseUrl);
        Assert.Equal("openai-compatible", provider.Protocol);
        Assert.Equal("DEEPSEEK_API_KEY", provider.ApiKeyEnvironmentVariable);
        Assert.True(provider.HasApiKey);
        var model = Assert.Single((await client.GetFromJsonAsync<ModelDto[]>("/api/models"))!);
        Assert.Equal(provider.Id, model.ProviderId);
        Assert.Equal("deepseek-v4-flash-vision-exp", model.ModelId);
        Assert.True(model.SupportsImage && model.SupportsStreaming);
        Assert.False(model.SupportsAudio || model.SupportsVideo || model.SupportsReasoning || model.SupportsToolCalling);
        Assert.True(model.ContextWindow > 0);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
        Assert.Equal("env:DEEPSEEK_API_KEY", (await db.Providers.AsNoTracking().SingleAsync()).ApiKey);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/models/{model.Id}")).StatusCode);
        await db.Database.MigrateAsync();
        Assert.Empty((await client.GetFromJsonAsync<ModelDto[]>("/api/models"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/providers/{provider.Id}")).StatusCode);
        await db.Database.MigrateAsync();
        Assert.Empty((await client.GetFromJsonAsync<ProviderDto[]>("/api/providers"))!);
    }

    [Theory]
    [InlineData("DeepSeek", "https://custom.example.com", true)]
    [InlineData("deepseek", "https://custom.example.com", true)]
    [InlineData("Existing", "https://api.deepseek.com/", true)]
    [InlineData("Existing", "https://API.DEEPSEEK.COM/v1/", true)]
    [InlineData("Other", "https://other.example.com", false)]
    public async Task Upgrade_preserves_existing_configuration(string name, string url, bool matchesDefault)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new VideoNoteDbContext(new DbContextOptionsBuilder<VideoNoteDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260909034156_AddPromptTemplates");
        var provider = new Provider
        {
            Name = name,
            BaseUrl = url,
            ApiKey = "env:EXISTING_TEST_ONLY",
            Protocol = ProviderProtocol.GeminiNative,
            TranscriptionModel = "keep-transcription"
        };
        provider.Models.Add(new ModelConfig { ModelId = "keep-model", ContextWindow = 123, SupportsAudio = true });
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();
        var saved = await db.Providers.Include(p => p.Models).SingleAsync(p => p.Id == provider.Id);
        Assert.Equal(name, saved.Name);
        Assert.Equal(url, saved.BaseUrl);
        Assert.Equal(ProviderProtocol.GeminiNative, saved.Protocol);
        Assert.Equal("env:EXISTING_TEST_ONLY", saved.ApiKey);
        Assert.Equal("keep-transcription", saved.TranscriptionModel);
        Assert.Equal("keep-model", Assert.Single(saved.Models).ModelId);
        Assert.Equal(matchesDefault ? 1 : 2, await db.Providers.CountAsync());
        saved.Name = "Edited after upgrade";
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        Assert.Equal("Edited after upgrade", (await db.Providers.SingleAsync(p => p.Id == provider.Id)).Name);
        // Rolling the schema back must not erase settings that became user-owned.
        await migrator.MigrateAsync("20260909034156_AddPromptTemplates");
        Assert.Equal(matchesDefault ? 1 : 2, await db.Providers.CountAsync());
    }
}
