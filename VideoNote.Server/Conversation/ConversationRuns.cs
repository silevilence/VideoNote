using System.Collections.Concurrent;

namespace VideoNote.Server.Conversation;

// Admission and deletion also share AnalysisQueue.Gate; the dictionary is safe for lock-free status checks.
public sealed class ConversationRuns
{
    private readonly ConcurrentDictionary<Guid, byte> active = new();
    public bool TryBegin(Guid id) => active.TryAdd(id, 0);
    public bool IsActive(Guid id) => active.ContainsKey(id);
    public void End(Guid id) => active.TryRemove(id, out _);
}

public sealed class ConversationOptions
{
    public int MaxOutputTokens { get; set; } = 2048;
    public int RequestTimeoutSeconds { get; set; } = 300;
}

public sealed class ConversationException(string message) : Exception(message);
