using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using VideoNote.Server.Tests.Api;
using VideoNote.Server.Storage;

namespace VideoNote.Server.Tests.Storage;

public sealed class UploadConfigurationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Upload":{"MaxBytes":42}}""")]
    [InlineData("""{"Upload":{"AllowedExtensions":[".custom"]}}""")]
    [InlineData("""{"Upload":{"AllowedExtensions":[]}}""")]
    public void Missing_settings_use_defaults_and_explicit_arrays_replace_them(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var config = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new UploadOptions();
        options.Bind(config.GetSection("Upload"));
        Assert.Equal(json.Contains("42") ? 42L : 1073741824L, options.MaxBytes);
        var expected = json.Contains(".custom") ? new[] { ".custom" } :
            json.Contains("[]") ? [] : new[] { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v" };
        Assert.Equal(expected, options.AllowedExtensions);
    }

    [Fact]
    public async Task Runtime_override_replaces_defaults_in_advertised_limits_and_upload_validation()
    {
        await using var app = new ApiFactory(new() { ["Upload:AllowedExtensions:0"] = ".custom" });
        using var client = app.CreateClient();
        var limits = await client.GetFromJsonAsync<UploadLimits>("/api/tasks/upload-limits");
        Assert.Equal(new[] { ".custom" }, limits!.AllowedExtensions);
        using var rejected = new ByteArrayContent([1, 2, 3]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/tasks?fileName=test.mp4", rejected)).StatusCode);
        using var accepted = new ByteArrayContent([1, 2, 3]);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/tasks?fileName=test.custom", accepted)).StatusCode);
    }

    private sealed record UploadLimits(long MaxBytes, string[] AllowedExtensions);
}
