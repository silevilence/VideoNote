using System.Buffers;
using Microsoft.Extensions.Options;

namespace VideoNote.Server.Storage;

public sealed class VideoFileStore(WorkDirectoryPaths paths, IOptions<UploadOptions> options)
{
    public void Validate(string fileName, long? length)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260 ||
            fileName.IndexOfAny(['/', '\\']) >= 0 || fileName.Any(char.IsControl) ||
            !options.Value.AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            throw new UploadRejectedException("文件名或视频扩展名不受支持。", 400);
        if (length > options.Value.MaxBytes) throw new UploadRejectedException("视频超过上传大小上限。", 413);
        if (length == 0) throw new UploadRejectedException("视频不能为空。", 400);
    }

    public async Task<string> SaveAsync(Guid id, string fileName, Stream input, long? length, CancellationToken ct)
    {
        Validate(fileName, length);
        var directory = TaskDirectories(id).First();
        EnsureNoLinks(directory);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".uploading");
        var final = Path.Combine(directory, "source" + Path.GetExtension(fileName).ToLowerInvariant());
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long total = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) != 0)
                {
                    total += read;
                    if (total > options.Value.MaxBytes) throw new UploadRejectedException("视频超过上传大小上限。", 413);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (total == 0 || (length.HasValue && total != length.Value))
                    throw new UploadRejectedException("上传内容为空或不完整。", 400);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, final);
            return Path.GetRelativePath(paths.Root, final);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(final)) File.Delete(final);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public void DeleteTaskFiles(Guid id)
    {
        var directories = TaskDirectories(id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Validate all trees before removing any file. Do not follow junctions/symbolic links.
        foreach (var directory in directories) { EnsureNoLinks(directory); ValidateTree(directory); }
        foreach (var directory in directories)
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private IEnumerable<string> TaskDirectories(Guid id) =>
        new[] { paths.Videos, paths.Frames, paths.Audio, paths.Subtitles }.Select(root => Path.Combine(root, id.ToString("N")));

    private static void EnsureNoLinks(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("工作目录不能包含符号链接或目录联接。");
    }
    private static void ValidateTree(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("任务物料不能包含符号链接。");
            if (entry is DirectoryInfo child) ValidateTree(child.FullName);
        }
    }
}
