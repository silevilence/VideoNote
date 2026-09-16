using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoNote.Server.Analysis;
using VideoNote.Server.Conversation;
using VideoNote.Server.Data;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/tasks/{id:guid}/conversation")]
public sealed class ConversationsController(VideoNoteDbContext db, ConversationService service, ConversationRuns runs,
    AnalysisQueue queue, IOptions<ConversationOptions> options, ILogger<ConversationsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ConversationMessageDto>>> History(Guid id, CancellationToken ct)
    {
        if (!await db.AnalysisTasks.AnyAsync(t => t.Id == id, ct)) return NotFound();
        return await db.ConversationMessages.AsNoTracking().Where(m => m.AnalysisTaskId == id)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .Select(m => new ConversationMessageDto(m.Id, m.Role, m.Content, m.CreatedAtUtc)).ToListAsync(ct);
    }

    [HttpPost]
    public async Task<IActionResult> Reply(Guid id, ConversationInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Message)) return BadRequest(new { message = "请输入问题。" });
        await queue.Gate.WaitAsync(ct);
        try
        {
            var task = await db.AnalysisTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
            if (task is null) return NotFound();
            if (task.Status != AnalysisTaskStatus.Completed || string.IsNullOrWhiteSpace(task.ResultText))
                return Conflict(new { message = "任务完成并生成报告后才能对话。" });
            if (!runs.TryBegin(id)) return Conflict(new { message = "该任务正在生成回答，请等待完成后再提问。" });
        }
        finally { queue.Gate.Release(); }
        try
        {
            Response.ContentType = "application/x-ndjson; charset=utf-8";
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Accel-Buffering"] = "no";
            await Response.StartAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
            try
            {
                await foreach (var item in service.Reply(id, input.Message, timeout.Token)) await Write(item, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogWarning("任务 {TaskId} 对话失败（{Type}）。", id, ex.GetType().Name);
                var message = ex switch
                {
                    ConversationException e => e.Message,
                    OperationCanceledException => "对话请求超时，本轮未保存，请稍后重试。",
                    _ => "对话生成或保存失败，本轮未完成。请检查模型配置与服务连接后重试。"
                };
                await Write(new("error", message), ct);
            }
        }
        finally { runs.End(id); }
        return new EmptyResult();
    }

    private async Task Write(ConversationEvent item, CancellationToken ct)
    {
        await Response.WriteAsync(JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
