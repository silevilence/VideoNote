using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data;
using VideoNote.Server.Realtime;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Analysis;

public sealed class AnalysisProgressWriter(IServiceScopeFactory scopes, AnalysisQueue queue,
    IHubContext<AnalysisHub> hub, ILogger<AnalysisProgressWriter> logger)
{
    private long retryNotificationsAt;
    public static bool Terminal(AnalysisTaskStatus status) =>
        status is AnalysisTaskStatus.Completed or AnalysisTaskStatus.Failed or AnalysisTaskStatus.Canceled;

    public async Task UpdateAsync(Guid id, AnalysisTaskStatus status, int percent, string description,
        CancellationToken ct = default, string? error = null, string? result = null)
    {
        await queue.Gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var task = await db.AnalysisTasks.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (task is null || Terminal(task.Status)) return;
            if (!Terminal(status) && (int)status < (int)task.Status) throw new InvalidOperationException("任务阶段不能倒退。");
            task.Status = status;
            task.ProgressPercent = Math.Clamp(Math.Max(task.ProgressPercent, percent), 0, 100);
            task.StageDescription = description;
            task.ErrorMessage = error;
            if (status == AnalysisTaskStatus.Preprocessing) task.StartedAtUtc ??= DateTime.UtcNow;
            if (Terminal(status)) task.CompletedAtUtc = DateTime.UtcNow;
            if (result is not null) task.ResultText = result;
            await db.SaveChangesAsync(ct);
            // Preserve event order with cancellation; transport waiting is bounded.
            await NotifyAsync(id, "AnalysisProgress", new AnalysisProgress(id, status, task.ProgressPercent, description, error), ct);
        }
        finally { queue.Gate.Release(); }
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct)
    {
        await queue.Gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var task = await db.AnalysisTasks.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (task is null) return false;
            if (Terminal(task.Status)) return true;
            task.Status = AnalysisTaskStatus.Canceled;
            task.StageDescription = "已取消";
            task.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            queue.Cancel(id);
            await NotifyAsync(id, "AnalysisProgress",
                new AnalysisProgress(id, task.Status, task.ProgressPercent, task.StageDescription, null), ct);
            return true;
        }
        finally { queue.Gate.Release(); }
    }

    public Task TextAsync(Guid id, string stage, int segment, string text, CancellationToken ct) =>
        NotifyAsync(id, "AnalysisText", new AnalysisText(id, stage, segment, text), ct);

    private async Task NotifyAsync(Guid id, string method, object message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Environment.TickCount64 < Volatile.Read(ref retryNotificationsAt)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try { await hub.Clients.All.SendAsync(method, message, timeout.Token).WaitAsync(timeout.Token); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Avoid paying a transport timeout for every token while clients recover via polling.
            Volatile.Write(ref retryNotificationsAt, Environment.TickCount64 + 5000);
            logger.LogWarning("任务 {TaskId} 通知推送失败（{Type}），可通过详情接口恢复。", id, ex.GetType().Name);
        }
    }
}
