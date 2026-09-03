using VideoNote.Shared.Domain;

namespace VideoNote.Server.Data.Entities;

public sealed class ConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AnalysisTaskId { get; set; }

    public AnalysisTask AnalysisTask { get; set; } = null!;

    public ConversationRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
