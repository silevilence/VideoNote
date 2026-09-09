namespace VideoNote.Server.Configuration;

public enum ProviderSecretKind { Plaintext, EnvironmentVariable, Protected }

/// <summary>Interprets the persisted credential format without resolving or exposing a secret.</summary>
public sealed class ProviderSecretReference
{
    private ProviderSecretReference(ProviderSecretKind kind, string payload)
    {
        Kind = kind;
        Payload = payload;
    }

    public ProviderSecretKind Kind { get; }
    internal string Payload { get; }
    public bool IsConfigured => Payload.Length > 0;
    public string? EnvironmentVariableName => Kind == ProviderSecretKind.EnvironmentVariable ? Payload : null;

    public static ProviderSecretReference Parse(string stored) =>
        stored.StartsWith("env:", StringComparison.Ordinal) ? EnvironmentVariable(stored[4..]) :
        stored.StartsWith("protected:", StringComparison.Ordinal) ? Protected(stored[10..]) :
        new(ProviderSecretKind.Plaintext, stored);

    public static ProviderSecretReference EnvironmentVariable(string name) => new(ProviderSecretKind.EnvironmentVariable, name);
    internal static ProviderSecretReference Protected(string payload) => new(ProviderSecretKind.Protected, payload);

    public string ToStorageValue() => Kind switch
    {
        ProviderSecretKind.EnvironmentVariable => "env:" + Payload,
        ProviderSecretKind.Protected => "protected:" + Payload,
        _ => Payload
    };

    public override string ToString() => $"ProviderSecretReference ({Kind})";
}
