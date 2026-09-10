using VideoNote.Shared.Domain;
namespace VideoNote.Shared.Contracts;

public sealed record AnalysisProgress(Guid TaskId, AnalysisTaskStatus Status, int ProgressPercent,
    string? StageDescription, string? ErrorMessage);
public sealed record AnalysisText(Guid TaskId, string Stage, int SegmentIndex, string Text);
