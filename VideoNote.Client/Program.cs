using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VideoNote.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ApiClient>();
await builder.Build().RunAsync();
