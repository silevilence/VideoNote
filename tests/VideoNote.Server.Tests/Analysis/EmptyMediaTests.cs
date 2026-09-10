using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoNote.Server.Analysis;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;
using VideoNote.Server.Tests.Api;
using VideoNote.Server.Transcription;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Analysis;

public sealed class EmptyMediaTests
{
    [Theory]
    [InlineData(AnalysisMode.SampledFrames, true)]
    [InlineData(AnalysisMode.SampledFrames, false)]
    [InlineData(AnalysisMode.Subtitles, false)]
    public async Task Short_audio_tail_is_skipped_without_losing_visual_or_spoken_evidence(AnalysisMode mode, bool direct)
    {
        await using var app = new ApiFactory(new() { ["Ffmpeg:SegmentSeconds"] = "3", ["Ffmpeg:OverlapSeconds"] = "0" });
        _ = app.CreateClient();
        var paths = app.Services.GetRequiredService<WorkDirectoryPaths>();
        var source = Path.Combine(paths.Root, "short-audio.mkv");
        await app.Services.GetRequiredService<MediaProcessRunner>().RunAsync("ffmpeg",
            ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=10:duration=7",
             "-f", "lavfi", "-i", "sine=frequency=440:duration=1.5", "-c:v", "libx264", "-c:a", "aac", source], default);
        var task = new AnalysisTask { Mode = mode, OriginalFileName = "short-audio.mkv" };
        await using (var input = File.OpenRead(source))
            task.VideoPath = await app.Services.GetRequiredService<VideoFileStore>().SaveAsync(task.Id, task.OriginalFileName, input, input.Length, default);
        var transcriber = new ProbeTranscriber(app.Services.GetRequiredService<IFfmpegService>());
        var processor = new MediaPreprocessor(app.Services.GetRequiredService<IFfmpegService>(), transcriber, paths,
            app.Services.GetRequiredService<AnalysisProgressWriter>(), Options.Create(new FfmpegOptions { SegmentSeconds = 3, OverlapSeconds = 0 }));
        var material = await processor.PrepareAsync(task, new ModelConfig { SupportsAudio = direct }, default);
        if (mode == AnalysisMode.SampledFrames) Assert.NotEmpty(material.Frames);
        if (direct)
        {
            Assert.Single(material.Audio); Assert.Equal(0, transcriber.Calls);
            // Context splitting must also skip the empty tail of a partially filled audio slice.
            var options = new PipelineOptions { MaxOutputTokens = 256, AudioTokensPerSecond = 1000 };
            var planner = new SegmentPlanner(app.Services.GetRequiredService<IFfmpegService>());
            var plan = await planner.PlanAsync(task, material with { Frames = [] }, new AnalysisBudget(4096, "", options), options, default);
            Assert.Single(plan);
            Assert.NotNull(plan[0].AudioPath);
        }
        else
        {
            Assert.Single(material.Subtitles); Assert.Equal(1, transcriber.Calls);
            Assert.Equal("real audio", material.Subtitles[0].Text);
        }
        Assert.DoesNotContain(Directory.GetFiles(paths.Root, "*", SearchOption.AllDirectories), p => p.Contains(".partial"));
        Assert.Null(await app.Services.GetRequiredService<IFfmpegService>().ExtractAudioRangeAsync(source, task.Id, 6, 7));
    }

    [Fact]
    public async Task Short_video_stream_does_not_create_audio_only_video_segments()
    {
        await using var app = new ApiFactory(new() { ["Ffmpeg:SegmentSeconds"] = "3", ["Ffmpeg:OverlapSeconds"] = "0" });
        _ = app.CreateClient();
        var paths = app.Services.GetRequiredService<WorkDirectoryPaths>();
        var source = Path.Combine(paths.Root, "short-video.mkv");
        await app.Services.GetRequiredService<MediaProcessRunner>().RunAsync("ffmpeg",
            ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=10:duration=1.5",
             "-f", "lavfi", "-i", "sine=frequency=440:duration=7", "-c:v", "libx264", "-c:a", "aac", source], default);
        var service = app.Services.GetRequiredService<IFfmpegService>();
        var segments = await service.SegmentAsync(source, Guid.NewGuid());
        Assert.Single(segments);
        Assert.True((await service.ProbeAsync(segments[0].Path)).HasVideo);
        var options = new PipelineOptions { MaxOutputTokens = 256, VideoTokensPerSecond = 1000 };
        var plan = await new SegmentPlanner(service).PlanAsync(new AnalysisTask { Mode = AnalysisMode.DirectVideo },
            new PreparedMedia(7, segments, [], [], []), new AnalysisBudget(4096, "", options), options, default);
        Assert.Single(plan);
        Assert.NotNull(plan[0].VideoPath);
    }

    private sealed class ProbeTranscriber(IFfmpegService media) : ITranscriptionService
    {
        public int Calls;
        public async Task<Transcript> TranscribeAsync(string path, Guid? provider, CancellationToken ct)
        {
            Assert.True((await media.ProbeAsync(path, ct)).HasAudio);
            Calls++;
            return new Transcript([new(0, 1, "real audio")]);
        }
    }
}
