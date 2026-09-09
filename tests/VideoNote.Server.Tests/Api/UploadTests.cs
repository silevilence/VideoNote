using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;
using VideoNote.Shared.Contracts;

namespace VideoNote.Server.Tests.Api;

public sealed class UploadTests
{
    [Fact]
    public async Task Large_valid_video_streams_intact_and_deletion_cleans_all_materials()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromMinutes(3);
        var paths = app.Services.GetRequiredService<WorkDirectoryPaths>();
        var source = Path.Combine(paths.Root, "large-test.mp4");
        var runner = new MediaProcessRunner(Options.Create(new FfmpegOptions()));
        await runner.RunAsync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi",
            "-i", "color=red:size=64x64:rate=1:duration=1", "-c:v", "libx264", source], CancellationToken.None);
        // A standards-compliant MP4 free box adds 320 MiB without allocating a matching byte array.
        await using (var padding = new FileStream(source, FileMode.Open, FileAccess.Write))
        {
            padding.Seek(0, SeekOrigin.End);
            var header = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(header, 320 * 1024 * 1024);
            "free"u8.CopyTo(header.AsSpan(4));
            await padding.WriteAsync(header);
            padding.SetLength(padding.Length + 320L * 1024 * 1024 - 8);
        }
        await using var input = File.OpenRead(source);
        using var content = new StreamContent(input);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await client.PostAsync("/api/tasks?fileName=large.mp4&mode=SampledFrames", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        string savedPath;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var saved = await db.AnalysisTasks.SingleAsync();
            savedPath = Path.Combine(paths.Root, saved.VideoPath);
            db.ConversationMessages.Add(new ConversationMessage { AnalysisTaskId = task.Id, Content = "test" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(input.Length, new FileInfo(savedPath).Length);
        input.Position = 0;
        await using (var saved = File.OpenRead(savedPath))
            Assert.Equal(await SHA256.HashDataAsync(input), await SHA256.HashDataAsync(saved));
        foreach (var root in new[] { paths.Frames, paths.Audio, paths.Subtitles })
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, task.Id.ToString("N"))).FullName;
            await File.WriteAllTextAsync(Path.Combine(directory, "material.txt"), "test");
        }
        Assert.Single((await client.GetFromJsonAsync<TaskDto[]>("/api/tasks"))!);
        Assert.Equal(task.Id, (await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}"))!.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        foreach (var root in new[] { paths.Videos, paths.Frames, paths.Audio, paths.Subtitles })
            Assert.False(Directory.Exists(Path.Combine(root, task.Id.ToString("N"))));
        using (var scope = app.Services.CreateScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>().ConversationMessages.ToListAsync());
        await input.DisposeAsync();
        File.Delete(source);
    }

    [Theory]
    [InlineData("bad.exe", 10, false, 400)]
    [InlineData("../bad.mp4", 10, false, 400)]
    [InlineData("empty.mp4", 0, false, 400)]
    [InlineData("large.mp4", 2048, false, 413)]
    [InlineData("chunked.mp4", 2048, true, 413)]
    public async Task Invalid_uploads_leave_no_task_or_partial_file(string name, int size, bool chunked, int status)
    {
        await using var app = new ApiFactory(new() { ["Upload:MaxBytes"] = "1024" });
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        using HttpContent content = chunked ? new UnknownLengthContent(new byte[size]) : new ByteArrayContent(new byte[size]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await client.PostAsync("/api/tasks?fileName=" + Uri.EscapeDataString(name), content);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<TaskDto[]>("/api/tasks"))!);
        Assert.Empty(Directory.EnumerateFiles(app.Services.GetRequiredService<WorkDirectoryPaths>().Videos, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Interrupted_stream_removes_temporary_output()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoNote-upload-tests", Guid.NewGuid().ToString("N"));
        var paths = WorkDirectoryPaths.Create(root, new WorkDirectoryOptions());
        new WorkDirectoryInitializer(paths).Initialize();
        var store = new VideoFileStore(paths, Options.Create(new UploadOptions { AllowedExtensions = [".mp4"] }));
        try
        {
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Guid.NewGuid(), "test.mp4", new BrokenStream(), null, default));
            Assert.Empty(Directory.EnumerateFileSystemEntries(paths.Videos));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.SaveAsync(Guid.NewGuid(), "test.mp4", new MemoryStream(new byte[100]), 100, cancellation.Token));
            Assert.Empty(Directory.EnumerateFileSystemEntries(paths.Videos));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Simulated connection failure."));
    }
}
