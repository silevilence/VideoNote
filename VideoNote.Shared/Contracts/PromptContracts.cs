using System.ComponentModel.DataAnnotations;
namespace VideoNote.Shared.Contracts;

public sealed class PromptInput
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required, StringLength(100000)] public string Content { get; set; } = "";
}
public sealed record PromptDto(Guid Id, string Name, string Content, bool IsBuiltIn, DateTime UpdatedAtUtc);
