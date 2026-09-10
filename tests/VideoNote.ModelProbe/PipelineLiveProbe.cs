using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Storage;
using VideoNote.Server.Tests.Api;
using VideoNote.Server.Tests.Analysis;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

internal static class PipelineLiveProbe
{
    public static async Task<int> RunAsync()
    {
        // Only freshly generated test patterns and placeholder subtitles leave the machine.
        // Resolve the credential only inside the provider client; never print it or upstream errors.
        try
        {
            await using var transcription = await ProtocolEndpoint.Start();
            var transcriptionId = Guid.NewGuid();
            await using var app = new ApiFactory(new()
            {
                ["Ffmpeg:FramesPerSecond"] = "0.25",
                ["Transcription:ProviderId"] = transcriptionId.ToString(),
                ["Pipeline:MaxOutputTokens"] = "2048",
                ["Pipeline:RequestTimeoutSeconds"] = "240"
            }, runWorker: true);
            using var http = app.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            var paths = app.Services.GetRequiredService<WorkDirectoryPaths>();
            var fixture = Path.Combine(paths.Root, "synthetic-probe.mkv");
            var subtitles = Path.Combine(paths.Root, "synthetic-probe.srt");
            await File.WriteAllTextAsync(subtitles, "1\n00:00:01,000 --> 00:00:03,000\nVideoNote synthetic test subtitle.\n");
            await app.Services.GetRequiredService<MediaProcessRunner>().RunAsync("ffmpeg",
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i",
                 "testsrc2=size=160x90:rate=5:duration=8", "-f", "lavfi", "-i",
                 "sine=frequency=440:sample_rate=16000:duration=8", "-i", subtitles,
                 "-map", "0:v", "-map", "1:a", "-map", "2:s", "-c:v", "libx264", "-preset", "ultrafast",
                 "-crf", "35", "-c:a", "aac", "-c:s", "srt", fixture], default);
            Guid modelId;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
                var model = new ModelConfig
                {
                    ModelId = "deepseek-v4-flash-vision-exp", ContextWindow = 128000, SupportsImage = true, SupportsStreaming = true,
                    Provider = new Provider { Name = "DeepSeek live pipeline", BaseUrl = "https://api.deepseek.com", ApiKey = "env:DEEPSEEK_API_KEY" }
                };
                db.Add(model);
                db.Add(new Provider { Id = transcriptionId, Name = "Simulated transcription", BaseUrl = transcription.Url + "/v1", TranscriptionModel = "test-whisper" });
                await db.SaveChangesAsync(); modelId = model.Id;
            }
            foreach (var mode in new[] { AnalysisMode.SampledFrames, AnalysisMode.Subtitles })
            {
                using var content = new StreamContent(File.OpenRead(fixture));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var created = await http.PostAsync($"/api/tasks?fileName=synthetic-probe.mkv&mode={mode}&modelConfigId={modelId}", content);
                created.EnsureSuccessStatusCode();
                var task = (await created.Content.ReadFromJsonAsync<TaskDto>())!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
                while (true)
                {
                    var result = (await http.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", timeout.Token))!;
                    if (result.Status == AnalysisTaskStatus.Failed)
                    { Console.WriteLine($"FAIL: {mode}: {result.ErrorMessage}"); return 1; }
                    if (result.Status == AnalysisTaskStatus.Completed)
                    {
                        if (result.Segments?.Count is not > 0 || result.ResultText?.Length is not > 20) return 1;
                        Directory.CreateDirectory("work-tests/live-pipeline");
                        await File.WriteAllTextAsync($"work-tests/live-pipeline/{mode}.md", result.ResultText, timeout.Token);
                        Console.WriteLine($"PASS: DeepSeek {mode}, map calls={result.Segments.Count}, report chars={result.ResultText.Length}");
                        break;
                    }
                    await Task.Delay(1000, timeout.Token);
                }
            }
            return 0;
        }
        catch (Exception ex)
        { Console.WriteLine($"FAIL: pipeline probe ({ex.GetType().Name}), upstream details suppressed"); return 1; }
    }
}
