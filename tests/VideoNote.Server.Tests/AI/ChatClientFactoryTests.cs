using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using VideoNote.Server.AI;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.AI;

public sealed class ChatClientFactoryTests
{
    [Theory]
    [InlineData(ProviderProtocol.OpenAiCompatible)]
    [InlineData(ProviderProtocol.GeminiNative)]
    public async Task Same_caller_handles_text_streaming_images_and_configuration_changes(ProviderProtocol protocol)
    {
        var requests = new ConcurrentQueue<(string Path, string Body)>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.Run(async context =>
        {
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            requests.Enqueue((context.Request.Path.ToString(), body));
            var streaming = body.Contains("\"stream\":true") || context.Request.Path.ToString().Contains("streamGenerateContent");
            var google = protocol == ProviderProtocol.GeminiNative;
            context.Response.ContentType = streaming ? "text/event-stream" : "application/json";
            var text = google
                ? """{"candidates":[{"content":{"role":"model","parts":[{"text":"OK"}]},"finishReason":"STOP"}],"modelVersion":"test"}"""
                : streaming
                    ? """{"id":"test","object":"chat.completion.chunk","created":1,"model":"test","choices":[{"index":0,"delta":{"role":"assistant","content":"OK"},"finish_reason":"stop"}]}"""
                    : """{"id":"test","object":"chat.completion","created":1,"model":"test","choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}]}""";
            await context.Response.WriteAsync(streaming ? "data: " + text + "\n\n" + (google ? "" : "data: [DONE]\n\n") : text);
        });
        await server.StartAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new VideoNoteDbContext(new DbContextOptionsBuilder<VideoNoteDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var model = new ModelConfig { ModelId = "test-model", ContextWindow = 1000,
            Provider = new Provider { Name = "test", BaseUrl = server.Urls.Single() + (protocol == ProviderProtocol.OpenAiCompatible ? "/v1" : "/v1beta"), Protocol = protocol, ApiKey = "test-only" } };
        db.ModelConfigs.Add(model); await db.SaveChangesAsync();
        var factory = new ChatClientFactory(db, new ProviderSecrets(new EphemeralDataProtectionProvider()));
        using (var client = await factory.CreateAsync(model.Id))
        {
            Assert.Equal("OK", (await client.GetResponseAsync("Hello")).Text);
            var text = new StringBuilder();
            await foreach (var update in client.GetStreamingResponseAsync("Hello")) text.Append(update.Text);
            Assert.Equal("OK", text.ToString());
            await client.GetResponseAsync([new ChatMessage(ChatRole.User,
                [new TextContent("Describe"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")])]);
            await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User,
                [new DataContent(new byte[] { 1 }, "video/mp4")])]));
            if (protocol == ProviderProtocol.GeminiNative)
            {
                await client.GetResponseAsync([new ChatMessage(ChatRole.User,
                    [new DataContent(new byte[] { 1, 2 }, "audio/wav"),
                     new UriContent(new Uri("https://generativelanguage.googleapis.com/v1beta/files/test"), "video/mp4")])]);
                Assert.Contains(requests, r => r.Body.Contains("audio/wav") && r.Body.Contains("video/mp4") && r.Body.Contains("files/test"));
            }
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync("cancel", cancellationToken: canceled.Token));
        }
        model.ModelId = "changed-model"; await db.SaveChangesAsync();
        using (var client = await factory.CreateAsync(model.Id)) await client.GetResponseAsync("Changed");
        var sent = requests.ToArray();
        Assert.Contains(sent, r => r.Path.Contains(protocol == ProviderProtocol.GeminiNative ? "/v1beta/models/test-model:generateContent" : "/v1/chat/completions"));
        Assert.Contains(sent, r => r.Body.Contains("image/png") || r.Body.Contains("image_url"));
        Assert.Contains(sent, r => r.Body.Contains("changed-model") || r.Path.Contains("changed-model"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(Guid.NewGuid()));
        await server.StopAsync();
    }

    [Fact]
    public void Secrets_resolve_encrypted_values_and_environment_references()
    {
        var secrets = new ProviderSecrets(new EphemeralDataProtectionProvider());
        Assert.Equal("unit-test", secrets.Resolve(secrets.Protect("unit-test")));
        var name = "VIDEONOTE_TEST_" + Guid.NewGuid().ToString("N");
        Assert.Throws<InvalidOperationException>(() => secrets.Resolve("env:" + name));
        Environment.SetEnvironmentVariable(name, "test-only");
        try { Assert.Equal("test-only", secrets.Resolve("env:" + name)); }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }
}
