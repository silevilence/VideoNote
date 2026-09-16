using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Api;

public sealed class TaskCreationTests
{
    [Fact]
    public async Task Multipart_stream_preserves_inline_snapshot_without_editing_template()
    {
        await using var app = new ApiFactory();
        using var http = app.CreateClient();
        var template = (await http.GetFromJsonAsync<List<PromptDto>>("/api/prompts"))![0];
        var text = string.Concat(Enumerable.Repeat("中文长提示词 & ? #\n", 600));
        using var body = Upload(new() { FileName = "inline.mp4", Mode = AnalysisMode.Subtitles,
            ModelConfigId = Guid.Parse("20000000-0000-0000-0000-000000000002"), PromptTemplateId = template.Id, PromptContent = text });
        var response = await http.PostAsync("/api/tasks/upload", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        var saved = (await http.GetFromJsonAsync<TaskDto>($"/api/tasks/{created.Id}"))!;
        Assert.Equal(text.Trim(), saved.PromptContentSnapshot);
        Assert.Equal(AnalysisTaskStatus.Queued, saved.Status);
        Assert.Equal(template.Content, (await http.GetFromJsonAsync<PromptDto>($"/api/prompts/{template.Id}"))!.Content);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 16001)]
    public async Task Invalid_creation_is_rejected_before_video_is_saved(bool model, int length)
    {
        await using var app = new ApiFactory();
        using var http = app.CreateClient();
        using var body = Upload(new() { FileName = "invalid.mp4", Mode = AnalysisMode.Subtitles,
            ModelConfigId = model ? Guid.Parse("20000000-0000-0000-0000-000000000002") : null, PromptContent = new string('x', length) });
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/api/tasks/upload", body)).StatusCode);
        Assert.Empty((await http.GetFromJsonAsync<TaskDto[]>("/api/tasks"))!);
    }

    private static MultipartFormDataContent Upload(CreateTaskInput input)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(input)), "metadata");
        form.Add(new ByteArrayContent([1, 2, 3]), "video", input.FileName);
        return form;
    }
}


