using VideoNote.Shared.Domain;

namespace VideoNote.Server.Data.Entities;

public sealed class Provider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public ProviderProtocol Protocol { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string? TranscriptionModel { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ModelConfig> Models { get; } = [];
}
