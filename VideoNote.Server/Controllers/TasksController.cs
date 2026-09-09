using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Storage;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/tasks")]
public sealed class TasksController(VideoNoteDbContext db, VideoFileStore files, IOptions<UploadOptions> options) : ControllerBase
{
    private static TaskDto Dto(AnalysisTask t, bool includeContent = false) => new(t.Id, t.OriginalFileName, t.Mode, t.Status, t.StageDescription, t.CreatedAtUtc, t.ModelConfigId, t.PromptTemplateId, includeContent ? t.PromptContentSnapshot : null);
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
        if (input.ModelConfigId.HasValue && !await db.ModelConfigs.AnyAsync(m => m.Id == input.ModelConfigId, ct))
            return BadRequest(new { message = "所选模型不存在。" });
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
            StageDescription = "已保存：功能验证任务，尚未接入分析管线。"
        };
        try
        {
            task.VideoPath = await files.SaveAsync(task.Id, input.FileName, Request.Body, Request.ContentLength, ct);
            db.AnalysisTasks.Add(task);
            await db.SaveChangesAsync(ct);
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var task = await db.AnalysisTasks.FindAsync([id], ct);
        if (task is null) return NotFound();
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
