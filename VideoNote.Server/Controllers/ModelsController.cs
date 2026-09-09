using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Contracts;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/models")]
public sealed class ModelsController(VideoNoteDbContext db) : ControllerBase
{
    private static ModelDto Dto(ModelConfig m) => new()
    {
        Id = m.Id,
        ProviderId = m.ProviderId,
        ModelId = m.ModelId,
        ContextWindow = m.ContextWindow,
        SupportsReasoning = m.SupportsReasoning,
        SupportsToolCalling = m.SupportsToolCalling,
        SupportsStreaming = m.SupportsStreaming,
        SupportsImage = m.SupportsImage,
        SupportsAudio = m.SupportsAudio,
        SupportsVideo = m.SupportsVideo
    };

    [HttpGet]
    public async Task<IEnumerable<ModelDto>> List(Guid? providerId, CancellationToken ct) =>
        (await db.ModelConfigs.AsNoTracking().Where(m => providerId == null || m.ProviderId == providerId)
            .OrderBy(m => m.ModelId).ToListAsync(ct)).Select(Dto);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ModelDto>> Get(Guid id, CancellationToken ct) =>
        await db.ModelConfigs.FindAsync([id], ct) is { } m ? Ok(Dto(m)) : NotFound();

    [HttpPost]
    public async Task<ActionResult<ModelDto>> Create(ModelInput input, CancellationToken ct)
    {
        if (!await db.Providers.AnyAsync(p => p.Id == input.ProviderId, ct))
            return BadRequest(new { message = "提供商不存在。" });
        var m = new ModelConfig();
        Apply(m, input);
        db.ModelConfigs.Add(m);
        if (!await Save(ct)) return Conflict(new { message = "该提供商下已存在同名模型，或提供商已删除。" });
        return CreatedAtAction(nameof(Get), new { id = m.Id }, Dto(m));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ModelDto>> Update(Guid id, ModelInput input, CancellationToken ct)
    {
        var m = await db.ModelConfigs.FindAsync([id], ct);
        if (m is null) return NotFound();
        if (!await db.Providers.AnyAsync(p => p.Id == input.ProviderId, ct))
            return BadRequest(new { message = "提供商不存在。" });
        Apply(m, input);
        if (!await Save(ct)) return Conflict(new { message = "该提供商下已存在同名模型，或提供商已删除。" });
        return Ok(Dto(m));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var m = await db.ModelConfigs.FindAsync([id], ct);
        if (m is null) return NotFound();
        db.ModelConfigs.Remove(m);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(ModelConfig m, ModelInput i)
    {
        m.ProviderId = i.ProviderId; m.ModelId = i.ModelId.Trim(); m.ContextWindow = i.ContextWindow;
        m.SupportsReasoning = i.SupportsReasoning; m.SupportsToolCalling = i.SupportsToolCalling;
        m.SupportsStreaming = i.SupportsStreaming; m.SupportsImage = i.SupportsImage;
        m.SupportsAudio = i.SupportsAudio; m.SupportsVideo = i.SupportsVideo;
    }
    private async Task<bool> Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        { return false; }
    }
}

