using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VideoNote.Server.Storage;

namespace VideoNote.Server.Media;

public interface IFfmpegService
{
    Task<MediaInfo> ProbeAsync(string source, CancellationToken ct = default);
    Task<IReadOnlyList<VideoSegment>> SegmentAsync(string source, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<VideoFrame>> ExtractFramesAsync(string source, Guid taskId, CancellationToken ct = default);
    Task<string> ExtractAudioAsync(string source, Guid taskId, string format = "wav", CancellationToken ct = default);
    Task<string> ExtractAudioRangeAsync(string source, Guid taskId, double start, double end, CancellationToken ct = default);
    Task<string?> ExtractSubtitlesAsync(string source, Guid taskId, CancellationToken ct = default);
}

public sealed class FfmpegService(WorkDirectoryPaths paths, MediaProcessRunner runner, IOptions<FfmpegOptions> options) : IFfmpegService
{
    private readonly FfmpegOptions settings = options.Value;
    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    public async Task<MediaInfo> ProbeAsync(string source, CancellationToken ct = default)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("源视频不存在。", source);
        var json = await runner.RunAsync(settings.FfprobePath,
            ["-v", "error", "-show_format", "-show_streams", "-of", "json", source], ct);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (!doc.RootElement.TryGetProperty("format", out var format) ||
            !format.TryGetProperty("duration", out var durationElement) ||
            !double.TryParse(durationElement.GetString(), CultureInfo.InvariantCulture, out var duration) ||
            !double.IsFinite(duration) || duration <= 0)
            throw new InvalidOperationException("无法确定媒体时长。");
        var subtitle = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "subtitle");
        return new(duration, streams.Any(s => s.GetProperty("codec_type").GetString() == "video"),
            streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"),
            subtitle.ValueKind == JsonValueKind.Undefined ? null : subtitle.GetProperty("codec_name").GetString());
    }

    public async Task<IReadOnlyList<VideoSegment>> SegmentAsync(string source, Guid taskId, CancellationToken ct = default)
    {
        var info = await ProbeAsync(source, ct);
        if (!info.HasVideo) throw new InvalidOperationException("源文件不包含视频流。");
        var directory = Directory.CreateDirectory(Path.Combine(paths.Videos, taskId.ToString("N"), "segments")).FullName;
        var segments = new List<VideoSegment>();
        for (double start = 0; start < info.DurationSeconds; start += settings.SegmentSeconds - settings.OverlapSeconds)
        {
            var end = Math.Min(start + settings.SegmentSeconds, info.DurationSeconds);
            var output = Path.Combine(directory, $"segment_{segments.Count:D5}.mp4");
            await EncodeAsync(["-ss", Number(start), "-i", source, "-t", Number(end - start),
                "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-c:v", "libx264", "-preset", "veryfast",
                "-c:a", "aac", "-movflags", "+faststart"], output, ct);
            segments.Add(new(output, start, end));
            if (end >= info.DurationSeconds) break;
        }
        return segments;
    }

    public async Task<IReadOnlyList<VideoFrame>> ExtractFramesAsync(string source, Guid taskId, CancellationToken ct = default)
    {
        var info = await ProbeAsync(source, ct);
        if (!info.HasVideo) throw new InvalidOperationException("源文件不包含视频流。");
        // A fresh operation directory prevents stale frames surviving retries with a shorter input.
        var directory = Directory.CreateDirectory(Path.Combine(paths.Frames, taskId.ToString("N"), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await runner.RunAsync(settings.FfmpegPath, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", source, "-map", "0:v:0", "-vf", $"fps={Number(settings.FramesPerSecond)}:start_time=0",
            "-q:v", "2", Path.Combine(directory, "frame_%010d.jpg")], ct);
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
        var frames = new List<VideoFrame>();
        foreach (var file in Directory.GetFiles(directory, "frame_*.jpg").Order(StringComparer.Ordinal))
        {
            var time = frames.Count / settings.FramesPerSecond;
            var output = Path.Combine(directory, $"at_{(long)Math.Round(time * 1000):D12}ms.jpg");
            File.Move(file, output);
            frames.Add(new(output, time));
        }
        return frames;
    }

    public async Task<string> ExtractAudioAsync(string source, Guid taskId, string format = "wav", CancellationToken ct = default)
    {
        if (format is not ("wav" or "mp3")) throw new ArgumentException("音频格式仅支持 wav 或 mp3。", nameof(format));
        if (!(await ProbeAsync(source, ct)).HasAudio) throw new InvalidOperationException("源视频不包含音轨。");
        var output = Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Audio, taskId.ToString("N"))).FullName, "audio." + format);
        await EncodeAsync(["-i", source, "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a",
            format == "wav" ? "pcm_s16le" : "libmp3lame"], output, ct);
        return output;
    }

    public async Task<string> ExtractAudioRangeAsync(string source, Guid taskId, double start, double end, CancellationToken ct = default)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
            throw new ArgumentException("音频分段范围无效。");
        var directory = Directory.CreateDirectory(Path.Combine(paths.Audio, taskId.ToString("N"))).FullName;
        var output = Path.Combine(directory, $"audio_{(long)(start * 1000):D12}_{(long)(end * 1000):D12}.mp3");
        await EncodeAsync(["-ss", Number(start), "-i", source, "-t", Number(end - start),
            "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame"], output, ct);
        return output;
    }

    public async Task<string?> ExtractSubtitlesAsync(string source, Guid taskId, CancellationToken ct = default)
    {
        var info = await ProbeAsync(source, ct);
        if (info.SubtitleCodec is null) return null;
        if (info.SubtitleCodec is not ("subrip" or "ass" or "ssa" or "webvtt" or "mov_text" or "text"))
            throw new InvalidOperationException("内嵌字幕是位图或不支持的格式，无法提取为文本字幕。");
        var output = Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Subtitles, taskId.ToString("N"))).FullName, "subtitles.srt");
        await EncodeAsync(["-i", source, "-map", "0:s:0", "-c:s", "srt"], output, ct);
        return output;
    }

    private async Task EncodeAsync(IEnumerable<string> arguments, string output, CancellationToken ct)
    {
        // Keep a previous successful artifact intact until the replacement is complete.
        var temporary = Path.Combine(Path.GetDirectoryName(output)!,
            Guid.NewGuid().ToString("N") + ".partial" + Path.GetExtension(output));
        try
        {
            await runner.RunAsync(settings.FfmpegPath,
                new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" }.Concat(arguments).Append(temporary), ct);
            File.Move(temporary, output, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
