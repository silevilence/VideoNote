namespace VideoNote.Server.Data.Entities;

public sealed class ModelConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProviderId { get; set; }

    public Provider Provider { get; set; } = null!;

    public string ModelId { get; set; } = string.Empty;

    public int ContextWindow { get; set; }

    public bool SupportsReasoning { get; set; }

    public bool SupportsToolCalling { get; set; }

    public bool SupportsStreaming { get; set; }

    public bool SupportsImage { get; set; }

    public bool SupportsAudio { get; set; }

    public bool SupportsVideo { get; set; }

    public ICollection<AnalysisTask> AnalysisTasks { get; } = [];
}
