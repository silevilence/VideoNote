using System.Text.Json;
using Microsoft.Extensions.Options;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;
using VideoNote.Server.Transcription;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Analysis;

public sealed record AudioSegment(string Path, double StartSeconds, double EndSeconds);
public sealed record PreparedMedia(double DurationSeconds, IReadOnlyList<VideoSegment> Videos,
    IReadOnlyList<VideoFrame> Frames, IReadOnlyList<AudioSegment> Audio, IReadOnlyList<TimedText> Subtitles);
public interface IMediaPreprocessor
{
    Task<PreparedMedia> PrepareAsync(AnalysisTask task, ModelConfig model, CancellationToken ct);
}
public sealed class MediaPreprocessor(IFfmpegService media, ITranscriptionService transcription,
    WorkDirectoryPaths paths, AnalysisProgressWriter progress, IOptions<FfmpegOptions> options) : IMediaPreprocessor
{
    public async Task<PreparedMedia> PrepareAsync(AnalysisTask task, ModelConfig model, CancellationToken ct)
    {
        var source = Path.GetFullPath(Path.Combine(paths.Root, task.VideoPath));
        var directory = Path.Combine(paths.Videos, task.Id.ToString("N")) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new AnalysisException("视频路径不属于当前任务。");
        foreach (var root in new[] { source, Path.Combine(paths.Frames, task.Id.ToString("N")),
            Path.Combine(paths.Audio, task.Id.ToString("N")), Path.Combine(paths.Subtitles, task.Id.ToString("N")) })
            for (FileSystemInfo? current = Directory.Exists(root) ? new DirectoryInfo(root) : new FileInfo(root); current is not null;
                current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
                if ((current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0))
                    throw new AnalysisException("任务目录不能包含符号链接或目录联接。");
        try
        {
            await Stage(2, "读取视频信息");
            var info = await media.ProbeAsync(source, ct);
            if (!info.HasVideo) throw new AnalysisException("源文件不包含视频流。");
            IReadOnlyList<VideoSegment> videos = [];
            IReadOnlyList<VideoFrame> frames = [];
            var audio = new List<AudioSegment>();
            var subtitles = new List<TimedText>();
            switch (task.Mode)
            {
                case AnalysisMode.DirectVideo:
                    await Stage(5, "正在切分视频");
                    videos = await media.SegmentAsync(source, task.Id, ct);
                    await Stage(24, $"已生成 {videos.Count} 个视频分段");
                    break;
                case AnalysisMode.SampledFrames:
                    await Stage(5, "正在抽取带时间戳的视频帧");
                    frames = await media.ExtractFramesAsync(source, task.Id, ct);
                    await Stage(12, $"已抽取 {frames.Count} 帧");
                    if (info.HasAudio) await Audio(model.SupportsAudio);
                    else await Stage(24, "视频无音轨，将仅依据画面理解");
                    break;
                case AnalysisMode.Subtitles:
                    await Stage(5, "正在提取内嵌软字幕");
                    var srt = await media.ExtractSubtitlesAsync(source, task.Id, ct);
                    if (srt is not null) subtitles.AddRange(SubtitleParser.Parse(await File.ReadAllTextAsync(srt, ct)));
                    if (subtitles.Count == 0)
                    {
                        if (!info.HasAudio) throw new AnalysisException("视频没有可用软字幕，也没有可转写的音轨。");
                        await Stage(10, "无有效软字幕，改用音频转写");
                        await Audio(false);
                    }
                    break;
                default: throw new AnalysisException("不支持的理解模式。");
            }
            var result = new PreparedMedia(info.DurationSeconds, videos, frames, audio, subtitles);
            var manifestDirectory = Directory.CreateDirectory(Path.Combine(paths.Subtitles, task.Id.ToString("N"))).FullName;
            await File.WriteAllTextAsync(Path.Combine(manifestDirectory, "materials.json"), JsonSerializer.Serialize(result), ct);
            await Stage(25, "预处理完成");
            return result;

            Task Stage(int percent, string description) =>
                progress.UpdateAsync(task.Id, AnalysisTaskStatus.Preprocessing, percent, description, ct);
            async Task Audio(bool direct)
            {
                // Bound every upload independently of the total video duration; timestamps remain video-relative.
                var seconds = options.Value.SegmentSeconds;
                var count = (int)Math.Ceiling(info.DurationSeconds / seconds);
                for (var index = 0; index < count; index++)
                {
                    var start = index * seconds;
                    var end = Math.Min(start + seconds, info.DurationSeconds);
                    await Stage(12 + (int)(12.0 * index / count),
                        direct ? $"提取音频分段 {index + 1}/{count}（音频直传）" : $"转写音频分段 {index + 1}/{count}");
                    var path = await media.ExtractAudioRangeAsync(source, task.Id, start, end, ct);
                    if (path is null) continue;
                    if (direct) audio.Add(new(path, start, end));
                    else
                    {
                        var transcript = await transcription.TranscribeAsync(path, model.ProviderId, ct);
                        foreach (var segment in transcript.Segments)
                        {
                            if (segment.StartSeconds >= end - start || segment.EndSeconds > end - start + 1)
                                throw new AnalysisException("转写时间戳超出音频分段范围。");
                            subtitles.Add(new(start + segment.StartSeconds,
                                Math.Min(end, start + segment.EndSeconds), segment.Text));
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (AnalysisException) { throw; }
        catch (TimeoutException) { throw new AnalysisException("视频预处理超时，请调整 FFmpeg 超时配置。"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { throw new AnalysisException("视频预处理失败，请检查媒体文件、FFmpeg 配置、字幕格式和工作目录权限。"); }
    }
}
