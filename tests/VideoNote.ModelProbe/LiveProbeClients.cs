using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using VideoNote.Server.AI;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data;

// Only the explicitly invoked live probe uses this transport; count physical requests and disable SDK retries.
internal sealed class LiveRequestBudget : DelegatingHandler
{
    private readonly string path = Path.GetFullPath("work-tests/live-pipeline/request-count.txt");
    private int count;
    public LiveRequestBudget() : base(new HttpClientHandler())
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        count = File.Exists(path) ? int.Parse(File.ReadAllText(path)) : 0;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host != "api.deepseek.com") throw new InvalidOperationException("Unexpected live endpoint.");
        var next = Interlocked.Increment(ref count);
        if (next > 20) throw new InvalidOperationException("Live request budget exhausted.");
        await File.WriteAllTextAsync(path, next.ToString(), ct);
        Console.WriteLine($"LIVE_REQUEST {next}/20");
        return await base.SendAsync(request, ct);
    }
}

internal sealed class LiveProbeClients(VideoNoteDbContext db, ProviderSecrets secrets, HttpClient http) : IModelChatClientFactory
{
    public async Task<IChatClient> CreateAsync(Guid modelConfigId, CancellationToken cancellationToken = default)
    {
        var model = await db.ModelConfigs.Include(m => m.Provider).SingleAsync(m => m.Id == modelConfigId, cancellationToken);
        var options = new OpenAI.OpenAIClientOptions
        {
            Endpoint = new Uri(model.Provider.BaseUrl), Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0), NetworkTimeout = Timeout.InfiniteTimeSpan
        };
        return new OpenAI.OpenAIClient(new ApiKeyCredential(secrets.Resolve(model.Provider.ApiKey)), options)
            .GetChatClient(model.ModelId).AsIChatClient();
    }
}
