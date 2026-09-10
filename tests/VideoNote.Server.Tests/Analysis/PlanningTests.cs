using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Analysis;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Tests.Api;
using VideoNote.Server.Transcription;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Analysis;

public sealed class PlanningTests
{
    [Fact]
    public async Task Video_minimum_slice_cannot_exceed_context_budget()
    {
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var options = new PipelineOptions { MaxOutputTokens = 256, VideoTokensPerSecond = 100000 };
        var budget = new AnalysisBudget(4096, "", options);
        var prepared = new PreparedMedia(1, [new("must-not-be-opened.mp4", 0, 1)], [], [], []);
        var planner = scope.ServiceProvider.GetRequiredService<SegmentPlanner>();
        var error = await Assert.ThrowsAsync<AnalysisException>(() => planner.PlanAsync(
            new AnalysisTask { Mode = AnalysisMode.DirectVideo }, prepared, budget, options, default));
        Assert.Contains("预算", error.Message);
    }

    [Fact]
    public void Text_budget_splits_unicode_without_losing_content_and_packs_within_limit()
    {
        var text = string.Concat(Enumerable.Repeat("中文🙂 evidence\n", 1000));
        var parts = AnalysisBudget.Split(text, 511).ToArray();
        Assert.Equal(text, string.Concat(parts));
        Assert.All(parts, p => Assert.InRange(AnalysisBudget.Size(p), 1, 511));
        var packed = AnalysisPipeline.Pack(parts, 1024);
        Assert.All(packed, p => Assert.InRange(AnalysisBudget.Size(p), 1, 1024));
        Assert.Throws<AnalysisException>(() => new AnalysisBudget(512, "", new()));
        Assert.Throws<AnalysisException>(() => AnalysisBudget.Split("x", 1).ToArray());
    }
    [Fact]
    public async Task Planner_splits_subtitles_frames_video_and_audio_by_context()
    {
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var planner = scope.ServiceProvider.GetRequiredService<SegmentPlanner>();
        var options = new PipelineOptions { MaxOutputTokens = 256, ImageTokenEstimate = 256,
            AudioTokensPerSecond = 100, VideoTokensPerSecond = 200 };
        var budget = new AnalysisBudget(4096, "", options);
        var text = string.Concat(Enumerable.Repeat("中文🙂 ", 1000));
        var material = new PreparedMedia(3, [], [new("frame1", 0), new("frame2", 1), new("frame3", 2)], [], [new(0, 3, text)]);
        var task = new AnalysisTask { Mode = AnalysisMode.Subtitles };
        var subtitles = await planner.PlanAsync(task, material, budget, options, default);
        Assert.True(subtitles.Count > 1);
        Assert.Contains(text[..20], string.Concat(subtitles.Select(p => p.Text)));
        task.Mode = AnalysisMode.SampledFrames;
        var frames = await planner.PlanAsync(task, material, budget, options, default);
        Assert.True(frames.Count > 1);
        Assert.All(frames, f => Assert.NotEmpty(f.Frames));
        options.ImageTokenEstimate = 999999;
        await Assert.ThrowsAsync<AnalysisException>(() => planner.PlanAsync(task, material, budget, options, default));
        task.Mode = AnalysisMode.Subtitles;
        await Assert.ThrowsAsync<AnalysisException>(() => planner.PlanAsync(task, material with { Subtitles = [] }, budget, options, default));

        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/sample.mkv"));
        options.VideoTokensPerSecond = 1000;
        task.Mode = AnalysisMode.DirectVideo;
        var videos = await planner.PlanAsync(task, material with { Videos = [new(source, 0, 5)] }, budget, options, default);
        Assert.True(videos.Count > 1);
        Assert.All(videos, v => Assert.True(File.Exists(v.VideoPath)));
        options.ImageTokenEstimate = 256;
        options.AudioTokensPerSecond = 1000;
        task.Mode = AnalysisMode.SampledFrames;
        var audio = await planner.PlanAsync(task, material with { Frames = [], Audio = [new(source, 0, 5)] }, budget, options, default);
        Assert.True(audio.Count > 1);
        Assert.All(audio, v => Assert.True(File.Exists(v.AudioPath)));
    }
}
