using Microsoft.Extensions.Options;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;

namespace VideoNote.Server.Tests.Media;

public sealed class FfmpegServiceTests
{
    private static string Fixture => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/sample.mkv"));

    [Fact]
    public async Task Real_ffmpeg_generates_overlapping_segments_frames_audio_and_subtitles()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoNote-media-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var paths = WorkDirectoryPaths.Create(root, new WorkDirectoryOptions());
            new WorkDirectoryInitializer(paths).Initialize();
            var options = Options.Create(new FfmpegOptions());
            var service = new FfmpegService(paths, new MediaProcessRunner(options), options);
            var id = Guid.NewGuid();
            var info = await service.ProbeAsync(Fixture);
            Assert.InRange(info.DurationSeconds, 124.9, 125.2);
            Assert.True(info.HasVideo && info.HasAudio);
            var segments = await service.SegmentAsync(Fixture, id);
            Assert.Equal(3, segments.Count);
            Assert.Equal(0, segments[0].StartSeconds);
            Assert.Equal(55, segments[1].StartSeconds);
            Assert.Equal(110, segments[2].StartSeconds);
            foreach (var segment in segments)
                Assert.InRange((await service.ProbeAsync(segment.Path)).DurationSeconds, segment.EndSeconds - segment.StartSeconds - .3, segment.EndSeconds - segment.StartSeconds + .3);
            var frames = await service.ExtractFramesAsync(Fixture, id);
            Assert.InRange(frames.Count, 125, 126);
            Assert.Equal(0, frames[0].TimestampSeconds);
            Assert.Equal(1, frames[1].TimestampSeconds);
            Assert.Contains("000000001000ms", frames[1].Path);
            Assert.All(frames, f => Assert.True(new FileInfo(f.Path).Length > 0));
            var wav = await service.ExtractAudioAsync(Fixture, id);
            var audio = await service.ProbeAsync(wav);
            Assert.True(audio.HasAudio); Assert.False(audio.HasVideo);
            Assert.InRange(audio.DurationSeconds, 124.8, 125.4);
            Assert.True(File.Exists(await service.ExtractAudioAsync(Fixture, id, "mp3")));
            var subtitles = (await service.ExtractSubtitlesAsync(Fixture, id))!;
            Assert.Contains("VideoNote first subtitle.", await File.ReadAllTextAsync(subtitles));
            Assert.Contains("VideoNote second subtitle.", await File.ReadAllTextAsync(subtitles));
            Assert.Null(await service.ExtractSubtitlesAsync(segments[0].Path, id));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExtractAudioAsync(Fixture, id, "invalid"));
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.ProbeAsync(Path.Combine(root, "missing.mp4")));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProbeAsync(Fixture, canceled.Token));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Missing_executable_is_actionable()
    {
        var runner = new MediaProcessRunner(Options.Create(new FfmpegOptions()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync("videonote-missing-ffmpeg.exe", [], CancellationToken.None));
        Assert.Contains("程序路径", error.Message);
    }
}
