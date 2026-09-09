using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Data;
using VideoNote.Shared.Contracts;

namespace VideoNote.Server.Tests.Api;

public sealed class PromptTests
{
    [Theory]
    [InlineData(10, false)]
    [InlineData(200, false)]
    [InlineData(196, true)]
    public async Task Copies_keep_valid_names_and_duplicate_template_names_remain_allowed(int length, bool emoji)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var input = new PromptInput { Name = new string('字', length) + (emoji ? "😀结尾" : ""), Content = "Copy me" };
        var response = await client.PostAsJsonAsync("/api/prompts", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/prompts", input)).StatusCode);
        var source = (await response.Content.ReadFromJsonAsync<PromptDto>())!;
        var copyResponse = await client.PostAsJsonAsync($"/api/prompts/{source.Id}/copy", new { });
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);
        var copy = (await copyResponse.Content.ReadFromJsonAsync<PromptDto>())!;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal(source.Content, copy.Content);
        Assert.EndsWith(" 副本", copy.Name);
        Assert.True(copy.Name.Length <= VideoNote.Shared.Domain.PromptTemplateRules.MaxNameLength);
        var expectedPrefix = source.Name[..Math.Min(source.Name.Length, emoji ? 196 : 197)];
        Assert.Equal(expectedPrefix + " 副本", copy.Name);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/prompts/{copy.Id}",
            new PromptInput { Name = copy.Name, Content = "Editable copy" })).StatusCode);
    }

    [Fact]
    public async Task Builtins_are_readonly_custom_templates_are_editable_and_tasks_keep_snapshot()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var initial = (await client.GetFromJsonAsync<PromptDto[]>("/api/prompts"))!;
        Assert.Equal(4, initial.Length);
        Assert.All(initial, p => Assert.True(p.IsBuiltIn));
        var source = initial[0];
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/prompts/{source.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/prompts/{source.Id}", new PromptInput { Name = "changed", Content = "changed" })).StatusCode);
        var response = await client.PostAsJsonAsync($"/api/prompts/{source.Id}/copy", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = (await response.Content.ReadFromJsonAsync<PromptDto>())!;
        Assert.False(copy.IsBuiltIn); Assert.Equal(source.Content, copy.Content);
        var update = new PromptInput { Name = "Custom", Content = "Original task instructions" };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/prompts/{copy.Id}", update)).StatusCode);
        using var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        response = await client.PostAsync($"/api/tasks?fileName=prompt.mp4&promptTemplateId={copy.Id}", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        update.Content = "Changed later";
        await client.PutAsJsonAsync($"/api/prompts/{copy.Id}", update);
        Assert.Equal("Changed later", (await client.GetFromJsonAsync<PromptDto>($"/api/prompts/{copy.Id}"))!.Content);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/prompts/{copy.Id}")).StatusCode);
        var saved = (await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}"))!;
        Assert.Null(saved.PromptTemplateId);
        Assert.Equal("Original task instructions", saved.PromptContentSnapshot);
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>().Database.MigrateAsync();
        Assert.Equal(4, (await client.GetFromJsonAsync<PromptDto[]>("/api/prompts"))!.Length);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/prompts", new PromptInput { Name = "New", Content = "New content" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/prompts", new PromptInput { Name = " ", Content = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/prompts/{Guid.NewGuid()}")).StatusCode);
    }
}
