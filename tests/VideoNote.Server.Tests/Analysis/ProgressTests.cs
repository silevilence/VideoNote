using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Analysis;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Tests.Api;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Analysis;

public sealed class ProgressTests
{
    [Fact]
    public async Task Progress_is_monotonic_and_terminal_states_cannot_be_overwritten()
    {
        await using var app = new ApiFactory();
        using var http = app.CreateClient();
        var task = await WorkerTests.Upload(http);
        var progress = app.Services.GetRequiredService<AnalysisProgressWriter>();
        await progress.UpdateAsync(task.Id, AnalysisTaskStatus.Preprocessing, 10, "prepare");
        await progress.UpdateAsync(task.Id, AnalysisTaskStatus.Understanding, 50, "map");
        await progress.UpdateAsync(task.Id, AnalysisTaskStatus.Understanding, 20, "map");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            progress.UpdateAsync(task.Id, AnalysisTaskStatus.Preprocessing, 60, "regression"));
        await progress.UpdateAsync(task.Id, AnalysisTaskStatus.Canceled, 0, "canceled");
        await progress.UpdateAsync(task.Id, AnalysisTaskStatus.Completed, 100, "late completion", result: "wrong");
        var result = await WorkerTests.WaitFor(http, task.Id, AnalysisTaskStatus.Canceled);
        Assert.Equal(50, result.ProgressPercent);
        Assert.Null(result.ResultText);
        Assert.Equal(task.StageDescription, result.Logs![0].Description);
        Assert.Equal(new[] { "排队等待分析", "prepare", "map", "canceled" }, result.Logs!.Select(l => l.Description));
        Assert.Equal(50, result.Logs![^1].ProgressPercent);
        await progress.UpdateAsync(Guid.NewGuid(), AnalysisTaskStatus.Preprocessing, 1, "missing");
    }

    [Fact]
    public async Task Restart_recovers_queued_work_and_marks_interrupted_work_failed()
    {
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        Guid queued, interrupted;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var a = new AnalysisTask { OriginalFileName = "queued", VideoPath = "unused" };
            var b = new AnalysisTask { OriginalFileName = "interrupted", VideoPath = "unused", Status = AnalysisTaskStatus.Combining };
            db.AddRange(a, b); await db.SaveChangesAsync();
            queued = a.Id; interrupted = b.Id;
        }
        var worker = ActivatorUtilities.CreateInstance<AnalysisWorker>(app.Services);
        await worker.StartAsync(CancellationToken.None);
        using var http = app.CreateClient();
        var failed = await WorkerTests.WaitFor(http, interrupted, AnalysisTaskStatus.Failed);
        Assert.Contains("中断", failed.ErrorMessage);
        await WorkerTests.WaitFor(http, queued, AnalysisTaskStatus.Failed);
        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
    }
}
