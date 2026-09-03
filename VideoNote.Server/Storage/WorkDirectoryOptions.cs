namespace VideoNote.Server.Storage;

public sealed class WorkDirectoryOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "work";

    public string VideosDirectoryName { get; set; } = "videos";

    public string FramesDirectoryName { get; set; } = "frames";

    public string AudioDirectoryName { get; set; } = "audio";

    public string SubtitlesDirectoryName { get; set; } = "subtitles";
}
