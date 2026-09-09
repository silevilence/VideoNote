using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Api;

public sealed class ConfigurationEdgeTests
{
    [Theory]
    [InlineData("ftp://example.com", "openai-compatible", null, null, false)]
    [InlineData("https://example.com?key=test", "openai-compatible", null, null, false)]
    [InlineData("https://user:pass@example.com", "openai-compatible", null, null, false)]
    [InlineData("https://example.com", "invalid", null, null, false)]
    [InlineData("https://example.com", "openai-compatible", "fake", "TEST_ONLY", false)]
    [InlineData("https://example.com", "openai-compatible", "fake", null, true)]
    public async Task Ambiguous_credentials_and_unsafe_urls_are_rejected(string url, string protocol, string? key, string? variable, bool clear)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/providers", new ProviderInput
        {
            Name = "validation",
            BaseUrl = url,
            Protocol = protocol,
            ApiKey = key,
            ApiKeyEnvironmentVariable = variable,
            ClearApiKey = clear
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_models_missing_resources_and_direct_model_deletion_are_handled()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var p = (await (await client.PostAsJsonAsync("/api/providers",
            new ProviderInput { Name = "one", Protocol = "gemini-native", BaseUrl = "https://example.com" }))
            .Content.ReadFromJsonAsync<ProviderDto>())!;
        var second = (await (await client.PostAsJsonAsync("/api/providers",
            new ProviderInput { Name = "two", BaseUrl = "https://example.com" }))
            .Content.ReadFromJsonAsync<ProviderDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/providers/{second.Id}",
            new ProviderInput { Name = "one", BaseUrl = "https://example.com" })).StatusCode);
        var input = new ModelInput { ProviderId = p.Id, ModelId = "one", ContextWindow = 1024 };
        var model = (await (await client.PostAsJsonAsync("/api/models", input)).Content.ReadFromJsonAsync<ModelDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/models", input)).StatusCode);
        input.ModelId = "two";
        var secondModel = (await (await client.PostAsJsonAsync("/api/models", input)).Content.ReadFromJsonAsync<ModelDto>())!;
        input.ModelId = "one";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/models/{secondModel.Id}", input)).StatusCode);
        input.ProviderId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/models", input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/models/{model.Id}", input)).StatusCode);
        foreach (var route in new[] { "providers", "models", "prompts", "tasks" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/{route}/{Guid.NewGuid()}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/{route}/{Guid.NewGuid()}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/models/{Guid.NewGuid()}", input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/providers/{Guid.NewGuid()}",
            new ProviderInput { Name = "missing", BaseUrl = "https://example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/prompts/{Guid.NewGuid()}",
            new PromptInput { Name = "missing", Content = "missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/prompts/{Guid.NewGuid()}/copy", new { })).StatusCode);
        Guid taskId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var task = new AnalysisTask { ModelConfigId = model.Id, OriginalFileName = "history", VideoPath = "history" };
            db.AnalysisTasks.Add(task); await db.SaveChangesAsync(); taskId = task.Id;
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/models/{model.Id}")).StatusCode);
        using (var scope = app.Services.CreateScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>().AnalysisTasks.FindAsync(taskId))!.ModelConfigId);
    }
}
