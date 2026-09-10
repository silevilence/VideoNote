using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using VideoNote.Server.AI;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;

if (args.Contains("--pipeline")) return await PipelineLiveProbe.RunAsync();
// This program alone resolves the credential at call time. Never print settings, headers or raw exceptions.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new VideoNoteDbContext(new DbContextOptionsBuilder<VideoNoteDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
var model = new ModelConfig
{
    ModelId = "deepseek-v4-flash-vision-exp", ContextWindow = 128000, SupportsImage = true, SupportsStreaming = true,
    Provider = new Provider { Name = "DeepSeek probe", BaseUrl = "https://api.deepseek.com", ApiKey = "env:DEEPSEEK_API_KEY" }
};
db.ModelConfigs.Add(model);
await db.SaveChangesAsync();
try
{
    using var client = await new ChatClientFactory(db, new ProviderSecrets(new EphemeralDataProtectionProvider())).CreateAsync(model.Id);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var options = new ChatOptions { MaxOutputTokens = 512 };
    var reply = await client.GetResponseAsync("Reply with exactly VIDEONOTE_OK.", options, timeout.Token);
    if (!reply.Text.Contains("VIDEONOTE_OK")) throw new InvalidOperationException();
    Console.WriteLine("PASS: DeepSeek non-streaming text");
    var text = "";
    await foreach (var update in client.GetStreamingResponseAsync("Reply with exactly VIDEONOTE_OK.", options, timeout.Token)) text += update.Text;
    if (!text.Contains("VIDEONOTE_OK")) throw new InvalidOperationException();
    Console.WriteLine("PASS: DeepSeek streaming text");
    if (args.Length > 0)
    {
        var bytes = await File.ReadAllBytesAsync(args[0]);
        reply = await client.GetResponseAsync([new ChatMessage(ChatRole.User,
            [new TextContent("Name the main color in this image in one English word."), new DataContent(bytes, "image/png")])], options, timeout.Token);
        if (!reply.Text.Contains("red", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        Console.WriteLine("PASS: DeepSeek image understanding (generated red test image)");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: model probe (" + ex.GetType().Name + "); credential and upstream details suppressed.");
    return 1;
}
return 0;
