using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data;
using VideoNote.Shared.Contracts;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/settings/conversation")]
public sealed class ConversationSettingsController(VideoNoteDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ConversationSettingsDto> Get(CancellationToken ct) =>
        new((await db.ConversationSettings.AsNoTracking().SingleAsync(ct)).ModelConfigId);

    [HttpPut]
    public async Task<IActionResult> Save(ConversationSettingsDto input, CancellationToken ct)
    {
        if (input.ModelConfigId is { } id && !await db.ModelConfigs.AnyAsync(m => m.Id == id, ct))
            return BadRequest(new { message = "所选对话模型不存在。" });
        (await db.ConversationSettings.SingleAsync(ct)).ModelConfigId = input.ModelConfigId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { message = "对话模型已被删除，请刷新后重选。" }); }
        return NoContent();
    }
}
