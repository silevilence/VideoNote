using System.ComponentModel.DataAnnotations;
using VideoNote.Shared.Domain;

namespace VideoNote.Shared.Contracts;

public sealed class CreateTaskInput
{
    [Required, StringLength(260)] public string FileName { get; set; } = "";
    [EnumDataType(typeof(AnalysisMode))] public AnalysisMode Mode { get; set; }
    public Guid? ModelConfigId { get; set; }
    public Guid? PromptTemplateId { get; set; }
    public bool AllowCapabilityOverride { get; set; }
}
public sealed record TaskDto(Guid Id, string OriginalFileName, AnalysisMode Mode, AnalysisTaskStatus Status,
    string? StageDescription, DateTime CreatedAtUtc, Guid? ModelConfigId, Guid? PromptTemplateId, string? PromptContentSnapshot = null, int ProgressPercent = 0, string? ErrorMessage = null,
    string? ResultText = null, DateTime? StartedAtUtc = null, DateTime? CompletedAtUtc = null, IReadOnlyList<SegmentResultDto>? Segments = null);
public sealed record SegmentResultDto(int Index, double StartSeconds, double EndSeconds, string Text);
public sealed record UploadLimitsDto(long MaxBytes, string[] AllowedExtensions);
