namespace VideoNote.Server.Storage;

public sealed class WorkDirectoryInitializer(WorkDirectoryPaths paths)
{
    public void Initialize()
    {
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.Videos);
        Directory.CreateDirectory(paths.Frames);
        Directory.CreateDirectory(paths.Audio);
        Directory.CreateDirectory(paths.Subtitles);
        Directory.CreateDirectory(paths.Keys);
    }
}
