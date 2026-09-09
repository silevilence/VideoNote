using System.ComponentModel.DataAnnotations;
using VideoNote.Shared.Domain;
namespace VideoNote.Shared.Contracts;

public sealed class PromptInput
{
    [Required, StringLength(PromptTemplateRules.MaxNameLength)] public string Name { get; set; } = "";
    [Required, StringLength(100000)] public string Content { get; set; } = "";
}
public sealed record PromptDto(Guid Id, string Name, string Content, bool IsBuiltIn, DateTime UpdatedAtUtc);
