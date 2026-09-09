using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoNote.Server.Data;
using VideoNote.Server.Storage;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Api;

public sealed class TaskEdgeTests
{
    [Fact]
    public async Task Invalid_configuration_and_running_or_locked_task_deletion_are_clear()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var limits = (await client.GetFromJsonAsync<UploadLimitsDto>("/api/tasks/upload-limits"))!;
        Assert.Equal(1024L * 1024 * 1024, limits.MaxBytes);
        foreach (var name in new[] { "modelConfigId", "promptTemplateId" })
        {
            using var invalid = new ByteArrayContent([1]);
            invalid.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/tasks?fileName=test.mp4&{name}={Guid.NewGuid()}", invalid)).StatusCode);
        }
        using var bytes = new ByteArrayContent([1, 2, 3]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var task = (await (await client.PostAsync("/api/tasks?fileName=task.mp4", bytes)).Content.ReadFromJsonAsync<TaskDto>())!;
        string path;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            var saved = (await db.AnalysisTasks.FindAsync(task.Id))!;
            saved.Status = AnalysisTaskStatus.Understanding; await db.SaveChangesAsync();
            path = Path.Combine(app.Services.GetRequiredService<WorkDirectoryPaths>().Root, saved.VideoPath);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
            (await db.AnalysisTasks.FindAsync(task.Id))!.Status = AnalysisTaskStatus.Failed;
            await db.SaveChangesAsync();
        }
        if (OperatingSystem.IsWindows())
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Model_deleted_during_upload_rolls_back_saved_video()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var provider = (await (await client.PostAsJsonAsync("/api/providers", new ProviderInput { Name = "race", BaseUrl = "https://example.com" })).Content.ReadFromJsonAsync<ProviderDto>())!;
        var model = (await (await client.PostAsJsonAsync("/api/models", new ModelInput { ProviderId = provider.Id, ModelId = "race", ContextWindow = 1000 })).Content.ReadFromJsonAsync<ModelDto>())!;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var content = new PausedContent(gate.Task);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upload = client.PostAsync($"/api/tasks?fileName=race.mp4&modelConfigId={model.Id}", content, timeout.Token);
        try
        {
            var videos = app.Services.GetRequiredService<WorkDirectoryPaths>().Videos;
            while (!Directory.EnumerateFiles(videos, "*.uploading", SearchOption.AllDirectories).Any())
                await Task.Delay(20, timeout.Token);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/models/{model.Id}")).StatusCode);
            gate.TrySetResult();
            Assert.Equal(HttpStatusCode.Conflict, (await upload).StatusCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(videos));
            Assert.Empty((await client.GetFromJsonAsync<TaskDto[]>("/api/tasks"))!);
        }
        finally { gate.TrySetResult(); }
    }

    private sealed class PausedContent(Task gate) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(new byte[64 * 1024]); await stream.FlushAsync();
            await gate; await stream.WriteAsync(new byte[1]);
        }
    }
}
