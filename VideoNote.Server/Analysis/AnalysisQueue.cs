using System.Collections.Concurrent;
using System.Threading.Channels;
namespace VideoNote.Server.Analysis;

public sealed class AnalysisOptions
{
    // One consumer today; the setting is reserved and validated to avoid implying unsupported parallelism.
    public int MaxConcurrency { get; set; } = 1;
}
public sealed class AnalysisQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new() { SingleReader = true });
    public SemaphoreSlim Gate { get; } = new(1, 1);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> running = new();
    public void Enqueue(Guid id) => channel.Writer.TryWrite(id);
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
    public bool IsActive(Guid id) => running.ContainsKey(id);
    public void Register(Guid id, CancellationTokenSource cts) => running[id] = cts;
    public void Cancel(Guid id) { if (running.TryGetValue(id, out var cts)) cts.Cancel(); }
    public void Release(Guid id) => running.TryRemove(id, out _);
}
