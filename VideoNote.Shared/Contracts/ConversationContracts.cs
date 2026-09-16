using System.ComponentModel.DataAnnotations;
using VideoNote.Shared.Domain;

namespace VideoNote.Shared.Contracts;

public sealed record ConversationSettingsDto(Guid? ModelConfigId);
public sealed class ConversationInput
{
    [Required, StringLength(8000, MinimumLength = 1)] public string Message { get; set; } = "";
}
public sealed record ConversationMessageDto(Guid Id, ConversationRole Role, string Content, DateTime CreatedAtUtc);
public sealed record ConversationEvent(string Type, string? Text = null);
