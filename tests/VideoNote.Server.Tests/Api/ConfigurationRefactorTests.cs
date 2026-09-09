using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using VideoNote.Server.Configuration;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Api;

public sealed class ConfigurationRefactorTests
{
    [Theory]
    [InlineData(ProviderProtocol.OpenAiCompatible, "openai-compatible")]
    [InlineData(ProviderProtocol.GeminiNative, "gemini-native")]
    public async Task Protocol_names_round_trip_through_api_and_edit_form(ProviderProtocol protocol, string name)
    {
        Assert.Equal(name, protocol.ToWireName());
        Assert.Equal(protocol, ProviderProtocolNames.Parse(name));
        Assert.False(ProviderProtocolNames.TryParse("unknown", out _));
        Assert.False(ProviderProtocolNames.TryParse(null, out _));
        Assert.Throws<ArgumentException>(() => ProviderProtocolNames.Parse("unknown"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ProviderProtocol)99).ToWireName());
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var input = new ProviderInput { Name = "Protocol test", BaseUrl = "https://example.com", Protocol = name };
        var response = await client.PostAsJsonAsync("/api/providers", input);
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadFromJsonAsync<ProviderDto>())!;
        Assert.Equal(name, dto.Protocol);
        Assert.Equal(name, ProviderInput.From(dto).Protocol);
    }

    [Fact]
    public void Response_has_no_input_validators_and_edit_mapping_preserves_every_field()
    {
        Assert.False(typeof(ModelInput).IsAssignableFrom(typeof(ModelDto)));
        Assert.All(typeof(ModelDto).GetProperties(), p => Assert.Empty(p.GetCustomAttributes<ValidationAttribute>()));
        var dto = new ModelDto
        {
            Id = Guid.NewGuid(),
            ProviderId = Guid.NewGuid(),
            ModelId = "vision",
            ContextWindow = 2048,
            SupportsReasoning = true,
            SupportsToolCalling = true,
            SupportsStreaming = true,
            SupportsImage = true,
            SupportsAudio = true,
            SupportsVideo = true
        };
        var input = ModelInput.From(dto);
        foreach (var property in typeof(ModelInput).GetProperties())
            Assert.Equal(typeof(ModelDto).GetProperty(property.Name)!.GetValue(dto), property.GetValue(input));
        input.ModelId = "";
        input.ContextWindow = 0;
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(ModelInput.ModelId)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(ModelInput.ContextWindow)));
        Assert.Equal("vision", dto.ModelId);
    }

    [Fact]
    public void Secret_reference_preserves_storage_compatibility_and_redacts_diagnostic_output()
    {
        var secrets = new ProviderSecrets(new EphemeralDataProtectionProvider());
        foreach (var stored in new[] { "", "legacy-test-only", "env:UNIT_TEST_REFERENCE_ONLY", secrets.Protect("protected-test-only") })
        {
            var reference = ProviderSecretReference.Parse(stored);
            Assert.Equal(stored, reference.ToStorageValue());
            Assert.Equal(stored.Length > 0, reference.IsConfigured);
            if (stored.Length > 0) Assert.DoesNotContain(stored, reference.ToString());
        }
        Assert.Equal(ProviderSecretKind.Plaintext, ProviderSecretReference.Parse("legacy-test-only").Kind);
        Assert.Equal("legacy-test-only", secrets.Resolve("legacy-test-only"));
        Assert.Equal("", secrets.Resolve(""));
        var environment = ProviderSecretReference.EnvironmentVariable("UNIT_TEST_REFERENCE_ONLY");
        Assert.Equal("UNIT_TEST_REFERENCE_ONLY", environment.EnvironmentVariableName);
        Assert.Null(ProviderSecretReference.Parse(secrets.Protect("test")).EnvironmentVariableName);
    }
}
