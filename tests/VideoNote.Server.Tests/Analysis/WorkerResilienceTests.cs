using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoNote.Server.Analysis;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Tests.Api;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Analysis;

public sealed class WorkerResilienceTests
{
    [Theory]
    [InlineData("recovery-read")]
    [InlineData("recovery-write")]
    [InlineData("claim")]
    [InlineData("completion")]
    public async Task Storage_failure_is_retried_without_replaying_analysis_or_losing_queued_work(string stage)
    {
        var fault = new StorageFault(stage);
        var pipeline = new ImmediatePipeline(stage == "completion" ? () => fault.Arm() : null);
        await using var original = new ApiFactory();
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddDbContext<VideoNoteDbContext>(o => o.AddInterceptors(fault));
            s.RemoveAll<IAnalysisPipeline>(); s.AddSingleton<IAnalysisPipeline>(pipeline);
        }));
        using var http = app.CreateClient();
        var task = await WorkerTests.Upload(http);
        Guid interrupted;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var old = new AnalysisTask { OriginalFileName = "old", VideoPath = "unused", Status = AnalysisTaskStatus.Combining };
            db.Add(old); await db.SaveChangesAsync(); interrupted = old.Id;
        }
        if (stage != "completion") fault.Arm();
        using var worker = ActivatorUtilities.CreateInstance<AnalysisWorker>(app.Services);
        try
        {
            await worker.StartAsync(default);
            await fault.Observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("中断", (await WorkerTests.WaitFor(http, interrupted, AnalysisTaskStatus.Failed)).ErrorMessage);
            Assert.Equal("report", (await WorkerTests.WaitFor(http, task.Id, AnalysisTaskStatus.Completed)).ResultText);
            var next = await WorkerTests.Upload(http);
            await WorkerTests.WaitFor(http, next.Id, AnalysisTaskStatus.Completed);
            Assert.Equal(2, pipeline.Calls);
            Assert.False(worker.ExecuteTask!.IsCompleted);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task Graceful_stop_marks_running_work_interrupted_and_preserves_queued_work()
    {
        var pipeline = new WaitingPipeline();
        await using var original = new ApiFactory();
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IAnalysisPipeline>(); s.AddSingleton<IAnalysisPipeline>(pipeline);
        }));
        using var http = app.CreateClient();
        var running = await WorkerTests.Upload(http);
        using var worker = ActivatorUtilities.CreateInstance<AnalysisWorker>(app.Services);
        await worker.StartAsync(default);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = await WorkerTests.Upload(http);
        await worker.StopAsync(default);
        var stopped = await WorkerTests.WaitFor(http, running.Id, AnalysisTaskStatus.Failed);
        Assert.Contains("服务停止", stopped.ErrorMessage);
        await WorkerTests.WaitFor(http, queued.Id, AnalysisTaskStatus.Queued);
        Assert.False(app.Services.GetRequiredService<AnalysisQueue>().IsActive(running.Id));
    }

    private sealed class ImmediatePipeline(Action? beforeReturn) : IAnalysisPipeline
    {
        public int Calls;
        public Task<string> RunAsync(Guid id, CancellationToken ct)
        { Calls++; beforeReturn?.Invoke(); return Task.FromResult("report"); }
    }
    private sealed class WaitingPipeline : IAnalysisPipeline
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> RunAsync(Guid id, CancellationToken ct)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return ""; }
    }
    private sealed class StorageFault(string stage) : DbCommandInterceptor
    {
        private int armed;
        public TaskCompletionSource Observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() { if (!Observed.Task.IsCompleted) Interlocked.Exchange(ref armed, 1); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            var matches = stage switch
            {
                "claim" => sql.Contains("SELECT EXISTS"),
                "recovery-read" => sql.Contains("ORDER BY") && sql.Contains("Status"),
                _ => sql.StartsWith("UPDATE")
            };
            if (matches && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Observed.TrySetResult();
                throw new SqliteException("simulated storage fault", 5);
            }
            return ValueTask.FromResult(result);
        }
    }
}
