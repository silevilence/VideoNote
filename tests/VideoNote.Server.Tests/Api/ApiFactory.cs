using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace VideoNote.Server.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string root = "work-tests/" + Guid.NewGuid().ToString("N");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:RootPath"] = root
        }));
    }
}

