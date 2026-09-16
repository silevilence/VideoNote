using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Text.Json;
using VideoNote.Server.Analysis;
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
    AnalysisQueue queue, AnalysisProgressWriter progress, VideoNote.Server.Conversation.ConversationRuns conversations) : ControllerBase
{
    private static TaskDto Dto(AnalysisTask t, bool includeContent = false) => new(t.Id, t.OriginalFileName, t.Mode, t.Status, t.StageDescription, t.CreatedAtUtc, t.ModelConfigId, t.PromptTemplateId, includeContent ? t.PromptContentSnapshot : null,
        t.ProgressPercent, t.ErrorMessage, includeContent ? t.ResultText : null, t.StartedAtUtc, t.CompletedAtUtc, includeContent ? JsonSerializer.Deserialize<List<SegmentResultDto>>(t.SegmentResultsJson) : null,
        includeContent ? JsonSerializer.Deserialize<List<TaskLogDto>>(t.LogsJson) : null);
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
        => await CreateCore(input, Request.Body, Request.ContentLength, ct);

    // Read metadata first, then stream the file directly; never bind IFormFile or buffer the video.
    [HttpPost("upload"), Consumes("multipart/form-data"), DisableRequestSizeLimit, StreamingUpload]
    public async Task<ActionResult<TaskDto>> Upload(CancellationToken ct)
    {
        try
        {
            var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(Request.ContentType!).Boundary).Value;
            if (string.IsNullOrEmpty(boundary) || boundary.Length > 128) return BadRequest(new { message = "上传边界无效。" });
            var reader = new MultipartReader(boundary, Request.Body) { BodyLengthLimit = 128 * 1024 };
            var metadata = await reader.ReadNextSectionAsync(ct);
            if (metadata?.AsFormDataSection()?.Name != "metadata") return BadRequest(new { message = "上传缺少任务参数。" });
            var input = await JsonSerializer.DeserializeAsync<CreateTaskInput>(metadata.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
            if (input is null || !TryValidateModel(input)) return BadRequest(new { message = "任务参数无效，提示词最多 16000 字符。" });
            reader.BodyLengthLimit = options.Value.MaxBytes;
            var file = await reader.ReadNextSectionAsync(ct);
            if (file?.AsFileSection()?.Name != "video") return BadRequest(new { message = "上传缺少视频。" });
            return await CreateCore(input, file.Body, null, ct);
        }
        catch (JsonException) { return BadRequest(new { message = "任务参数格式无效。" }); }
        catch (InvalidDataException) { return BadRequest(new { message = "上传格式无效或内容超过限制。" }); }
    }

    private async Task<ActionResult<TaskDto>> CreateCore(CreateTaskInput input, Stream body, long? length, CancellationToken ct)
    {
        if (input.ModelConfigId is null) return BadRequest(new { message = "请选择分析模型。" });
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
            PromptContentSnapshot = input.PromptContent is null ? prompt?.Content : string.IsNullOrWhiteSpace(input.PromptContent) ? null : input.PromptContent.Trim(),
            StageDescription = "排队等待分析"
        };
        try
        {
            task.VideoPath = await files.SaveAsync(task.Id, input.FileName, body, length, ct);
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
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        await progress.CancelAsync(id, ct) ? NoContent() : NotFound();

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
        if (conversations.IsActive(id)) return Conflict(new { message = "正在生成对话回复，请停止或等待回复结束后删除。" });
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
