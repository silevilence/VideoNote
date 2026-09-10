using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoNote.Server.Analysis;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Transcription;

public sealed class TranscriptionOptions
{
    public Guid? ProviderId { get; set; }
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 600;
    public long MaxAudioBytes { get; set; } = 25 * 1024 * 1024;
}
public sealed record TimedText(double StartSeconds, double EndSeconds, string Text);
public sealed record Transcript(IReadOnlyList<TimedText> Segments)
{
    public string Text => string.Join("\n", Segments.Select(s => $"[{s.StartSeconds:F2}-{s.EndSeconds:F2}] {s.Text}"));
}
// A future local Whisper backend can implement this interface without changing the pipeline.
public interface IAudioTranscriptionBackend
{
    Task<Transcript> TranscribeAsync(string audioPath, Uri endpoint, string model, string apiKey, CancellationToken ct);
}
public interface ITranscriptionService
{
    Task<Transcript> TranscribeAsync(string audioPath, Guid? preferredProviderId, CancellationToken ct);
}
public sealed class TranscriptionService(VideoNoteDbContext db, ProviderSecrets secrets,
    IAudioTranscriptionBackend backend, IOptions<TranscriptionOptions> options) : ITranscriptionService
{
    public async Task<Transcript> TranscribeAsync(string audioPath, Guid? preferredProviderId, CancellationToken ct)
    {
        var settings = options.Value;
        var preferred = preferredProviderId is { } id
            ? await db.Providers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) : null;
        var provider = preferred?.Protocol == ProviderProtocol.OpenAiCompatible &&
            !string.IsNullOrWhiteSpace(preferred.TranscriptionModel) ? preferred :
            settings.ProviderId is { } global
                ? await db.Providers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == global, ct) : preferred;
        var model = provider?.TranscriptionModel;
        if (string.IsNullOrWhiteSpace(model)) model = settings.Model;
        if (provider is null || provider.Protocol != ProviderProtocol.OpenAiCompatible || string.IsNullOrWhiteSpace(model))
            throw new AnalysisException("未配置可用转写服务：请设置 OpenAI 兼容提供商的转写模型，或配置 Transcription:ProviderId 和 Model。");
        string key;
        try { key = secrets.Resolve(provider.ApiKey); }
        catch (Exception) { throw new AnalysisException("转写提供商密钥不可用，请检查密钥或环境变量配置。"); }
        return await backend.TranscribeAsync(audioPath,
            new Uri(provider.BaseUrl.TrimEnd('/') + "/audio/transcriptions"), model, key, ct);
    }
}
