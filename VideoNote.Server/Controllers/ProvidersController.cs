using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Contracts;

namespace VideoNote.Server.Controllers;

[ApiController, Route("api/providers")]
public sealed class ProvidersController(VideoNoteDbContext db, ProviderSecrets secrets) : ControllerBase
{
    private static ProviderDto Dto(Provider p)
    {
        var secret = ProviderSecretReference.Parse(p.ApiKey);
        return new(p.Id, p.Name, p.Protocol.ToWireName(), p.BaseUrl,
            secret.IsConfigured, secret.EnvironmentVariableName, p.TranscriptionModel);
    }

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
        if (await db.SaveConfigurationAsync(ct) is { } conflict)
            return Conflict(new { message = conflict == ConfigurationConflict.Duplicate ? "提供商名称已存在。" : "关联配置已删除，请刷新后重试。" });
        return CreatedAtAction(nameof(Get), new { id = p.Id }, Dto(p));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProviderDto>> Update(Guid id, ProviderInput input, CancellationToken ct)
    {
        var p = await db.Providers.FindAsync([id], ct);
        if (p is null) return NotFound();
        Apply(p, input);
        if (await db.SaveConfigurationAsync(ct) is { } conflict)
            return Conflict(new { message = conflict == ConfigurationConflict.Duplicate ? "提供商名称已存在。" : "关联配置已删除，请刷新后重试。" });
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
        p.Protocol = ProviderProtocolNames.Parse(input.Protocol);
        p.BaseUrl = input.BaseUrl.TrimEnd('/');
        p.TranscriptionModel = string.IsNullOrWhiteSpace(input.TranscriptionModel) ? null : input.TranscriptionModel.Trim();
        p.UpdatedAtUtc = DateTime.UtcNow;
        if (input.ClearApiKey) p.ApiKey = "";
        else if (!string.IsNullOrEmpty(input.ApiKeyEnvironmentVariable)) p.ApiKey = ProviderSecretReference.EnvironmentVariable(input.ApiKeyEnvironmentVariable).ToStorageValue();
        else if (!string.IsNullOrEmpty(input.ApiKey)) p.ApiKey = secrets.Protect(input.ApiKey);
    }

}

