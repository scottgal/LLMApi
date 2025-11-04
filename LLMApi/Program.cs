using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using LLMApi.Helpers;
using LLMApi.Extensions;
using LLMApi.Middleware;

// Support optional --config|-c <path> to an external JSON configuration file
static string? GetConfigPath(string[] args)
{
    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (string.Equals(a, "--config", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-c", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length) return args[i + 1];
        }
        else if (a.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
        {
            return a.Substring("--config=".Length);
        }
    }
    return null;
}

var configPath = GetConfigPath(args);

// Show help if requested
if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase)))
{
    HelpDisplay.Show();
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });

// Rebuild configuration sources to ensure precedence and optional external file support.
// Precedence (lowest to highest): appsettings.json -> appsettings.{ENV}.json -> external --config -> env vars -> command line
builder.Configuration.Sources.Clear();

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false);

if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
{
    builder.Configuration.AddJsonFile(configPath!, optional: false, reloadOnChange: false);
}

builder.Configuration
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// Add LLMock API services with bound configuration (supports command-line overrides like --MockLlmApi:BaseUrl=...)
builder.Services.AddLLMockApi(builder.Configuration);

// Add LLMock SignalR services
builder.Services.AddLLMockSignalR(builder.Configuration);

// Add Dashboard with logging
builder.Services.AddDashboard();
builder.Logging.AddDashboardLogging();

var app = builder.Build();

// Display startup banner with configuration info
var options = app.Services.GetRequiredService<IOptions<LLMockApiOptions>>().Value;
StartupBanner.Display(options);

// Use routing
app.UseRouting();

// Add dashboard middleware to track API connections
app.UseMiddleware<DashboardMiddleware>();

// Configure static files with default document support
app.UseDefaultFiles(); // Serves index.html for /
app.UseStaticFiles();

// Map LLMock API endpoints at /api/mock
app.MapLLMockApi("/api/mock", includeStreaming: true);

// Map LLMock SignalR hub and management endpoints
app.MapLLMockSignalR("/hub/mock", "/api/mock");

app.Run();

// Expose Program as partial to support WebApplicationFactory in integration tests
public partial class Program { }
