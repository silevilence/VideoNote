using Microsoft.Extensions.Options;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;

namespace VideoNote.Server.Tests.Media;

public sealed class MediaFailureTests
{
    [Fact]
    public async Task Timeout_and_cancellation_stop_real_child_process()
    {
        string[] arguments = ["-hide_banner", "-loglevel", "error", "-nostdin", "-re", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "30", "-f", "null", "-"];
        var runner = new MediaProcessRunner(Options.Create(new FfmpegOptions { TimeoutSeconds = 1 }));
        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync("ffmpeg", arguments, default));
        runner = new MediaProcessRunner(Options.Create(new FfmpegOptions()));
        using var cancel = new CancellationTokenSource(300);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync("ffmpeg", arguments, cancel.Token));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync("ffmpeg", ["-not-a-real-argument"], default));
        Assert.Contains("退出码", error.Message);
    }

    [Fact]
    public async Task Failed_replacement_keeps_previous_artifact_and_failed_frames_leave_no_partial_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoNote-media-tests", Guid.NewGuid().ToString("N"));
        var paths = WorkDirectoryPaths.Create(root, new WorkDirectoryOptions());
        new WorkDirectoryInitializer(paths).Initialize();
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/sample.mkv"));
        var id = Guid.NewGuid();
        var audio = Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Audio, id.ToString("N"))).FullName, "audio.wav");
        await File.WriteAllTextAsync(audio, "previous successful result");
        var options = Options.Create(new FfmpegOptions { FfmpegPath = "videonote-missing-ffmpeg.exe" });
        var service = new FfmpegService(paths, new MediaProcessRunner(options), options);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExtractAudioAsync(fixture, id));
            Assert.Equal("previous successful result", await File.ReadAllTextAsync(audio));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(audio)!));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExtractFramesAsync(fixture, id));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(paths.Frames, id.ToString("N"))));
        }
        finally { Directory.Delete(root, true); }
    }
}
