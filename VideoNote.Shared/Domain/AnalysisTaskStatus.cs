namespace VideoNote.Shared.Domain;

public enum AnalysisTaskStatus
{
    Queued,
    Preprocessing,
    Understanding,
    Combining,
    Completed,
    Failed,
    Canceled
}
