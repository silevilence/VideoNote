using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoNote.Server.Analysis;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Tests.Api;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Analysis;

public sealed class PipelineFailureTests
{
    [Theory]
    [InlineData("http")]
    [InlineData("empty")]
    [InlineData("length")]
    [InlineData("files")]
    public async Task Invalid_upstream_results_never_become_successful_reports(string failure)
    {
        await using var endpoint = await ProtocolEndpoint.Start();
        endpoint.FailChat = failure == "http";
        endpoint.EmptyChat = failure == "empty";
        endpoint.Truncate = failure == "length";
        endpoint.FailFiles = failure == "files";
        await using var original = new ApiFactory(runWorker: true);
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IMediaPreprocessor>(); s.AddSingleton<IMediaPreprocessor>(new PreparedStub());
        }));
        using var http = app.CreateClient();
        var model = await PipelineProtocolTests.Seed(app.Services, endpoint.Url, failure == "files");
        var task = await Upload(http, model, failure == "files" ? AnalysisMode.DirectVideo : AnalysisMode.Subtitles);
        var result = await PipelineProtocolTests.WaitTerminal(http, task.Id);
        Assert.Equal(AnalysisTaskStatus.Failed, result.Status);
        Assert.Null(result.ResultText);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain("secret", result.ErrorMessage);
    }

    [Fact]
    public async Task Oversized_intermediate_notes_are_reduced_and_nonstreaming_models_work()
    {
        await using var endpoint = await ProtocolEndpoint.Start();
        endpoint.LongMaps = true;
        await using var original = new ApiFactory(new() { ["Pipeline:MaxOutputTokens"] = "256" }, runWorker: true);
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IMediaPreprocessor>(); s.AddSingleton<IMediaPreprocessor>(new PreparedStub { Long = true });
        }));
        using var http = app.CreateClient();
        var id = await PipelineProtocolTests.Seed(app.Services, endpoint.Url, false);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var model = (await db.ModelConfigs.FindAsync(id))!;
            model.ContextWindow = 4096; model.SupportsStreaming = false; await db.SaveChangesAsync();
        }
        var task = await Upload(http, id, AnalysisMode.Subtitles);
        var result = await PipelineProtocolTests.WaitTerminal(http, task.Id);
        Assert.True(result.Status == AnalysisTaskStatus.Completed, result.ErrorMessage);
        Assert.True(result.Segments!.Count > 1);
        Assert.True(endpoint.ChatCalls > result.Segments.Count + 1);
        Assert.Contains("PIPELINE_OK", result.ResultText);
    }

    private static async Task<TaskDto> Upload(HttpClient http, Guid model, AnalysisMode mode)
    {
        using var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await http.PostAsync($"/api/tasks?fileName=test.mp4&mode={mode}&modelConfigId={model}", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }
    private sealed class PreparedStub : IMediaPreprocessor
    {
        public bool Long;
        public Task<PreparedMedia> PrepareAsync(AnalysisTask task, ModelConfig model, CancellationToken ct)
        {
            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/sample.mkv"));
            return Task.FromResult(new PreparedMedia(1, [new(source, 0, 1)], [], [],
                [new(0, 1, Long ? new string('x', 7000) : "test subtitle")]));
        }
    }
}
