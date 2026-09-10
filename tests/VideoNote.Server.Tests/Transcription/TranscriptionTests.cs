using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoNote.Server.Analysis;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Server.Media;
using VideoNote.Server.Tests.Api;
using VideoNote.Server.Transcription;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Transcription;

public sealed class TranscriptionTests
{
    [Fact]
    public async Task Real_audio_is_uploaded_to_configured_protocol_endpoint_with_timestamps_and_safe_failures()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var endpoint = builder.Build();
        var status = 200;
        string? model = null;
        long uploaded = 0;
        endpoint.MapPost("/v1/audio/transcriptions", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            model = form["model"];
            Assert.Equal("verbose_json", form["response_format"]);
            Assert.Equal("segment", form["timestamp_granularities[]"]);
            Assert.Equal("Bearer test-only", context.Request.Headers.Authorization);
            uploaded = form.Files["file"]!.Length;
            context.Response.StatusCode = status;
            await context.Response.WriteAsync(status == 200
                ? """{"text":"hello world","segments":[{"start":0,"end":1.5,"text":"hello"},{"start":2,"end":3,"text":"world"}]}"""
                : "secret-upstream-error");
        });
        await endpoint.StartAsync();
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
        var provider = new Provider { Name = "transcription", BaseUrl = endpoint.Urls.Single() + "/v1", TranscriptionModel = "test-whisper", ApiKey = "test-only" };
        db.Add(provider); await db.SaveChangesAsync();
        var ffmpeg = app.Services.GetRequiredService<IFfmpegService>();
        var audio = await ffmpeg.ExtractAudioAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/sample.mkv")), Guid.NewGuid());
        var service = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();
        var transcript = await service.TranscribeAsync(audio, provider.Id, CancellationToken.None);
        Assert.Equal(2, transcript.Segments.Count);
        Assert.Equal(1.5, transcript.Segments[0].EndSeconds);
        Assert.Contains("hello", transcript.Text);
        Assert.Equal("test-whisper", model);
        Assert.Equal(new FileInfo(audio).Length, uploaded);
        status = 401;
        var error = await Assert.ThrowsAsync<AnalysisException>(() => service.TranscribeAsync(audio, provider.Id, CancellationToken.None));
        Assert.Contains("401", error.Message);
        Assert.DoesNotContain("secret", error.Message);
        await Assert.ThrowsAsync<AnalysisException>(() => service.TranscribeAsync(audio, Guid.NewGuid(), CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TranscribeAsync(audio, provider.Id, cancelled.Token));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"segments\":[]}")]
    [InlineData("{\"segments\":[{\"start\":-1,\"end\":2,\"text\":\"bad\"}]}")]
    [InlineData("{\"segments\":[{\"start\":2,\"end\":1,\"text\":\"bad\"}]}")]
    [InlineData("{\"segments\":[{\"start\":0,\"end\":1,\"text\":\" \"}]}")]
    [InlineData("{\"segments\":[{\"start\":\"0\",\"end\":1,\"text\":\"bad\"}]}")]
    public void Invalid_timestamp_responses_fail_explicitly(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Throws<AnalysisException>(() => OpenAiTranscriptionBackend.Parse(doc.RootElement));
    }

    [Fact]
    public async Task Global_configuration_and_file_transport_errors_are_explicit()
    {
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
        var provider = new Provider { Name = "global", BaseUrl = "http://localhost/v1" };
        db.Add(provider); await db.SaveChangesAsync();
        var backend = new StubBackend();
        var options = Options.Create(new TranscriptionOptions { ProviderId = provider.Id, Model = "global-model" });
        var service = new TranscriptionService(db,
            app.Services.GetRequiredService<VideoNote.Server.Configuration.ProviderSecrets>(), backend, options);
        await service.TranscribeAsync("unused", null, CancellationToken.None);
        Assert.Equal("global-model", backend.Model);
        provider.ApiKey = "env:VIDEONOTE_MISSING_TRANSCRIPTION_KEY"; await db.SaveChangesAsync();
        Assert.Contains("密钥", (await Assert.ThrowsAsync<AnalysisException>(() => service.TranscribeAsync("unused", null, CancellationToken.None))).Message);

        var path = Path.Combine(app.Services.GetRequiredService<VideoNote.Server.Storage.WorkDirectoryPaths>().Audio, "test.wav");
        await File.WriteAllBytesAsync(path, [1, 2]);
        using var http = new HttpClient(new FakeHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var real = new OpenAiTranscriptionBackend(http, Options.Create(new TranscriptionOptions { TimeoutSeconds = 1 }));
        var uri = new Uri("http://localhost/audio/transcriptions");
        Assert.Contains("不存在", (await Assert.ThrowsAsync<AnalysisException>(() => real.TranscribeAsync(path + "missing", uri, "test", "", default))).Message);
        Assert.Contains("超时", (await Assert.ThrowsAsync<AnalysisException>(() => real.TranscribeAsync(path, uri, "timeout", "", default))).Message);
        Assert.Contains("网络", (await Assert.ThrowsAsync<AnalysisException>(() => real.TranscribeAsync(path, uri, "network", "", default))).Message);
        Assert.Contains("JSON", (await Assert.ThrowsAsync<AnalysisException>(() => real.TranscribeAsync(path, uri, "invalid", "", default))).Message);
        var limited = new OpenAiTranscriptionBackend(http, Options.Create(new TranscriptionOptions { MaxAudioBytes = 1 }));
        Assert.Contains("上限", (await Assert.ThrowsAsync<AnalysisException>(() => limited.TranscribeAsync(path, uri, "test", "", default))).Message);
    }

    private sealed class StubBackend : IAudioTranscriptionBackend
    {
        public string? Model;
        public Task<Transcript> TranscribeAsync(string path, Uri endpoint, string model, string key, CancellationToken ct)
        { Model = model; return Task.FromResult(new Transcript([new(0, 1, "text")])); }
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (body.Contains("timeout")) await Task.Delay(Timeout.Infinite, ct);
            if (body.Contains("network")) throw new HttpRequestException("secret");
            return new(HttpStatusCode.OK) { Content = new StringContent("invalid JSON") };
        }
    }
}
