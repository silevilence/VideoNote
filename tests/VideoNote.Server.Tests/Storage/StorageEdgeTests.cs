using Microsoft.Extensions.Options;
using VideoNote.Server.Storage;

namespace VideoNote.Server.Tests.Storage;

public sealed class StorageEdgeTests
{
    [Theory]
    [InlineData("", "videos")]
    [InlineData("../escape", "videos")]
    [InlineData("work", "../escape")]
    [InlineData("work", "")]
    public void Invalid_work_paths_are_rejected(string root, string child)
    {
        Assert.Throws<InvalidOperationException>(() => WorkDirectoryPaths.Create(
            Path.GetTempPath(), new WorkDirectoryOptions { RootPath = root, VideosDirectoryName = child }));
    }

    [Fact]
    public async Task Duplicate_upload_id_and_truncated_stream_preserve_existing_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoNote-storage-tests", Guid.NewGuid().ToString("N"));
        var paths = WorkDirectoryPaths.Create(root, new WorkDirectoryOptions());
        new WorkDirectoryInitializer(paths).Initialize();
        var store = new VideoFileStore(paths, Options.Create(new UploadOptions()));
        var id = Guid.NewGuid();
        try
        {
            var stored = await store.SaveAsync(id, "test.MP4", new MemoryStream([1, 2, 3]), 3, default);
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(id, "test.mp4", new MemoryStream([4, 5]), 2, default));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(paths.Root, stored)));
            Assert.Single(Directory.GetFiles(Path.Combine(paths.Videos, id.ToString("N"))));
            await Assert.ThrowsAsync<UploadRejectedException>(() => store.SaveAsync(Guid.NewGuid(), "truncated.mp4", new MemoryStream([1]), 100, default));
            await Assert.ThrowsAsync<UploadRejectedException>(() => store.SaveAsync(Guid.NewGuid(), "empty.mp4", new MemoryStream(), null, default));
            Assert.Single(Directory.GetDirectories(paths.Videos));
        }
        finally { Directory.Delete(root, true); }
    }
}
