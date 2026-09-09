using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoNote.Client.Pages;
using VideoNote.Server.Components;
using VideoNote.Server.Data;
using VideoNote.Server.Realtime;
using VideoNote.Server.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddControllers();
builder.Services.AddSingleton<VideoNote.Server.Configuration.ProviderSecrets>();
builder.Services.AddSignalR();
builder.Services.AddOptions<UploadOptions>().BindConfiguration("Upload")
    .Validate(o => o.MaxBytes > 0 && o.AllowedExtensions is { Length: > 0 } &&
        o.AllowedExtensions.All(e => !string.IsNullOrWhiteSpace(e) && e.StartsWith('.') && e.Length > 1 &&
            e.Skip(1).All(char.IsAsciiLetterOrDigit)), "上传大小或扩展名配置无效。").ValidateOnStart();
builder.Services.AddSingleton<VideoFileStore>();
builder.Services.AddOptions<VideoNote.Server.Media.FfmpegOptions>()
    .BindConfiguration("Ffmpeg")
    .Validate(o => double.IsFinite(o.SegmentSeconds) && o.SegmentSeconds > 0 &&
        double.IsFinite(o.OverlapSeconds) && o.OverlapSeconds >= 0 && o.OverlapSeconds < o.SegmentSeconds &&
        double.IsFinite(o.FramesPerSecond) && o.FramesPerSecond > 0 && o.FramesPerSecond <= 60 &&
        o.TimeoutSeconds > 0 && !string.IsNullOrWhiteSpace(o.FfmpegPath) && !string.IsNullOrWhiteSpace(o.FfprobePath),
        "FFmpeg 分段、重叠、帧率、超时和程序路径配置无效。")
    .ValidateOnStart();
builder.Services.AddSingleton<VideoNote.Server.Media.MediaProcessRunner>();
builder.Services.AddSingleton<VideoNote.Server.Media.IFfmpegService, VideoNote.Server.Media.FfmpegService>();
builder.Services.AddScoped<VideoNote.Server.AI.IModelChatClientFactory, VideoNote.Server.AI.ChatClientFactory>();

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var options = config.GetSection(WorkDirectoryOptions.SectionName).Get<WorkDirectoryOptions>() ?? new();
    return WorkDirectoryPaths.Create(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath, options);
});
builder.Services.AddSingleton<WorkDirectoryInitializer>();
builder.Services.AddDataProtection().SetApplicationName("VideoNote");
builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
    .Configure<WorkDirectoryPaths>((options, paths) =>
        options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
            new DirectoryInfo(paths.Keys), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));
builder.Services.AddDbContext<VideoNoteDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var paths = sp.GetRequiredService<WorkDirectoryPaths>();
    var connection = new SqliteConnectionStringBuilder(
        config.GetConnectionString("VideoNote") ?? "Data Source=videonote.db");
    if (!Path.IsPathRooted(connection.DataSource))
        connection.DataSource = Path.Combine(paths.Root, connection.DataSource);
    options.UseSqlite(connection.ToString());
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapControllers();
app.MapHub<AnalysisHub>("/hubs/analysis");
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VideoNote.Client._Imports).Assembly);

app.Services.GetRequiredService<WorkDirectoryInitializer>().Initialize();
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<VideoNoteDbContext>();
    await database.Database.MigrateAsync();
}

app.Run();

public partial class Program { }
