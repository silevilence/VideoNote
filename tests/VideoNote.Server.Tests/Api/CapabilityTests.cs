using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VideoNote.Client.Components;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;
namespace VideoNote.Server.Tests.Api;

public sealed class CapabilityTests
{
    [Theory]
    [InlineData(AnalysisMode.DirectVideo, false, false, false)]
    [InlineData(AnalysisMode.DirectVideo, true, false, false)]
    [InlineData(AnalysisMode.DirectVideo, false, true, true)]
    [InlineData(AnalysisMode.SampledFrames, false, true, false)]
    [InlineData(AnalysisMode.SampledFrames, true, false, true)]
    [InlineData(AnalysisMode.Subtitles, false, false, true)]
    [InlineData(AnalysisMode.Subtitles, true, true, true)]
    public async Task Ui_and_api_share_matching_and_explicit_override_allows_submission(
        AnalysisMode mode, bool image, bool video, bool matches)
    {
        await using var app = new ApiFactory();
        using var http = app.CreateClient();
        var provider = (await (await http.PostAsJsonAsync("/api/providers",
            new ProviderInput { Name = "capability", BaseUrl = "http://localhost/v1" })).Content.ReadFromJsonAsync<ProviderDto>())!;
        var model = (await (await http.PostAsJsonAsync("/api/models",
            new ModelInput { ProviderId = provider.Id, ModelId = "test", SupportsImage = image, SupportsVideo = video }))
            .Content.ReadFromJsonAsync<ModelDto>())!;
        Assert.Equal(matches, Ui.ModelMatches(model, mode));
        Assert.Equal(matches, ModelCapabilityRules.Matches(mode, image, video));
        using var first = await Submit(false);
        Assert.Equal(matches ? HttpStatusCode.Created : HttpStatusCode.BadRequest, first.StatusCode);
        if (!matches) Assert.Contains("手动覆盖", await first.Content.ReadAsStringAsync());
        using var overridden = await Submit(true);
        Assert.Equal(HttpStatusCode.Created, overridden.StatusCode);
        Assert.Equal(model.Id, (await overridden.Content.ReadFromJsonAsync<TaskDto>())!.ModelConfigId);
        Assert.False(ModelCapabilityRules.Matches((AnalysisMode)999, image, video));

        async Task<HttpResponseMessage> Submit(bool allow)
        {
            using var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return await http.PostAsync($"/api/tasks?fileName=test.mp4&mode={mode}&modelConfigId={model.Id}&allowCapabilityOverride={allow}", content);
        }
    }
}
