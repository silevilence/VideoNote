using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VideoNote.Server.Analysis;
namespace VideoNote.Server.Transcription;

public sealed class OpenAiTranscriptionBackend(HttpClient http, IOptions<TranscriptionOptions> options) : IAudioTranscriptionBackend
{
    public async Task<Transcript> TranscribeAsync(string audioPath, Uri endpoint, string model, string apiKey, CancellationToken ct)
    {
        var settings = options.Value;
        if (!File.Exists(audioPath)) throw new AnalysisException("待转写的音频文件不存在。");
        await using var stream = new FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == 0 || stream.Length > settings.MaxAudioBytes)
            throw new AnalysisException("音频为空或超过转写大小上限，请减小音频分段时长或调整 Transcription:MaxAudioBytes。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent(model), "model");
        body.Add(new StringContent("verbose_json"), "response_format");
        body.Add(new StringContent("segment"), "timestamp_granularities[]");
        var audio = new StreamContent(stream);
        audio.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(audioPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg" : "audio/wav");
        body.Add(audio, "file", Path.GetFileName(audioPath));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = body };
        if (!string.IsNullOrEmpty(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new AnalysisException($"转写端点返回 HTTP {(int)response.StatusCode}，请检查服务地址、密钥、模型和 verbose_json 时间戳支持。");
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, timeout.Token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return Parse(json.RootElement);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new AnalysisException("转写请求超时，请检查服务或调整 Transcription:TimeoutSeconds。"); }
        catch (HttpRequestException) { throw new AnalysisException("无法连接转写端点或响应过大，请检查地址与网络。"); }
        catch (JsonException) { throw new AnalysisException("转写端点返回无效 JSON，要求 verbose_json 和分段时间戳。"); }
    }

    public static Transcript Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("segments", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            throw new AnalysisException("转写响应缺少 segments 时间戳，请使用支持 verbose_json 的转写模型。");
        var segments = new List<TimedText>();
        double previousStart = -1;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Number || !start.TryGetDouble(out var a) ||
                !item.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.Number || !end.TryGetDouble(out var b) ||
                !item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String ||
                !double.IsFinite(a) || !double.IsFinite(b) || a < 0 || b <= a || a < previousStart)
                throw new AnalysisException("转写响应包含无效或无序的时间戳。");
            previousStart = a;
            var value = text.GetString()!.Trim();
            if (value.Length > 0) segments.Add(new(a, b, value));
        }
        if (segments.Count == 0) throw new AnalysisException("音频未识别出有效语音文本。");
        return new(segments);
    }
}
