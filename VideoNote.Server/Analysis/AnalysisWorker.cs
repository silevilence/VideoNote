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
        // Queued work is durable. Interrupted work is failed explicitly; partial model calls are never replayed silently.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var tasks = await db.AnalysisTasks.AsNoTracking().Where(t =>
                t.Status == AnalysisTaskStatus.Queued || t.Status == AnalysisTaskStatus.Preprocessing ||
                t.Status == AnalysisTaskStatus.Understanding || t.Status == AnalysisTaskStatus.Combining)
                .OrderBy(t => t.CreatedAtUtc).ToListAsync(stoppingToken);
            foreach (var task in tasks)
                if (task.Status == AnalysisTaskStatus.Queued) queue.Enqueue(task.Id);
                else await progress.UpdateAsync(task.Id, AnalysisTaskStatus.Failed, task.ProgressPercent,
                    "服务中断", stoppingToken, "上次分析被服务停止中断，请重新创建任务。");
        }
        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await queue.Gate.WaitAsync(stoppingToken);
            try
            {
                await using var check = scopes.CreateAsyncScope();
                var db = check.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
                if (!await db.AnalysisTasks.AnyAsync(t => t.Id == id && t.Status == AnalysisTaskStatus.Queued, stoppingToken))
                    continue;
                queue.Register(id, cancellation);
            }
            finally { queue.Gate.Release(); }
            try
            {
                await progress.UpdateAsync(id, AnalysisTaskStatus.Preprocessing, 1, "准备分析", cancellation.Token);
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IAnalysisPipeline>().RunAsync(id, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                await progress.UpdateAsync(id, AnalysisTaskStatus.Completed, 100, "分析完成", cancellation.Token, result: result);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                await progress.UpdateAsync(id, AnalysisTaskStatus.Canceled, 0, "已取消", CancellationToken.None);
            }
            catch (Exception ex)
            {
                // SDK exceptions may contain upstream bodies or credentials. Never persist or log their raw message.
                logger.LogWarning("任务 {TaskId} 分析失败（{Type}）。", id, ex.GetType().Name);
                var message = ex is AnalysisException ? ex.Message :
                    "分析失败，请检查视频、模型配置、网络或服务端日志中的错误类型。";
                await progress.UpdateAsync(id, AnalysisTaskStatus.Failed, 0, "分析失败", error: message);
            }
            finally
            {
                await queue.Gate.WaitAsync(CancellationToken.None);
                try { queue.Release(id); }
                finally { queue.Gate.Release(); }
            }
        }
    }
}
public sealed class AnalysisException(string message) : Exception(message);
