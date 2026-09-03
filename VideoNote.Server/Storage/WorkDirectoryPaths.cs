namespace VideoNote.Server.Storage;

public sealed record WorkDirectoryPaths(
    string Root,
    string Videos,
    string Frames,
    string Audio,
    string Subtitles,
    string Keys)
{
    public static WorkDirectoryPaths Create(
        string contentRootPath,
        WorkDirectoryOptions options)
    {
        var root = ResolveRoot(contentRootPath, options.RootPath);

        return new WorkDirectoryPaths(
            root,
            ResolveChild(root, options.VideosDirectoryName),
            ResolveChild(root, options.FramesDirectoryName),
            ResolveChild(root, options.AudioDirectoryName),
            ResolveChild(root, options.SubtitlesDirectoryName),
            ResolveChild(root, "keys"));
    }

    private static string ResolveRoot(string contentRootPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || Path.IsPathRooted(rootPath))
        {
            throw new InvalidOperationException("The storage root must be a relative path.");
        }

        var contentRoot = Path.GetFullPath(contentRootPath);
        var root = Path.GetFullPath(Path.Combine(contentRoot, rootPath));
        var relativePath = Path.GetRelativePath(contentRoot, root);
        if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The storage root must stay under the server content root.");
        }

        return root;
    }

    private static string ResolveChild(string root, string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName) || Path.IsPathRooted(directoryName))
        {
            throw new InvalidOperationException("Storage child directory names must be relative paths.");
        }

        var path = Path.GetFullPath(Path.Combine(root, directoryName));
        var relativePath = Path.GetRelativePath(root, path);
        if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage child directories must stay under the storage root.");
        }

        return path;
    }
}
