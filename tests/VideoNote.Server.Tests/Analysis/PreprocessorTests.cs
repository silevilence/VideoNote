using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoNote.Server.Analysis;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;
using VideoNote.Server.Tests.Api;
using VideoNote.Server.Transcription;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Analysis;

public sealed class PreprocessorTests
{
    private static string Fixture => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/sample.mkv"));

    [Theory]
    [InlineData(AnalysisMode.DirectVideo, false, false)]
    [InlineData(AnalysisMode.SampledFrames, true, false)]
    [InlineData(AnalysisMode.SampledFrames, false, false)]
    [InlineData(AnalysisMode.Subtitles, false, false)]
    [InlineData(AnalysisMode.Subtitles, false, true)]
    public async Task Modes_produce_real_materials_and_video_relative_timestamps(AnalysisMode mode, bool supportsAudio, bool stripSubtitles)
    {
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var paths = app.Services.GetRequiredService<WorkDirectoryPaths>();
        var runner = app.Services.GetRequiredService<MediaProcessRunner>();
        var source = Fixture;
        if (stripSubtitles)
        {
            source = Path.Combine(paths.Root, "without-subs.mkv");
            await runner.RunAsync("ffmpeg", ["-v", "error", "-y", "-i", Fixture, "-map", "0:v", "-map", "0:a", "-c", "copy", source], default);
        }
        var task = new AnalysisTask { OriginalFileName = "test.mkv", Mode = mode };
        await using (var stream = File.OpenRead(source))
            task.VideoPath = await app.Services.GetRequiredService<VideoFileStore>().SaveAsync(task.Id, "test.mkv", stream, stream.Length, default);
        var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
        db.Add(task); await db.SaveChangesAsync();
        var transcriber = new FakeTranscriber();
        var ffmpeg = app.Services.GetRequiredService<IFfmpegService>();
        var preprocessor = new MediaPreprocessor(ffmpeg, transcriber, paths,
            app.Services.GetRequiredService<AnalysisProgressWriter>(), Options.Create(new FfmpegOptions()));
        var prepared = await preprocessor.PrepareAsync(task, new ModelConfig { SupportsAudio = supportsAudio }, default);
        Assert.InRange(prepared.DurationSeconds, 124.9, 125.2);
        Assert.True(File.Exists(Path.Combine(paths.Subtitles, task.Id.ToString("N"), "materials.json")));
        Assert.Equal(25, (await WorkerTests.WaitFor(app.CreateClient(), task.Id, AnalysisTaskStatus.Preprocessing)).ProgressPercent);
        if (mode == AnalysisMode.DirectVideo)
        {
            Assert.Equal(3, prepared.Videos.Count);
            Assert.Equal(55, prepared.Videos[1].StartSeconds);
        }
        if (mode == AnalysisMode.SampledFrames)
        {
            Assert.InRange(prepared.Frames.Count, 125, 126);
            if (supportsAudio)
            {
                Assert.Equal(3, prepared.Audio.Count);
                Assert.Equal(60, prepared.Audio[1].StartSeconds);
                Assert.InRange((await ffmpeg.ProbeAsync(prepared.Audio[0].Path)).DurationSeconds, 59.8, 60.3);
                Assert.Equal(0, transcriber.Calls);
            }
        }
        if ((mode == AnalysisMode.SampledFrames && !supportsAudio) || stripSubtitles)
        {
            Assert.Equal(3, transcriber.Calls);
            Assert.Equal(61, prepared.Subtitles[1].StartSeconds);
            Assert.Equal(121, prepared.Subtitles[2].StartSeconds);
        }
        if (mode == AnalysisMode.Subtitles && !stripSubtitles)
        {
            Assert.Equal(0, transcriber.Calls);
            Assert.Equal(2, prepared.Subtitles.Count);
            Assert.Contains("first subtitle", prepared.Subtitles[0].Text);
        }
    }

    [Fact]
    public async Task Silent_video_supports_frames_but_subtitle_mode_fails_and_paths_are_guarded()
    {
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var paths = app.Services.GetRequiredService<WorkDirectoryPaths>();
        var silent = Path.Combine(paths.Root, "silent.mkv");
        await app.Services.GetRequiredService<MediaProcessRunner>().RunAsync("ffmpeg",
            ["-v", "error", "-y", "-i", Fixture, "-t", "2", "-map", "0:v", "-c", "copy", silent], default);
        var task = new AnalysisTask { OriginalFileName = "test.mkv", Mode = AnalysisMode.SampledFrames };
        await using (var input = File.OpenRead(silent))
            task.VideoPath = await app.Services.GetRequiredService<VideoFileStore>().SaveAsync(task.Id, "test.mkv", input, input.Length, default);
        var service = scope.ServiceProvider.GetRequiredService<IMediaPreprocessor>();
        var prepared = await service.PrepareAsync(task, new(), default);
        Assert.NotEmpty(prepared.Frames);
        Assert.Empty(prepared.Audio);
        task.Mode = AnalysisMode.Subtitles;
        Assert.Contains("没有", (await Assert.ThrowsAsync<AnalysisException>(() => service.PrepareAsync(task, new(), default))).Message);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(task, new(), cts.Token));
        task.VideoPath = "../outside.mkv";
        Assert.Contains("路径", (await Assert.ThrowsAsync<AnalysisException>(() => service.PrepareAsync(task, new(), default))).Message);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("1\n00:00:03,000 --> 00:00:01,000\nbad")]
    [InlineData("1\n00:61:01,000 --> 00:62:01,000\nbad")]
    [InlineData("1\nbad --> bad\ntext")]
    public void Invalid_subtitles_are_rejected(string text) =>
        Assert.Throws<AnalysisException>(() => SubtitleParser.Parse(text));

    private sealed class FakeTranscriber : ITranscriptionService
    {
        public int Calls;
        public Task<Transcript> TranscribeAsync(string path, Guid? provider, CancellationToken ct)
        {
            Assert.True(new FileInfo(path).Length > 0);
            Calls++;
            return Task.FromResult(new Transcript([new(1, 3, "spoken sample")]));
        }
    }
}
