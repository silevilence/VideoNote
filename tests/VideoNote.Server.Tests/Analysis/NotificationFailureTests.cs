using System.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoNote.Server.Analysis;
using VideoNote.Server.Realtime;
using VideoNote.Server.Tests.Api;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Analysis;

public sealed class NotificationFailureTests
{
    [Fact]
    public async Task Notification_failure_does_not_lose_model_result_or_stop_next_task()
    {
        await using var original = new ApiFactory(runWorker: true);
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IHubContext<AnalysisHub>>(); s.AddSingleton<IHubContext<AnalysisHub>>(new FailingHub());
            s.RemoveAll<IAnalysisPipeline>(); s.AddScoped<IAnalysisPipeline, TextPipeline>();
        }));
        using var http = app.CreateClient();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var task = await WorkerTests.Upload(http);
            var result = await PipelineProtocolTests.WaitTerminal(http, task.Id);
            Assert.Equal(AnalysisTaskStatus.Completed, result.Status);
            Assert.Equal("saved report", result.ResultText);
        }
    }

    [Fact]
    public async Task Cancel_returns_success_and_signals_running_work_when_broadcast_fails()
    {
        await using var original = new ApiFactory();
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IHubContext<AnalysisHub>>(); s.AddSingleton<IHubContext<AnalysisHub>>(new FailingHub());
        }));
        using var http = app.CreateClient();
        var task = await WorkerTests.Upload(http);
        var queue = app.Services.GetRequiredService<AnalysisQueue>();
        using var running = new CancellationTokenSource();
        queue.Register(task.Id, running);
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync($"/api/tasks/{task.Id}/cancel", null)).StatusCode);
            Assert.True(running.IsCancellationRequested);
            await WorkerTests.WaitFor(http, task.Id, AnalysisTaskStatus.Canceled);
        }
        finally { queue.Release(task.Id); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_transport_failure_or_timeout_is_isolated_but_user_cancellation_is_preserved(bool stall)
    {
        await using var original = new ApiFactory();
        var hub = new FailingHub(stall);
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IHubContext<AnalysisHub>>(); s.AddSingleton<IHubContext<AnalysisHub>>(hub);
        }));
        _ = app.CreateClient();
        var writer = app.Services.GetRequiredService<AnalysisProgressWriter>();
        await writer.TextAsync(Guid.NewGuid(), "report", 0, "text", default).WaitAsync(TimeSpan.FromSeconds(10));
        var calls = hub.Calls;
        Assert.Equal(1, calls);
        await writer.TextAsync(Guid.NewGuid(), "report", 0, "more", default);
        Assert.Equal(calls, hub.Calls);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.TextAsync(Guid.NewGuid(), "report", 0, "text", canceled.Token));
    }

    private sealed class TextPipeline(AnalysisProgressWriter writer) : IAnalysisPipeline
    {
        public async Task<string> RunAsync(Guid id, CancellationToken ct)
        {
            await writer.UpdateAsync(id, AnalysisTaskStatus.Understanding, 40, "map", ct);
            await writer.TextAsync(id, "understanding", 0, "model text", ct);
            await writer.UpdateAsync(id, AnalysisTaskStatus.Combining, 90, "reduce", ct);
            return "saved report";
        }
    }
    internal sealed class FailingHub(bool stall = false) : IHubContext<AnalysisHub>
    {
        private readonly ClientsStub clients = new(stall);
        public IHubClients Clients => clients;
        public int Calls => clients.Calls;
        public IGroupManager Groups => throw new NotSupportedException();
        private sealed class ClientsStub(bool stall) : IHubClients, IClientProxy
        {
            public IClientProxy All => this;
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
            public IClientProxy Client(string connectionId) => this;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => this;
            public IClientProxy Group(string groupName) => this;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => this;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => this;
            public IClientProxy User(string userId) => this;
            public IClientProxy Users(IReadOnlyList<string> userIds) => this;
            public int Calls;
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                Calls++;
                return stall ? Task.Delay(Timeout.Infinite, cancellationToken) :
                    throw new IOException("simulated transport failure");
            }
        }
    }
}
