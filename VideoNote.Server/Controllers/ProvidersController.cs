using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/providers")]
public sealed class ProvidersController(VideoNoteDbContext db, ProviderSecrets secrets) : ControllerBase
{
    private static ProviderDto Dto(Provider p) => new(p.Id, p.Name,
        p.Protocol == ProviderProtocol.OpenAiCompatible ? "openai-compatible" : "gemini-native",
        p.BaseUrl, p.ApiKey.Length > 0, ProviderSecrets.EnvironmentName(p.ApiKey), p.TranscriptionModel);

    [HttpGet]
    public async Task<IEnumerable<ProviderDto>> List(CancellationToken ct) =>
        (await db.Providers.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct)).Select(Dto);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProviderDto>> Get(Guid id, CancellationToken ct) =>
        await db.Providers.FindAsync([id], ct) is { } p ? Ok(Dto(p)) : NotFound();

    [HttpPost]
    public async Task<ActionResult<ProviderDto>> Create(ProviderInput input, CancellationToken ct)
    {
        var p = new Provider();
        Apply(p, input);
        db.Providers.Add(p);
        if (!await Save(ct)) return Conflict(new { message = "提供商名称已存在。" });
        return CreatedAtAction(nameof(Get), new { id = p.Id }, Dto(p));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProviderDto>> Update(Guid id, ProviderInput input, CancellationToken ct)
    {
        var p = await db.Providers.FindAsync([id], ct);
        if (p is null) return NotFound();
        Apply(p, input);
        if (!await Save(ct)) return Conflict(new { message = "提供商名称已存在。" });
        return Ok(Dto(p));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await db.Providers.FindAsync([id], ct);
        if (p is null) return NotFound();
        db.Providers.Remove(p);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Apply(Provider p, ProviderInput input)
    {
        p.Name = input.Name.Trim();
        p.Protocol = input.Protocol == "openai-compatible" ? ProviderProtocol.OpenAiCompatible : ProviderProtocol.GeminiNative;
        p.BaseUrl = input.BaseUrl.TrimEnd('/');
        p.TranscriptionModel = string.IsNullOrWhiteSpace(input.TranscriptionModel) ? null : input.TranscriptionModel.Trim();
        p.UpdatedAtUtc = DateTime.UtcNow;
        if (input.ClearApiKey) p.ApiKey = "";
        else if (!string.IsNullOrEmpty(input.ApiKeyEnvironmentVariable)) p.ApiKey = "env:" + input.ApiKeyEnvironmentVariable;
        else if (!string.IsNullOrEmpty(input.ApiKey)) p.ApiKey = secrets.Protect(input.ApiKey);
    }

    private async Task<bool> Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 })
        { return false; }
    }
}

