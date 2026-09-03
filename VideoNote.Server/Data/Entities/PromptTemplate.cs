namespace VideoNote.Server.Data.Entities;

public sealed class PromptTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<AnalysisTask> AnalysisTasks { get; } = [];
}
