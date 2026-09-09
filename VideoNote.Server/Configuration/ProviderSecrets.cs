using Microsoft.AspNetCore.DataProtection;

namespace VideoNote.Server.Configuration;

/// <summary>Stores a protected value or environment-variable reference; resolves only at call time.</summary>
public sealed class ProviderSecrets(IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("VideoNote.ProviderApiKey.v1");
    public string Protect(string key) => "protected:" + protector.Protect(key);
    public static string? EnvironmentName(string stored) =>
        stored.StartsWith("env:", StringComparison.Ordinal) ? stored[4..] : null;

    public string Resolve(string stored)
    {
        if (EnvironmentName(stored) is { } name)
            return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value : throw new InvalidOperationException("提供商密钥环境变量未设置。");
        if (stored.StartsWith("protected:", StringComparison.Ordinal))
            return protector.Unprotect(stored[10..]);
        // Existing databases may contain plaintext from the initial schema.
        return stored;
    }
}

