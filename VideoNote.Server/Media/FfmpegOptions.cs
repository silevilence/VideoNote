namespace VideoNote.Server.Media;

public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public double SegmentSeconds { get; set; } = 60;
    public double OverlapSeconds { get; set; } = 5;
    public double FramesPerSecond { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 900;
}
public sealed record MediaInfo(double DurationSeconds, bool HasVideo, bool HasAudio, string? SubtitleCodec, int? SubtitleStreamIndex = null);
public sealed record VideoSegment(string Path, double StartSeconds, double EndSeconds);
public sealed record VideoFrame(string Path, double TimestampSeconds);
