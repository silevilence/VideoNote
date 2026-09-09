using Microsoft.AspNetCore.DataProtection;

namespace VideoNote.Server.Configuration;

/// <summary>Stores a protected value or environment-variable reference; resolves only at call time.</summary>
public sealed class ProviderSecrets(IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("VideoNote.ProviderApiKey.v1");
    public string Protect(string key) => ProviderSecretReference.Protected(protector.Protect(key)).ToStorageValue();

    public string Resolve(string stored)
    {
        var reference = ProviderSecretReference.Parse(stored);
        return reference.Kind switch
        {
            ProviderSecretKind.EnvironmentVariable =>
                Environment.GetEnvironmentVariable(reference.Payload) is { Length: > 0 } value
                    ? value : throw new InvalidOperationException("提供商密钥环境变量未设置。"),
            ProviderSecretKind.Protected => protector.Unprotect(reference.Payload),
            // Existing databases may contain plaintext from the initial schema.
            _ => reference.Payload
        };
    }
}
