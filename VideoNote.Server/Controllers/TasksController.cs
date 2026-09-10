using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using VideoNote.Server.Analysis;
using VideoNote.Server.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Storage;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/tasks")]
public sealed class TasksController(VideoNoteDbContext db, VideoFileStore files, IOptions<UploadOptions> options,
    AnalysisQueue queue, IHubContext<AnalysisHub> hub) : ControllerBase
{
    private static TaskDto Dto(AnalysisTask t, bool includeContent = false) => new(t.Id, t.OriginalFileName, t.Mode, t.Status, t.StageDescription, t.CreatedAtUtc, t.ModelConfigId, t.PromptTemplateId, includeContent ? t.PromptContentSnapshot : null,
        t.ProgressPercent, t.ErrorMessage, includeContent ? t.ResultText : null, t.StartedAtUtc, t.CompletedAtUtc, includeContent ? System.Text.Json.JsonSerializer.Deserialize<List<SegmentResultDto>>(t.SegmentResultsJson) : null);
    [HttpGet("upload-limits")]
    public UploadLimitsDto Limits() => new(options.Value.MaxBytes, options.Value.AllowedExtensions);

    [HttpGet]
    public async Task<IEnumerable<TaskDto>> List(CancellationToken ct) =>
        (await db.AnalysisTasks.AsNoTracking().OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct)).Select(t => Dto(t));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskDto>> Get(Guid id, CancellationToken ct) =>
        await db.AnalysisTasks.FindAsync([id], ct) is { } t ? Ok(Dto(t, true)) : NotFound();

    [HttpPost, Consumes("application/octet-stream"), DisableRequestSizeLimit]
    public async Task<ActionResult<TaskDto>> Create([FromQuery] CreateTaskInput input, CancellationToken ct)
    {
        var selectedModel = input.ModelConfigId is { } modelId
            ? await db.ModelConfigs.AsNoTracking().SingleOrDefaultAsync(m => m.Id == modelId, ct) : null;
        if (input.ModelConfigId.HasValue && selectedModel is null)
            return BadRequest(new { message = "所选模型不存在。" });
        if (selectedModel is not null && !input.AllowCapabilityOverride &&
            !ModelCapabilityRules.Matches(input.Mode, selectedModel.SupportsImage, selectedModel.SupportsVideo))
            return BadRequest(new { message = $"所选模型未声明{ModelCapabilityRules.RequiredCapability(input.Mode)}能力；请更换模型或显式启用手动覆盖。" });
        if (input.PromptTemplateId.HasValue && !await db.PromptTemplates.AnyAsync(p => p.Id == input.PromptTemplateId, ct))
            return BadRequest(new { message = "所选模板不存在。" });
        var prompt = input.PromptTemplateId.HasValue ? await db.PromptTemplates.FindAsync([input.PromptTemplateId.Value], ct) : null;
        var task = new AnalysisTask
        {
            OriginalFileName = input.FileName,
            Mode = input.Mode,
            ModelConfigId = input.ModelConfigId,
            PromptTemplateId = input.PromptTemplateId,
            PromptContentSnapshot = prompt?.Content,
            StageDescription = "排队等待分析"
        };
        try
        {
            task.VideoPath = await files.SaveAsync(task.Id, input.FileName, Request.Body, Request.ContentLength, ct);
            db.AnalysisTasks.Add(task);
            await db.SaveChangesAsync(ct);
            queue.Enqueue(task.Id);
            return CreatedAtAction(nameof(Get), new { id = task.Id }, Dto(task));
        }
        catch (UploadRejectedException ex) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
        catch (DbUpdateException)
        {
            files.DeleteTaskFiles(task.Id);
            return Conflict(new { message = "任务保存失败，所选配置可能已被删除，请刷新重试。" });
        }
        catch (OperationCanceledException) { files.DeleteTaskFiles(task.Id); throw; }
        catch (IOException) { return StatusCode(500, new { message = "视频保存失败，请检查磁盘空间或工作目录权限。" }); }
        catch (UnauthorizedAccessException) { return StatusCode(500, new { message = "工作目录没有写入权限。" }); }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await queue.Gate.WaitAsync(ct);
        try
        {
            var task = await db.AnalysisTasks.FindAsync([id], ct);
            if (task is null) return NotFound();
            if (AnalysisProgressWriter.Terminal(task.Status)) return NoContent();
            task.Status = AnalysisTaskStatus.Canceled;
            task.StageDescription = "已取消";
            task.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            queue.Cancel(id);
            await hub.Clients.All.SendAsync("AnalysisProgress",
                new AnalysisProgress(id, task.Status, task.ProgressPercent, task.StageDescription, null), ct);
            return NoContent();
        }
        finally { queue.Gate.Release(); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await queue.Gate.WaitAsync(ct);
        try { return await DeleteCore(id, ct); }
        finally { queue.Gate.Release(); }
    }

    private async Task<IActionResult> DeleteCore(Guid id, CancellationToken ct)
    {
        var task = await db.AnalysisTasks.FindAsync([id], ct);
        if (task is null) return NotFound();
        if (queue.IsActive(id)) return Conflict(new { message = "任务正在退出，请稍后重试删除。" });
        if (task.Status is AnalysisTaskStatus.Preprocessing or AnalysisTaskStatus.Understanding or AnalysisTaskStatus.Combining)
            return Conflict(new { message = "任务正在运行，请先取消。" });
        try { files.DeleteTaskFiles(id); }
        catch (IOException) { return Conflict(new { message = "物料清理失败，请确认文件未被占用后重试；任务记录已保留。" }); }
        catch (UnauthorizedAccessException) { return Conflict(new { message = "物料清理权限不足，任务记录已保留。" }); }
        db.AnalysisTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
