using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using VideoNote.Server.Storage;

namespace VideoNote.Server.Tests.Api;

public sealed class ApiFactory(Dictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    private readonly string root = "work-tests/" + Guid.NewGuid().ToString("N");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?> { ["Storage:RootPath"] = root, ["Logging:LogLevel:Default"] = "None" };
            if (overrides is not null) foreach (var (key, value) in overrides) values[key] = value;
            config.AddInMemoryCollection(values);
        });
    }
    public override async ValueTask DisposeAsync()
    {
        var directory = Services.GetRequiredService<WorkDirectoryPaths>().Root;
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (directory.EndsWith(root.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
