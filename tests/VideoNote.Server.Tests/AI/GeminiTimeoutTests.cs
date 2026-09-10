using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.AI;
using VideoNote.Server.Tests.Analysis;
using VideoNote.Server.Tests.Api;
namespace VideoNote.Server.Tests.AI;

public sealed class GeminiTimeoutTests
{
    [Fact]
    [Trait("Category", "SlowProtocol")]
    public async Task Nonstreaming_gemini_can_read_a_body_after_120_seconds()
    {
        await using var endpoint = await ProtocolEndpoint.Start();
        endpoint.FirstChatDelayMilliseconds = 125000;
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        var id = await PipelineProtocolTests.Seed(app.Services, endpoint.Url, true);
        using var scope = app.Services.CreateScope();
        using var client = await scope.ServiceProvider.GetRequiredService<IModelChatClientFactory>().CreateAsync(id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var elapsed = Stopwatch.StartNew();
        var result = await client.GetResponseAsync("local synthetic timeout probe", cancellationToken: timeout.Token);
        Assert.Contains("PIPELINE_OK", result.Text);
        Assert.True(elapsed.Elapsed > TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task Caller_can_cancel_gemini_while_waiting_for_response_body()
    {
        await using var endpoint = await ProtocolEndpoint.Start();
        endpoint.FirstChatDelayMilliseconds = 10000;
        await using var app = new ApiFactory();
        _ = app.CreateClient();
        var id = await PipelineProtocolTests.Seed(app.Services, endpoint.Url, true);
        using var scope = app.Services.CreateScope();
        using var client = await scope.ServiceProvider.GetRequiredService<IModelChatClientFactory>().CreateAsync(id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = client.GetResponseAsync("local synthetic cancellation probe", cancellationToken: timeout.Token);
        await endpoint.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await timeout.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
        Assert.Equal(1, endpoint.ChatCalls);
    }
}
