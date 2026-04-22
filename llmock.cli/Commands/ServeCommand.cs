using LLMock.Cli.Dashboard;
using LLMock.Cli.Daemon;
using LLMock.Cli.Embedded;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Providers;
using Serilog;
using Serilog.Events;

namespace LLMock.Cli.Commands;

public static class ServeCommand
{
    public static async Task<int> RunAsync(
        int port,
        string[] specs,
        string? backend,
        string? model,
        string? baseUrl,
        string? apiKey,
        string? configFile,
        string? pack,
        bool headless,
        bool daemon,
        CancellationToken ct)
    {
        // Daemon mode: re-launch self with --headless, then exit
        if (daemon)
        {
            var self = Environment.ProcessPath ?? "llmock";
            var daemonArgs = BuildDaemonArgs(port, specs, backend, model, baseUrl, apiKey, configFile, pack);
            var psi = new System.Diagnostics.ProcessStartInfo(self, daemonArgs)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
            };
            System.Diagnostics.Process.Start(psi);
            Console.WriteLine($"  LLMock started in background on :{port}");
            Console.WriteLine("  Run 'llmock status' to check. 'llmock stop' to stop.");
            return 0;
        }

        ConfigureSerilog(headless);

        var builder = WebApplication.CreateBuilder();
        builder.Host.UseSerilog();

        // Load config
        if (!string.IsNullOrWhiteSpace(configFile))
            builder.Configuration.AddJsonFile(configFile, false, true);
        else
            builder.Configuration.AddJsonFile("appsettings.json", true, true);

        builder.Configuration.AddEnvironmentVariables("LLMOCK_");

        if (port == 5000) port = builder.Configuration.GetValue<int?>("LLMockCli:Port") ?? 5555;
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Determine Ollama URL from args or config
        var ollamaUrl = baseUrl ?? builder.Configuration.GetValue<string?>("LLMockApi:LlmBackends:0:BaseUrl") ?? "http://localhost:11434";
        // Strip /v1/ suffix for health check
        var ollamaHealthUrl = ollamaUrl.TrimEnd('/').Replace("/v1", "").TrimEnd('/');

        // Check if Ollama is reachable BEFORE deciding to use embedded model
        var ollamaReachable = await IsOllamaReachableAsync(ollamaHealthUrl);
        if (ollamaReachable)
            Log.Information("Ollama is reachable at {Url} — embedded model will NOT be loaded", ollamaHealthUrl);
        else
            Log.Information("Ollama not reachable at {Url} — embedded model fallback will be used", ollamaHealthUrl);

        // Register LLMock API
        builder.Services.AddLLMockApi(options =>
        {
            builder.Configuration.GetSection("LLMockApi").Bind(options);

            if (!string.IsNullOrWhiteSpace(backend) || !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(baseUrl))
            {
                var backendConfig = new LlmBackendConfig
                {
                    Name = backend ?? "cli",
                    Provider = backend ?? "ollama",
                    ModelName = model ?? "gemma4:4b",
                    BaseUrl = baseUrl ?? "http://localhost:11434/v1/",
                    ApiKey = apiKey,
                    Enabled = true
                };
                options.LlmBackends = options.LlmBackends is { Count: > 0 }
                    ? [backendConfig, ..options.LlmBackends.Skip(1)]
                    : [backendConfig];
            }

            if (options.LlmBackends == null || options.LlmBackends.Count == 0)
                options.LlmBackends =
                [
                    new LlmBackendConfig
                    {
                        Name = "ollama", Provider = "ollama",
                        ModelName = "gemma4:4b", BaseUrl = "http://localhost:11434/v1/", Enabled = true
                    }
                ];

            // Only add embedded backend if Ollama is not reachable
            if (!ollamaReachable)
                options.LlmBackends.Add(new LlmBackendConfig
                {
                    Name = "embedded", Provider = "embedded",
                    ModelName = "qwen3.5-0.8b", Enabled = true
                });
        });

        // Only register EmbeddedLlmProvider in DI if Ollama is not reachable
        EmbeddedLlmProvider? embeddedProvider = null;
        if (!ollamaReachable)
            builder.Services.AddSingleton<EmbeddedLlmProvider>();

        var app = builder.Build();

        // Register embedded provider with the factory (fallback) — only if Ollama is not reachable
        if (!ollamaReachable)
        {
            embeddedProvider = app.Services.GetRequiredService<EmbeddedLlmProvider>();
            var providerFactory = app.Services.GetRequiredService<LlmProviderFactory>();
            providerFactory.RegisterProvider(EmbeddedLlmProvider.ProviderName, embeddedProvider);

            // Now download/load the embedded model
            var modelOpts = new EmbeddedModelOptions();
            if (!headless)
            {
                Console.WriteLine($"\n  LLMock v{GetVersion()}");
                Console.WriteLine("  Ollama not found — checking embedded model...");
            }

            try
            {
                var modelPath = await ModelDownloader.EnsureModelAsync(modelOpts, ct);
                embeddedProvider.LoadModel(modelPath);
            }
            catch (Exception ex)
            {
                Log.Warning("Embedded model unavailable: {Message}. Starting without LLM backend.", ex.Message);
                if (!headless)
                    Console.WriteLine($"  Embedded model unavailable: {ex.Message}");
            }
        }
        else if (!headless)
        {
            Console.WriteLine($"\n  LLMock v{GetVersion()} — using Ollama backend");
        }

        app.UseSerilogRequestLogging(o =>
        {
            o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
            o.GetLevel = (ctx, elapsed, ex) =>
                ex != null ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning
                : LogEventLevel.Information;
        });

        app.MapLLMockApi();
        app.MapLLMockGraphQL("/api");

        // Dashboard stats endpoint
        app.MapGet("/api/dashboard/stats", (OpenApiContextManager contextManager) =>
        {
            var apiContexts = contextManager.GetAllContexts();
            var totalRequests = apiContexts.Sum(c => c.TotalCalls);
            var activeApiContexts = apiContexts.Count;
            return Results.Json(new
            {
                timestamp = DateTime.UtcNow,
                activeContexts = activeApiContexts,
                totalRequests,
                apiContexts = apiContexts.Select(c => new
                {
                    name = c.Name,
                    calls = c.TotalCalls,
                    lastUsed = c.LastUsedAt
                }).ToList()
            });
        });

        // Daemon IPC socket
        await using var daemonController = new DaemonController();
        await daemonController.StartServerAsync(ct);

        // Broadcast stats every second
        _ = BroadcastStatsAsync(daemonController, port, ct);

        // Load OpenAPI specs
        if (specs.Length > 0)
            await LoadSpecsAsync(app, specs);

        // Start server
        var serverTask = app.RunAsync(ct);

        if (!headless)
        {
            // Open dashboard
            var poller = new DashboardPoller(port);
            var renderer = new DashboardRenderer(poller);
            await renderer.RunAsync(ct);
        }
        else
        {
            Log.Information("LLMock listening on :{Port} (headless)", port);
            await serverTask;
        }

        return 0;
    }

    private static async Task<bool> IsOllamaReachableAsync(string baseUrl)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await http.GetAsync(baseUrl);
            // Any response (even 404) means the server is up
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task BroadcastStatsAsync(DaemonController daemon, int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await http.GetStringAsync($"http://localhost:{port}/api/dashboard/stats", ct);
                await daemon.BroadcastAsync(new { type = "stats", data = json });
            }
            catch { /* not ready yet */ }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task LoadSpecsAsync(WebApplication app, string[] specs)
    {
        var openApiManager = app.Services.GetRequiredService<DynamicOpenApiManager>();
        foreach (var (spec, i) in specs.Select((s, i) => (s, i)))
            try
            {
                var specName = Path.GetFileNameWithoutExtension(spec)?.Replace(".", "-") ?? $"spec{i}";
                var result = await openApiManager.LoadSpecAsync(specName, spec, $"/api/spec{i}");
                Log.Information("Loaded spec '{Name}' — {Count} endpoints at /api/spec{I}", specName, result.EndpointCount, i);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load spec: {Spec}", spec);
            }
    }

    private static string BuildDaemonArgs(int port, string[] specs, string? backend, string? model,
        string? baseUrl, string? apiKey, string? configFile, string? pack)
    {
        var parts = new List<string> { "serve", "--headless", "--port", port.ToString() };
        if (backend != null) { parts.Add("--backend"); parts.Add(backend); }
        if (model != null) { parts.Add("--model"); parts.Add(model); }
        if (baseUrl != null) { parts.Add("--base-url"); parts.Add(baseUrl); }
        if (apiKey != null) { parts.Add("--api-key"); parts.Add(apiKey); }
        if (configFile != null) { parts.Add("--config"); parts.Add(configFile); }
        if (pack != null) { parts.Add("--pack"); parts.Add(pack); }
        foreach (var spec in specs) { parts.Add("--spec"); parts.Add(spec); }
        return string.Join(" ", parts);
    }

    private static string GetVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "2.4.0";

    private static void ConfigureSerilog(bool headless)
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext();

        if (headless)
            config = config
                .WriteTo.File(
                    DaemonController.LogFilePath,
                    LogEventLevel.Information,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        else
            config = config
                .WriteTo.Console(LogEventLevel.Information)
                .WriteTo.File(
                    "logs/llmock-.log",
                    LogEventLevel.Warning,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);

        Log.Logger = config.CreateLogger();
    }
}
