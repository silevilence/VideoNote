using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;
using VideoNote.Server.Transcription;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Analysis;

public sealed record PlannedSegment(double StartSeconds, double EndSeconds, string Text,
    IReadOnlyList<VideoFrame> Frames, string? VideoPath = null, string? AudioPath = null);
public sealed class SegmentPlanner(IFfmpegService media)
{
    public async Task<IReadOnlyList<PlannedSegment>> PlanAsync(AnalysisTask task, PreparedMedia prepared,
        AnalysisBudget budget, PipelineOptions options, CancellationToken ct)
    {
        var plan = new List<PlannedSegment>();
        if (task.Mode == AnalysisMode.DirectVideo)
        {
            var seconds = (budget.Input - 128.0) / options.VideoTokensPerSecond;
            if (seconds < 0.1)
                throw new AnalysisException("模型上下文预算不足以容纳最小视频片段，请增大上下文窗口或调整视频预算配置。");
            foreach (var video in prepared.Videos)
                for (double offset = 0; offset < video.EndSeconds - video.StartSeconds; offset += seconds)
                {
                    var length = Math.Min(seconds, video.EndSeconds - video.StartSeconds - offset);
                    var path = offset == 0 && length >= video.EndSeconds - video.StartSeconds ? video.Path :
                        await media.ExtractVideoRangeAsync(video.Path, task.Id, offset, offset + length, ct);
                    if (path is not null)
                        plan.Add(new(video.StartSeconds + offset, video.StartSeconds + offset + length, "", [], path));
                }
        }
        else if (task.Mode == AnalysisMode.SampledFrames)
        {
            var count = Math.Min(options.MaxImagesPerSegment, (budget.Input - 256) / options.ImageTokenEstimate);
            if (count < 1) throw new AnalysisException("模型上下文不足以容纳一帧图像，请增大上下文窗口。");
            var allFrames = prepared.Frames.ToArray();
            for (var frameOffset = 0; frameOffset < allFrames.Length; frameOffset += count)
            {
                var frames = allFrames.Skip(frameOffset).Take(count).ToArray();
                var start = frames[0].TimestampSeconds;
                var lastIndex = frameOffset + frames.Length - 1;
                var end = lastIndex + 1 < prepared.Frames.Count ? prepared.Frames[lastIndex + 1].TimestampSeconds : prepared.DurationSeconds;
                var text = string.Join("\n", prepared.Subtitles.Where(t => t.EndSeconds > start && t.StartSeconds < end)
                    .Select(t => $"[{t.StartSeconds:F2}-{t.EndSeconds:F2}] {t.Text}"));
                foreach (var part in AnalysisBudget.Split(text, budget.Input - frames.Length * options.ImageTokenEstimate - 128))
                    plan.Add(new(start, end, part, frames));
            }
            // Audio evidence is understood in its own timed map calls, then combined with the visual evidence.
            foreach (var audio in prepared.Audio)
            {
                var seconds = (budget.Input - 128.0) / options.AudioTokensPerSecond;
                for (double offset = 0; offset < audio.EndSeconds - audio.StartSeconds; offset += seconds)
                {
                    var length = Math.Min(seconds, audio.EndSeconds - audio.StartSeconds - offset);
                    var path = offset == 0 && length >= audio.EndSeconds - audio.StartSeconds ? audio.Path :
                        await media.ExtractAudioRangeAsync(audio.Path, task.Id, offset, offset + length, ct);
                    if (path is not null)
                        plan.Add(new(audio.StartSeconds + offset, audio.StartSeconds + offset + length, "", [], AudioPath: path));
                }
            }
        }
        else
        {
            var pending = "";
            double start = 0, end = 0;
            foreach (var subtitle in prepared.Subtitles)
            {
                foreach (var part in AnalysisBudget.Split($"[{subtitle.StartSeconds:F2}-{subtitle.EndSeconds:F2}] {subtitle.Text}\n", budget.Input - 128))
                {
                    if (AnalysisBudget.Size(pending + part) > budget.Input - 128)
                    { plan.Add(new(start, end, pending, [])); pending = ""; }
                    if (pending.Length == 0) start = subtitle.StartSeconds;
                    pending += part; end = subtitle.EndSeconds;
                }
            }
            if (pending.Length > 0) plan.Add(new(start, end, pending, []));
        }
        if (plan.Count == 0) throw new AnalysisException("视频没有可供理解的物料。");
        return plan;
    }
}
