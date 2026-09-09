using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Contracts;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/prompts")]
public sealed class PromptsController(VideoNoteDbContext db) : ControllerBase
{
    private static PromptDto Dto(PromptTemplate p) => new(p.Id, p.Name, p.Content, p.IsBuiltIn, p.UpdatedAtUtc);
    [HttpGet]
    public async Task<IEnumerable<PromptDto>> List(CancellationToken ct) =>
        (await db.PromptTemplates.AsNoTracking().OrderByDescending(p => p.IsBuiltIn).ThenBy(p => p.Name).ToListAsync(ct)).Select(Dto);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PromptDto>> Get(Guid id, CancellationToken ct) =>
        await db.PromptTemplates.FindAsync([id], ct) is { } p ? Ok(Dto(p)) : NotFound();

    [HttpPost]
    public async Task<ActionResult<PromptDto>> Create(PromptInput input, CancellationToken ct)
    {
        var p = new PromptTemplate { Name = input.Name.Trim(), Content = input.Content };
        db.PromptTemplates.Add(p); await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, Dto(p));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PromptDto>> Update(Guid id, PromptInput input, CancellationToken ct)
    {
        var p = await db.PromptTemplates.FindAsync([id], ct);
        if (p is null) return NotFound();
        if (p.IsBuiltIn) return Conflict(new { message = "内置模板只读，请复制后编辑。" });
        p.Name = input.Name.Trim(); p.Content = input.Content; p.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(Dto(p));
    }

    [HttpPost("{id:guid}/copy")]
    public async Task<ActionResult<PromptDto>> Copy(Guid id, CancellationToken ct)
    {
        var source = await db.PromptTemplates.FindAsync([id], ct);
        if (source is null) return NotFound();
        var copy = source.CreateCopy();
        db.PromptTemplates.Add(copy); await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = copy.Id }, Dto(copy));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await db.PromptTemplates.FindAsync([id], ct);
        if (p is null) return NotFound();
        if (p.IsBuiltIn) return Conflict(new { message = "内置模板不可删除。" });
        db.PromptTemplates.Remove(p); await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
