using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Conversation;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Tests.Analysis;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Api;

public sealed class ConversationTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Agent_uses_context_once_and_persists_successful_turns(bool gemini, bool streaming)
    {
        await using var mock = await ProtocolEndpoint.Start();
        await using var app = new ApiFactory(); using var http = app.CreateClient();
        var (id, model) = await Seed(app, mock, gemini, streaming);
        Assert.Null((await http.GetFromJsonAsync<ConversationSettingsDto>("/api/settings/conversation"))!.ModelConfigId);
        for (var i = 0; i < 2; i++)
        {
            var events = await Ask(http, id, "question " + i);
            Assert.Contains(events, e => e.Type == "delta" && e.Text!.Contains("PIPELINE_OK"));
            Assert.Equal("done", events[^1].Type);
        }
        using var reloaded = app.CreateClient();
        var history = await reloaded.GetFromJsonAsync<List<ConversationMessageDto>>($"api/tasks/{id}/conversation");
        Assert.Equal(new[] { ConversationRole.User, ConversationRole.Assistant, ConversationRole.User, ConversationRole.Assistant }, history!.Select(m => m.Role));
        foreach (var body in mock.Bodies)
        {
            Assert.Equal(1, body.Split("REPORT_EVIDENCE").Length - 1);
            Assert.Equal(1, body.Split("SEGMENT_EVIDENCE").Length - 1);
        }
        Assert.Contains("question 0", mock.Bodies.Last());
        Assert.Equal(HttpStatusCode.NoContent, (await http.PutAsJsonAsync("api/settings/conversation", new ConversationSettingsDto(model))).StatusCode);
        Assert.Equal(model, (await http.GetFromJsonAsync<ConversationSettingsDto>("api/settings/conversation"))!.ModelConfigId);
        await http.DeleteAsync($"api/models/{model}");
        Assert.Null((await http.GetFromJsonAsync<ConversationSettingsDto>("api/settings/conversation"))!.ModelConfigId);
        Assert.Equal("error", (await Ask(http, id, "missing model"))[^1].Type);
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"api/tasks/{id}")).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>().ConversationMessages.ToListAsync());
    }

    [Theory]
    [InlineData(false, true, "filter")]
    [InlineData(false, false, "filter")]
    [InlineData(true, true, "filter")]
    [InlineData(true, false, "filter")]
    [InlineData(false, true, "length")]
    [InlineData(false, false, "length")]
    [InlineData(false, true, "empty")]
    [InlineData(false, true, "failure")]
    [InlineData(false, true, "budget")]
    public async Task Incomplete_or_failed_replies_never_save_a_turn(bool gemini, bool streaming, string failure)
    {
        await using var mock = await ProtocolEndpoint.Start();
        mock.Filtered = failure == "filter"; mock.Truncate = failure == "length";
        mock.EmptyChat = failure == "empty"; mock.FailChat = failure == "failure";
        await using var app = new ApiFactory(); using var http = app.CreateClient();
        var (id, _) = await Seed(app, mock, gemini, streaming, failure == "budget" ? 1000 : 128000);
        var events = await Ask(http, id, "question");
        Assert.Equal("error", events[^1].Type);
        Assert.DoesNotContain("secret", events[^1].Text);
        Assert.Empty((await http.GetFromJsonAsync<List<ConversationMessageDto>>($"api/tasks/{id}/conversation"))!);
        if (failure == "budget") Assert.Equal(0, mock.ChatCalls);
    }

    [Fact]
    public async Task Timeout_blocks_overlap_and_delete_then_releases_admission()
    {
        await using var mock = await ProtocolEndpoint.Start(); mock.FirstChatDelayMilliseconds = 3000;
        await using var app = new ApiFactory(new() { ["Conversation:RequestTimeoutSeconds"] = "1" }); using var http = app.CreateClient();
        var (id, _) = await Seed(app, mock);
        var pending = Ask(http, id, "slow");
        await mock.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"api/tasks/{id}/conversation", new ConversationInput { Message = "overlap" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.DeleteAsync($"api/tasks/{id}")).StatusCode);
        Assert.Contains("超时", (await pending)[^1].Text);
        Assert.False(app.Services.GetRequiredService<ConversationRuns>().IsActive(id));
        Assert.Empty((await http.GetFromJsonAsync<List<ConversationMessageDto>>($"api/tasks/{id}/conversation"))!);
        Assert.Equal("done", (await Ask(http, id, "retry"))[^1].Type);
    }

    [Fact]
    public async Task Invalid_and_unfinished_tasks_are_rejected_and_global_model_overrides_task_model()
    {
        await using var mock = await ProtocolEndpoint.Start();
        await using var app = new ApiFactory(); using var http = app.CreateClient();
        var (id, model) = await Seed(app, mock);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"api/tasks/{id}/conversation", new { message = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync($"api/tasks/{Guid.NewGuid()}/conversation", new { message = "hello" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"api/tasks/{Guid.NewGuid()}/conversation")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync("api/settings/conversation", new ConversationSettingsDto(Guid.NewGuid()))).StatusCode);
        await http.PutAsJsonAsync("api/settings/conversation", new ConversationSettingsDto(model));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var task = (await db.AnalysisTasks.FindAsync(id))!;
            task.ModelConfigId = null; await db.SaveChangesAsync();
        }
        Assert.Equal("done", (await Ask(http, id, "use global"))[^1].Type);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            (await db.AnalysisTasks.FindAsync(id))!.Status = AnalysisTaskStatus.Failed; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"api/tasks/{id}/conversation", new { message = "not finished" })).StatusCode);
    }

    private static async Task<(Guid Id, Guid Model)> Seed(ApiFactory app, ProtocolEndpoint mock, bool gemini = false, bool streaming = true, int window = 128000)
    {
        var model = await PipelineProtocolTests.Seed(app.Services, mock.Url, gemini);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
        var configured = (await db.ModelConfigs.FindAsync(model))!; configured.SupportsStreaming = streaming; configured.ContextWindow = window;
        var task = new AnalysisTask { OriginalFileName = "chat.mp4", VideoPath = "unused", ModelConfigId = model,
            Status = AnalysisTaskStatus.Completed, ResultText = "REPORT_EVIDENCE", SegmentResultsJson = JsonSerializer.Serialize(new[] { new SegmentResultDto(0, 0, 10, "SEGMENT_EVIDENCE") }) };
        db.Add(task); await db.SaveChangesAsync(); return (task.Id, model);
    }

    internal static async Task<List<ConversationEvent>> Ask(HttpClient http, Guid id, string text)
    {
        using var response = await http.PostAsJsonAsync($"api/tasks/{id}/conversation", new ConversationInput { Message = text });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<ConversationEvent>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToList();
    }
}
