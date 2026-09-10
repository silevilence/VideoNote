using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoNote.Server.Analysis;
using VideoNote.Server.Tests.Api;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Analysis;

public sealed class WorkerTests
{
    [Fact]
    public async Task Worker_streams_stages_completes_fails_and_cancels_serial_work()
    {
        var fake = new ControlledPipeline();
        await using var baseline = new ApiFactory(runWorker: true);
        await using var app = baseline.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IAnalysisPipeline>();
            s.AddSingleton<IAnalysisPipeline>(fake);
        }));
        using var http = app.CreateClient();
        fake.Progress = app.Services.GetRequiredService<AnalysisProgressWriter>();
        await using var hub = new HubConnectionBuilder().WithUrl(new Uri(http.BaseAddress!, "/hubs/analysis"), o =>
        {
            o.Transports = HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
        }).Build();
        var events = new System.Collections.Concurrent.ConcurrentQueue<AnalysisProgress>();
        hub.On<AnalysisProgress>("AnalysisProgress", events.Enqueue);
        await hub.StartAsync();

        var first = await Upload(http);
        await fake.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = await Upload(http);
        Assert.Equal(AnalysisTaskStatus.Queued, queued.Status);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync($"/api/tasks/{queued.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync($"/api/tasks/{first.Id}/cancel", null)).StatusCode);
        await WaitFor(http, first.Id, AnalysisTaskStatus.Canceled);
        fake.Mode = "complete";
        var complete = await Upload(http);
        var result = await WaitFor(http, complete.Id, AnalysisTaskStatus.Completed);
        Assert.Equal(100, result.ProgressPercent);
        Assert.Equal("# report", result.ResultText);
        Assert.NotNull(result.CompletedAtUtc);
        fake.Mode = "fail";
        var failed = await Upload(http);
        Assert.Contains("分析失败", (await WaitFor(http, failed.Id, AnalysisTaskStatus.Failed)).ErrorMessage);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!events.Any(e => e.TaskId == complete.Id && e.Status == AnalysisTaskStatus.Completed))
            await Task.Delay(20, timeout.Token);
        Assert.Contains(events, e => e.TaskId == complete.Id && e.Status == AnalysisTaskStatus.Understanding);
        Assert.Contains(events, e => e.TaskId == complete.Id && e.Status == AnalysisTaskStatus.Combining);
        Assert.Equal(1, fake.MaximumActive);
        Assert.DoesNotContain(queued.Id, fake.Ran);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"/api/tasks/{Guid.NewGuid()}/cancel", null)).StatusCode);
    }

    internal static async Task<TaskDto> Upload(HttpClient http)
    {
        using var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await http.PostAsync("/api/tasks?fileName=test.mp4", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }
    internal static async Task<TaskDto> WaitFor(HttpClient http, Guid id, AnalysisTaskStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var task = (await http.GetFromJsonAsync<TaskDto>($"/api/tasks/{id}", timeout.Token))!;
            if (task.Status == expected) return task;
            await Task.Delay(20, timeout.Token);
        }
    }
    private sealed class ControlledPipeline : IAnalysisPipeline
    {
        public AnalysisProgressWriter Progress = null!;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Mode = "wait";
        public List<Guid> Ran = [];
        private int active;
        public int MaximumActive;
        public async Task<string> RunAsync(Guid id, CancellationToken ct)
        {
            Ran.Add(id);
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref active));
            try
            {
                Started.TrySetResult();
                if (Mode == "wait") await Task.Delay(Timeout.Infinite, ct);
                if (Mode == "fail") throw new HttpRequestException("secret upstream body");
                await Progress.UpdateAsync(id, AnalysisTaskStatus.Understanding, 40, "理解", ct);
                await Progress.UpdateAsync(id, AnalysisTaskStatus.Combining, 90, "组合", ct);
                return "# report";
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}
