using System.ComponentModel.DataAnnotations;
using VideoNote.Shared.Domain;

namespace VideoNote.Shared.Contracts;

public sealed class ProviderInput : IValidatableObject
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required] public string Protocol { get; set; } = ProviderProtocolNames.OpenAiCompatible;
    [Required, StringLength(2048)] public string BaseUrl { get; set; } = "";
    [StringLength(1024)] public string? ApiKey { get; set; }
    [RegularExpression(@"^[A-Za-z_][A-Za-z0-9_]*$"), StringLength(200)]
    public string? ApiKeyEnvironmentVariable { get; set; }
    public bool ClearApiKey { get; set; }
    [StringLength(200)] public string? TranscriptionModel { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!ProviderProtocolNames.TryParse(Protocol, out _))
            yield return new("协议无效。", [nameof(Protocol)]);
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            yield return new("BaseUrl 必须是无凭据、查询参数或片段的 HTTP(S) 地址。", [nameof(BaseUrl)]);
        if ((!string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(ApiKeyEnvironmentVariable)) ||
            (ClearApiKey && (!string.IsNullOrEmpty(ApiKey) || !string.IsNullOrEmpty(ApiKeyEnvironmentVariable))))
            yield return new("输入密钥、环境变量和清除密钥只能选择一种。");
    }

    /// <summary>编辑回填：密钥字段保持为空（留空保留语义），环境变量引用显式回显。</summary>
    public static ProviderInput From(ProviderDto dto) => new()
    {
        Name = dto.Name,
        Protocol = dto.Protocol,
        BaseUrl = dto.BaseUrl,
        ApiKeyEnvironmentVariable = dto.ApiKeyEnvironmentVariable,
        TranscriptionModel = dto.TranscriptionModel
    };
}

public sealed record ProviderDto(Guid Id, string Name, string Protocol, string BaseUrl,
    bool HasApiKey, string? ApiKeyEnvironmentVariable, string? TranscriptionModel);

public sealed class ModelInput
{
    public Guid ProviderId { get; set; }
    [Required, StringLength(200)] public string ModelId { get; set; } = "";
    [Range(1, int.MaxValue)] public int ContextWindow { get; set; } = 128000;
    public bool SupportsReasoning { get; set; }
    public bool SupportsToolCalling { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsImage { get; set; }
    public bool SupportsAudio { get; set; }
    public bool SupportsVideo { get; set; }

    /// <summary>编辑回填：DTO → 输入，能力标记与上下文窗口原样保留。</summary>
    public static ModelInput From(ModelDto dto) => new()
    {
        ProviderId = dto.ProviderId,
        ModelId = dto.ModelId,
        ContextWindow = dto.ContextWindow,
        SupportsReasoning = dto.SupportsReasoning,
        SupportsToolCalling = dto.SupportsToolCalling,
        SupportsStreaming = dto.SupportsStreaming,
        SupportsImage = dto.SupportsImage,
        SupportsAudio = dto.SupportsAudio,
        SupportsVideo = dto.SupportsVideo
    };
}
public sealed class ModelDto
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public string ModelId { get; set; } = "";
    public int ContextWindow { get; set; }
    public bool SupportsReasoning { get; set; }
    public bool SupportsToolCalling { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsImage { get; set; }
    public bool SupportsAudio { get; set; }
    public bool SupportsVideo { get; set; }
}

