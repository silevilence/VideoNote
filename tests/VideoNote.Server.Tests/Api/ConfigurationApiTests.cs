using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Contracts;

namespace VideoNote.Server.Tests.Api;

public sealed class ConfigurationApiTests
{
    [Fact]
    public async Task Crud_protects_secrets_preserves_flags_and_history()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        Assert.Contains("work-tests", app.Services.GetRequiredService<VideoNote.Server.Storage.WorkDirectoryPaths>().Root);
        var input = new ProviderInput { Name = "Test", BaseUrl = "https://example.com/v1", ApiKey = "test-only-secret" };
        var response = await client.PostAsJsonAsync("/api/providers", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var p = (await response.Content.ReadFromJsonAsync<ProviderDto>())!;
        Assert.True(p.HasApiKey);
        Assert.DoesNotContain("test-only-secret", await client.GetStringAsync("/api/providers"));
        Assert.DoesNotContain("test-only-secret", await client.GetStringAsync($"/api/providers/{p.Id}"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/providers", input)).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var stored = (await scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>().Providers.FindAsync(p.Id))!;
            Assert.StartsWith("protected:", stored.ApiKey);
            Assert.DoesNotContain("test-only-secret", stored.ApiKey);
        }
        input.ApiKey = null;
        input.Name = "Updated";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/providers/{p.Id}", input)).StatusCode);
        Assert.True((await client.GetFromJsonAsync<ProviderDto>($"/api/providers/{p.Id}"))!.HasApiKey);
        input.ApiKeyEnvironmentVariable = "VIDEONOTE_TEST_KEY";
        await client.PutAsJsonAsync($"/api/providers/{p.Id}", input);
        Assert.Equal("VIDEONOTE_TEST_KEY", (await client.GetFromJsonAsync<ProviderDto>($"/api/providers/{p.Id}"))!.ApiKeyEnvironmentVariable);
        input.ApiKeyEnvironmentVariable = null; input.ClearApiKey = true;
        await client.PutAsJsonAsync($"/api/providers/{p.Id}", input);
        Assert.False((await client.GetFromJsonAsync<ProviderDto>($"/api/providers/{p.Id}"))!.HasApiKey);

        var model = new ModelInput
        {
            ProviderId = p.Id,
            ModelId = "vision",
            ContextWindow = 1000,
            SupportsReasoning = true,
            SupportsToolCalling = true,
            SupportsStreaming = true,
            SupportsImage = true,
            SupportsAudio = true,
            SupportsVideo = true
        };
        response = await client.PostAsJsonAsync("/api/models", model);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var m = (await response.Content.ReadFromJsonAsync<ModelDto>())!;
        Assert.True(m.SupportsReasoning && m.SupportsToolCalling && m.SupportsStreaming && m.SupportsImage && m.SupportsAudio && m.SupportsVideo);
        Assert.Single((await client.GetFromJsonAsync<ModelDto[]>($"/api/models?providerId={p.Id}"))!);
        Assert.Empty((await client.GetFromJsonAsync<ModelDto[]>($"/api/models?providerId={Guid.NewGuid()}"))!);
        model.SupportsVideo = false; model.ContextWindow = 2000;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/models/{m.Id}", model)).StatusCode);
        Assert.False((await client.GetFromJsonAsync<ModelDto>($"/api/models/{m.Id}"))!.SupportsVideo);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            db.AnalysisTasks.Add(new AnalysisTask { ModelConfigId = m.Id, OriginalFileName = "history.mp4", VideoPath = "history.mp4" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/providers/{p.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/models/{m.Id}")).StatusCode);
        using (var scope = app.Services.CreateScope())
            Assert.Null(scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>().AnalysisTasks.Single().ModelConfigId);
    }

    [Fact]
    public async Task Invalid_configuration_is_rejected()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        Assert.Contains("work-tests", app.Services.GetRequiredService<VideoNote.Server.Storage.WorkDirectoryPaths>().Root);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/providers",
            new ProviderInput { Name = " ", BaseUrl = "file:///test", Protocol = "unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/models",
            new ModelInput { ModelId = "model", ContextWindow = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/models/{Guid.NewGuid()}")).StatusCode);
    }
}
