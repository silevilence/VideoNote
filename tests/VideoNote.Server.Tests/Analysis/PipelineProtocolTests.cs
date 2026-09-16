using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Tests.Api;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Analysis;

public sealed class PipelineProtocolTests
{
    [Theory]
    [InlineData(AnalysisMode.DirectVideo, false, 3)]
    [InlineData(AnalysisMode.SampledFrames, false, 2)]
    [InlineData(AnalysisMode.SampledFrames, true, 5)]
    [InlineData(AnalysisMode.Subtitles, false, 1)]
    public async Task Three_modes_generate_persisted_reports_and_live_text(AnalysisMode mode, bool audio, int expectedMaps)
    {
        await using var mock = await ProtocolEndpoint.Start();
        await using var app = new ApiFactory(new()
        {
            ["Ffmpeg:FramesPerSecond"] = "0.02",
            ["Pipeline:MaxImagesPerSegment"] = "2",
            ["Pipeline:MaxOutputTokens"] = "512"
        }, runWorker: true);
        using var http = app.CreateClient();
        var modelId = await Seed(app.Services, mock.Url, mode == AnalysisMode.DirectVideo, audio);
        await using var hub = new HubConnectionBuilder().WithUrl(new Uri(http.BaseAddress!, "/hubs/analysis"), o =>
        {
            o.Transports = HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
        }).Build();
        var events = new ConcurrentQueue<AnalysisText>();
        hub.On<AnalysisText>("AnalysisText", events.Enqueue);
        await hub.StartAsync();
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/sample.mkv"));
        using var body = new StreamContent(File.OpenRead(fixture));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await http.PostAsync($"/api/tasks?fileName=sample.mkv&mode={mode}&modelConfigId={modelId}", body);
        response.EnsureSuccessStatusCode();
        var task = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        var done = await WaitTerminal(http, task.Id);
        Assert.True(done.Status == AnalysisTaskStatus.Completed, done.ErrorMessage);
        Assert.Contains("PIPELINE_OK", done.ResultText);
        Assert.Equal(expectedMaps, done.Segments!.Count);
        Assert.All(done.Segments, s => Assert.Contains("PIPELINE_OK", s.Text));
        Assert.Equal(expectedMaps + 1, mock.ChatCalls);
        Assert.Equal(100, done.ProgressPercent);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!events.Any(e => e.Stage == "report")) await Task.Delay(20, timeout.Token);
        Assert.Contains(events, e => e.Stage == "understanding");
        Assert.Equal(mode == AnalysisMode.DirectVideo ? 3 : 0, mock.Uploads);
        Assert.Equal(mock.Uploads, mock.Deletes);
        Assert.Equal(mode == AnalysisMode.SampledFrames && !audio ? 3 : 0, mock.Transcriptions);
        if (mode == AnalysisMode.SampledFrames) Assert.Contains(mock.Bodies, b => b.Contains("image"));
        if (audio) Assert.Contains(mock.Bodies, b => b.Contains("audio"));
        var reloaded = await http.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}");
        Assert.Equal(done.Segments, reloaded!.Segments);
        var conversation = await ConversationTests.Ask(http, task.Id, "请根据视频报告回答主要内容。");
        Assert.Equal("done", conversation[^1].Type);
        Assert.Contains(conversation, e => e.Type == "delta" && e.Text!.Contains("PIPELINE_OK"));
        Assert.Equal(2, (await http.GetFromJsonAsync<List<ConversationMessageDto>>($"api/tasks/{task.Id}/conversation"))!.Count);
        Assert.Equal(expectedMaps + 2, mock.ChatCalls);
    }
    internal static async Task<Guid> Seed(IServiceProvider services, string url, bool gemini, bool audio = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
        var model = new ModelConfig
        {
            ModelId = "test-model", ContextWindow = 128000, SupportsImage = true, SupportsVideo = gemini,
            SupportsAudio = audio, SupportsStreaming = true,
            Provider = new Provider { Name = "pipeline-probe", BaseUrl = url + (gemini ? "/v1beta" : "/v1"),
                Protocol = gemini ? ProviderProtocol.GeminiNative : ProviderProtocol.OpenAiCompatible,
                ApiKey = "test-only", TranscriptionModel = "test-whisper" }
        };
        db.Add(model); await db.SaveChangesAsync(); return model.Id;
    }
    internal static async Task<TaskDto> WaitTerminal(HttpClient http, Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (true)
        {
            var task = (await http.GetFromJsonAsync<TaskDto>($"/api/tasks/{id}", timeout.Token))!;
            if (task.Status is AnalysisTaskStatus.Completed or AnalysisTaskStatus.Canceled or AnalysisTaskStatus.Failed) return task;
            await Task.Delay(40, timeout.Token);
        }
    }
}

public sealed class ProtocolEndpoint : IAsyncDisposable
{
    private readonly WebApplication app;
    public string Url => app.Urls.Single();
    public int ChatCalls, Uploads, Deletes, Transcriptions;
    public readonly ConcurrentQueue<string> Bodies = new();
    public bool FailFiles, FailChat, EmptyChat, Truncate, LongMaps, Filtered, FilterReport, AbruptChat;
    public int FirstChatDelayMilliseconds;
    public TaskCompletionSource ChatStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ProtocolEndpoint(WebApplication app)
    {
        this.app = app;
        app.Run(async ctx =>
        {
            var path = ctx.Request.Path.ToString();
            if (path == "/upload/v1beta/files")
            {
                if (FailFiles) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsync("secret"); return; }
                Assert.Equal("resumable", ctx.Request.Headers["X-Goog-Upload-Protocol"]);
                Assert.Equal("test-only", ctx.Request.Headers["x-goog-api-key"]);
                ctx.Response.Headers["X-Goog-Upload-URL"] = Url + "/upload-session";
                await ctx.Response.WriteAsJsonAsync(new { }); return;
            }
            if (path == "/upload-session")
            {
                Assert.False(ctx.Request.Headers.ContainsKey("x-goog-api-key"));
                Assert.Equal("upload, finalize", ctx.Request.Headers["X-Goog-Upload-Command"]);
                using var bytes = new MemoryStream(); await ctx.Request.Body.CopyToAsync(bytes);
                Assert.True(bytes.Length > 0);
                var name = "files/video" + Interlocked.Increment(ref Uploads);
                await ctx.Response.WriteAsJsonAsync(new { file = new { name, uri = Url + "/v1beta/" + name, state = "PROCESSING" } }); return;
            }
            if (path.StartsWith("/v1beta/files/"))
            {
                if (ctx.Request.Method == "DELETE") { Interlocked.Increment(ref Deletes); await ctx.Response.WriteAsJsonAsync(new { }); return; }
                await ctx.Response.WriteAsJsonAsync(new { name = path[8..], uri = Url + path, state = "ACTIVE" }); return;
            }
            if (path == "/v1/audio/transcriptions")
            {
                Interlocked.Increment(ref Transcriptions);
                var form = await ctx.Request.ReadFormAsync();
                Assert.True(form.Files["file"]!.Length > 0);
                await ctx.Response.WriteAsync("""{"segments":[{"start":0,"end":1,"text":"A sample video with spoken content."}]}""");
                return;
            }
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            Bodies.Enqueue(body);
            Interlocked.Increment(ref ChatCalls);
            ChatStarted.TrySetResult();
            if (FailChat) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsync("secret upstream"); return; }
            var google = path.Contains("GenerateContent") || path.Contains("generateContent");
            var streaming = path.Contains("streamGenerateContent") || body.Contains("\"stream\":true");
            var decoded = System.Text.RegularExpressions.Regex.Unescape(body);
            var value = EmptyChat ? "" : LongMaps && !decoded.Contains("压缩这些") && !decoded.Contains("依据全部")
                ? string.Concat(Enumerable.Repeat("MAP", 600)) : "PIPELINE_OK";
            var filtered = Filtered && (!FilterReport || decoded.Contains("依据全部"));
            var finish = AbruptChat ? null : filtered ? "content_filter" : Truncate ? "length" : "stop";
            ctx.Response.ContentType = streaming ? "text/event-stream" : "application/json";
            var json = google
                ? JsonSerializer.Serialize(new { candidates = new[] { new { content = new { role = "model", parts = new[] { new { text = value } } }, finishReason = AbruptChat ? null : filtered ? "SAFETY" : Truncate ? "MAX_TOKENS" : "STOP" } }, modelVersion = "test" })
                : streaming
                    ? JsonSerializer.Serialize(new { id = "test", @object = "chat.completion.chunk", created = 1, model = "test", choices = new[] { new { index = 0, delta = new { role = "assistant", content = value }, finish_reason = finish } } })
                    : JsonSerializer.Serialize(new { id = "test", @object = "chat.completion", created = 1, model = "test", choices = new[] { new { index = 0, message = new { role = "assistant", content = value }, finish_reason = finish } } });
            if (ChatCalls == 1 && FirstChatDelayMilliseconds > 0)
            {
                await ctx.Response.StartAsync();
                await Task.Delay(FirstChatDelayMilliseconds, ctx.RequestAborted);
            }
            await ctx.Response.WriteAsync(streaming ? "data: " + json + "\n\n" + (google || AbruptChat ? "" : "data: [DONE]\n\n") : json);
        });
    }
    public static async Task<ProtocolEndpoint> Start()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var endpoint = new ProtocolEndpoint(builder.Build()); await endpoint.app.StartAsync(); return endpoint;
    }
    public ValueTask DisposeAsync() => app.DisposeAsync();
}
