using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Analysis;

public interface IAnalysisPipeline
{
    Task<string> RunAsync(Guid taskId, CancellationToken ct);
}
public sealed class AnalysisWorker(IServiceScopeFactory scopes, AnalysisQueue queue,
    AnalysisProgressWriter progress, ILogger<AnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Retry storage only: model calls and partial analyses must never be replayed.
            var tasks = await RetryStorageAsync(async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
                return await db.AnalysisTasks.AsNoTracking().Where(t =>
                    t.Status == AnalysisTaskStatus.Queued || t.Status == AnalysisTaskStatus.Preprocessing ||
                    t.Status == AnalysisTaskStatus.Understanding || t.Status == AnalysisTaskStatus.Combining)
                    .OrderBy(t => t.CreatedAtUtc).ToListAsync(stoppingToken);
            }, stoppingToken);
            foreach (var task in tasks)
                if (task.Status == AnalysisTaskStatus.Queued) queue.Enqueue(task.Id);
                else await SaveTerminalAsync(task.Id, AnalysisTaskStatus.Failed, "服务中断",
                    "上次分析被服务停止中断，请重新创建任务。", stoppingToken);

            await foreach (var id in queue.ReadAllAsync(stoppingToken))
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var claimed = await RetryStorageAsync(async () =>
                {
                    await queue.Gate.WaitAsync(stoppingToken);
                    try
                    {
                        await using var check = scopes.CreateAsyncScope();
                        var db = check.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
                        if (!await db.AnalysisTasks.AnyAsync(t => t.Id == id && t.Status == AnalysisTaskStatus.Queued, stoppingToken))
                            return false;
                        queue.Register(id, cancellation);
                        return true;
                    }
                    finally { queue.Gate.Release(); }
                }, stoppingToken);
                if (!claimed) continue;
                try
                {
                    await progress.UpdateAsync(id, AnalysisTaskStatus.Preprocessing, 1, "准备分析", cancellation.Token);
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<IAnalysisPipeline>().RunAsync(id, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    await RetryStorageAsync(async () =>
                    {
                        await progress.UpdateAsync(id, AnalysisTaskStatus.Completed, 100, "分析完成", cancellation.Token, result: result);
                        return true;
                    }, cancellation.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Allow a bounded final write during shutdown; restart recovery covers unavailable storage.
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await SaveTerminalAsync(id, AnalysisTaskStatus.Failed, "服务中断",
                            "分析被服务停止中断，请重新创建任务。", cleanup.Token);
                    }
                    catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
                    { logger.LogWarning("任务 {TaskId} 中断状态暂未保存，将在下次启动时恢复。", id); }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    await SaveTerminalAsync(id, AnalysisTaskStatus.Canceled, "已取消", null, stoppingToken);
                }
                catch (Exception ex)
                {
                    // SDK errors can contain credentials or upstream bodies; record only safe messages.
                    logger.LogWarning("任务 {TaskId} 分析失败（{Type}）。", id, ex.GetType().Name);
                    var message = ex is AnalysisException ? ex.Message :
                        "分析失败，请检查视频、模型配置、网络或服务端日志中的错误类型。";
                    await SaveTerminalAsync(id, AnalysisTaskStatus.Failed, "分析失败", message, stoppingToken);
                }
                finally
                {
                    await queue.Gate.WaitAsync(CancellationToken.None);
                    try { queue.Release(id); }
                    finally { queue.Gate.Release(); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private Task<bool> SaveTerminalAsync(Guid id, AnalysisTaskStatus status, string description, string? error, CancellationToken ct) =>
        RetryStorageAsync(async () =>
        {
            await progress.UpdateAsync(id, status, 0, description, ct, error: error);
            return true;
        }, ct);

    private async Task<T> RetryStorageAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (Exception ex) when (ex is DbException or DbUpdateException)
            {
                logger.LogWarning("分析队列存储暂不可用（{Type}），将重试；任务保留。", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }
}
public sealed class AnalysisException(string message) : Exception(message);
