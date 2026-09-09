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

var storageOptions = builder.Configuration
    .GetSection(WorkDirectoryOptions.SectionName)
    .Get<WorkDirectoryOptions>() ?? new WorkDirectoryOptions();
var workDirectoryPaths = WorkDirectoryPaths.Create(
    builder.Environment.ContentRootPath,
    storageOptions);
Directory.CreateDirectory(workDirectoryPaths.Root);
builder.Services.AddSingleton(workDirectoryPaths);
builder.Services.AddSingleton<WorkDirectoryInitializer>();
builder.Services.AddDataProtection()
    .SetApplicationName("VideoNote")
    .PersistKeysToFileSystem(new DirectoryInfo(workDirectoryPaths.Keys));

var sqliteConnectionString = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("VideoNote") ?? "Data Source=videonote.db");
if (!Path.IsPathRooted(sqliteConnectionString.DataSource))
{
    sqliteConnectionString.DataSource = Path.Combine(
        workDirectoryPaths.Root,
        sqliteConnectionString.DataSource);
}

builder.Services.AddDbContext<VideoNoteDbContext>(options =>
    options.UseSqlite(sqliteConnectionString.ToString()));

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
