using VideoNote.Shared.Domain;

namespace VideoNote.Server.Data.Entities;

public sealed class AnalysisTask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OriginalFileName { get; set; } = string.Empty;

    public string VideoPath { get; set; } = string.Empty;

    public AnalysisMode Mode { get; set; }

    public AnalysisTaskStatus Status { get; set; } = AnalysisTaskStatus.Queued;

    public int ProgressPercent { get; set; }

    public string? StageDescription { get; set; }

    public string? PromptContentSnapshot { get; set; }

    public string? ResultText { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid? ModelConfigId { get; set; }

    public ModelConfig? ModelConfig { get; set; }

    public Guid? PromptTemplateId { get; set; }

    public PromptTemplate? PromptTemplate { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public ICollection<ConversationMessage> Messages { get; } = [];
}
