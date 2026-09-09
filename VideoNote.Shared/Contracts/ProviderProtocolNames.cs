using VideoNote.Shared.Domain;

namespace VideoNote.Shared.Contracts;

/// <summary>Stable wire names shared by API validation, serialization and configuration forms.</summary>
public static class ProviderProtocolNames
{
    public const string OpenAiCompatible = "openai-compatible";
    public const string GeminiNative = "gemini-native";

    public static string ToWireName(this ProviderProtocol protocol) => protocol switch
    {
        ProviderProtocol.OpenAiCompatible => OpenAiCompatible,
        ProviderProtocol.GeminiNative => GeminiNative,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };

    public static bool TryParse(string? value, out ProviderProtocol protocol)
    {
        ProviderProtocol? parsed = value switch
        {
            OpenAiCompatible => ProviderProtocol.OpenAiCompatible,
            GeminiNative => ProviderProtocol.GeminiNative,
            _ => null
        };
        protocol = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    public static ProviderProtocol Parse(string value) => TryParse(value, out var protocol)
        ? protocol : throw new ArgumentException("协议无效。", nameof(value));
}
