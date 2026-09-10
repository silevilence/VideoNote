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
    public static bool Terminal(AnalysisTaskStatus status) =>
        status is AnalysisTaskStatus.Completed or AnalysisTaskStatus.Failed or AnalysisTaskStatus.Canceled;

    public async Task UpdateAsync(Guid id, AnalysisTaskStatus status, int percent, string description,
        CancellationToken ct = default, string? error = null, string? result = null)
    {
        AnalysisProgress? progress = null;
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
            progress = new(id, status, task.ProgressPercent, description, error);
            // Publish under the same gate so cancellation cannot be followed by an older progress event.
            try { await hub.Clients.All.SendAsync("AnalysisProgress", progress, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogWarning("任务 {TaskId} 进度推送失败（{Type}），可通过详情接口恢复。", id, ex.GetType().Name); }
        }
        finally { queue.Gate.Release(); }
    }

    public Task TextAsync(Guid id, string stage, int segment, string text, CancellationToken ct) =>
        hub.Clients.All.SendAsync("AnalysisText", new AnalysisText(id, stage, segment, text), ct);
}
