using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;
using Serilog;
using Serilog.Events;

namespace LLMock.Cli;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Simple arg parsing for MVP
        var port = 5000;
        var specs = new List<string>();
        string? backend = null;
        string? model = null;
        string? baseUrl = null;
        string? apiKey = null;
        string? configFile = null;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--port" or "-p" when i + 1 < args.Length:
                    port = int.Parse(args[++i]);
                    break;
                case "--spec" or "-s" when i + 1 < args.Length:
                    specs.Add(args[++i]);
                    break;
                case "--backend" or "-b" when i + 1 < args.Length:
                    backend = args[++i];
                    break;
                case "--model" or "-m" when i + 1 < args.Length:
                    model = args[++i];
                    break;
                case "--base-url" when i + 1 < args.Length:
                    baseUrl = args[++i];
                    break;
                case "--api-key" or "-k" when i + 1 < args.Length:
                    apiKey = args[++i];
                    break;
                case "--config" or "-c" when i + 1 < args.Length:
                    configFile = args[++i];
                    break;
                case "serve":
                    // Command - just continue
                    break;
                case "--help" or "-h":
                    ShowHelp();
                    return 0;
                default:
                    if (!args[i].StartsWith('-'))
                        // Treat as spec file
                        specs.Add(args[i]);
                    break;
            }

        try
        {
            await RunServer(specs.ToArray(), port, backend, model, baseUrl, apiKey, configFile);
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n✗ Fatal error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
                          LLMock CLI - LLM-powered mock API server with OpenAPI support

                          USAGE:
                              llmock [command] [options]

                          COMMANDS:
                              serve                           Start the mock API server (default)

                          OPTIONS:
                              --port, -p <port>              Server port (default: 5555 or from config)
                              --spec, -s <file-or-url>       OpenAPI spec file or URL (repeatable)
                              --backend, -b <provider>       LLM backend (ollama, openai, lmstudio)
                              --model, -m <model>            Model name
                              --base-url <url>               LLM backend base URL
                              --api-key, -k <key>            API key for LLM backend
                              --config, -c <file>            Path to appsettings.json file
                              --help, -h                     Show this help

                          EXAMPLES:
                              llmock serve
                              llmock serve --port 8080
                              llmock serve --spec petstore.yaml
                              llmock serve --backend openai --model gpt-4o-mini --api-key sk-...
                              llmock serve --spec https://petstore3.swagger.io/api/v3/openapi.json

                          LOGS:
                              Console: Info and above
                              File: logs/llmock-.log (Warning and above)

                          """);
    }

    private static async Task RunServer(
        string[] specs,
        int port,
        string? backend,
        string? model,
        string? baseUrl,
        string? apiKey,
        string? configFile)
    {
        // Configure Serilog early
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                LogEventLevel.Information)
            .WriteTo.File(
                "logs/llmock-.log",
                LogEventLevel.Warning,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var builder = WebApplication.CreateBuilder();

        // Use Serilog
        builder.Host.UseSerilog();

        // Load configuration first
        if (!string.IsNullOrWhiteSpace(configFile))
        {
            builder.Configuration.AddJsonFile(configFile, false, true);
            Log.Information("Loaded configuration from {ConfigFile}", configFile);
        }
        else
        {
            builder.Configuration.AddJsonFile("appsettings.json", true, true);
        }

        // Add environment variables support
        builder.Configuration.AddEnvironmentVariables("LLMOCK_");

        // Read port from config if not specified on command line (default 5000 means not specified)
        if (port == 5000)
        {
            port = builder.Configuration.GetValue<int?>("LLMockCli:Port") ?? 5555;
        }

        // Configure URLs
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Configure LLMock API with options
        builder.Services.AddLLMockApi(options =>
        {
            // Load from configuration first
            builder.Configuration.GetSection("LLMockApi").Bind(options);

            // Override with CLI arguments if provided
            if (!string.IsNullOrWhiteSpace(backend) || !string.IsNullOrWhiteSpace(model) ||
                !string.IsNullOrWhiteSpace(baseUrl))
            {
                // Create or update backend config from CLI args
                var backendConfig = new LlmBackendConfig
                {
                    Name = backend ?? "cli",
                    Provider = backend ?? "ollama",
                    ModelName = model ?? "llama3",
                    BaseUrl = baseUrl ?? "http://localhost:11434/v1/",
                    ApiKey = apiKey,
                    Enabled = true
                };

                if (options.LlmBackends == null || options.LlmBackends.Count == 0)
                    options.LlmBackends = [backendConfig];
                else
                    // Replace first backend with CLI config
                    options.LlmBackends[0] = backendConfig;

                Log.Information("LLM Backend configured: {Provider}/{Model} at {BaseUrl}",
                    backendConfig.Provider, backendConfig.ModelName, backendConfig.BaseUrl);
            }

            // Set reasonable defaults if nothing configured
            if (options.LlmBackends == null || options.LlmBackends.Count == 0)
            {
                options.LlmBackends =
                [
                    new LlmBackendConfig
                    {
                        Name = "ollama",
                        Provider = "ollama",
                        ModelName = "llama3",
                        BaseUrl = "http://localhost:11434/v1/",
                        Enabled = true
                    }
                ];

                Log.Information("Using default LLM backend: ollama/llama3 at http://localhost:11434/v1/");
            }
        });

        var app = builder.Build();

        // Add Serilog request logging with custom configuration
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                if (ex != null) return LogEventLevel.Error;
                if (httpContext.Response.StatusCode >= 500) return LogEventLevel.Error;
                if (httpContext.Response.StatusCode >= 400) return LogEventLevel.Warning;
                if (elapsed > 10000) return LogEventLevel.Warning; // Slow requests
                return LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());

                // Add backend info if present
                if (httpContext.Request.Query.ContainsKey("backend"))
                    diagnosticContext.Set("Backend", httpContext.Request.Query["backend"].ToString());
            };
        });

        // Map LLMock API endpoints (includes management endpoints)
        app.MapLLMockApi();

        // Read catch-all configuration
        var catchAllPath = builder.Configuration.GetValue<string?>("LLMockCli:CatchAllMockPath");
        var showDetailedErrors = builder.Configuration.GetValue<bool>("LLMockCli:ShowDetailedErrors", true);

        // Add catch-all route for any unmatched paths (acts as dynamic mock)
        // This comes AFTER specific routes so management endpoints take precedence
        if (!string.IsNullOrWhiteSpace(catchAllPath))
        {
            Log.Information("Catch-all mock enabled for paths starting with: {CatchAllPath}", catchAllPath);

            app.MapFallback(async (HttpContext context) =>
            {
                var path = context.Request.Path.Value ?? "/";

                // Check if path matches catch-all prefix
                if (catchAllPath != "/" && !path.StartsWith(catchAllPath, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 404;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"error\":\"Not Found\",\"path\":\"{path.Replace("\"", "\\\"")}\"}}");
                    return;
                }

                // Skip management endpoints (always excluded)
                if (path.StartsWith("/api/openapi/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/api/contexts", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/api/grpc-protos", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/api/signalr/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/api/graphql", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 404;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"error\":\"Not Found\",\"path\":\"{path.Replace("\"", "\\\"")}\"}}");
                    return;
                }

                // For all other paths, generate mock response
                Log.Information("Generating mock response for catch-all path: {Method} {Path}",
                    context.Request.Method, path);

                try
                {
                    var service = context.RequestServices.GetRequiredService<LLMockApiService>();
                    var method = context.Request.Method;
                    var query = context.Request.QueryString.Value;
                    var body = await service.ReadBodyAsync(context.Request);
                    var fullPathWithQuery = path + (query ?? "");

                    var handler = context.RequestServices.GetRequiredService<RegularRequestHandler>();
                    var response = await handler.HandleRequestAsync(method, fullPathWithQuery, body, context.Request, context);

                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(response);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error generating mock response for {Path}", path);
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";

                    if (showDetailedErrors)
                    {
                        var stackTrace = ex.StackTrace?.Replace("\"", "\\\"").Replace("\r\n", "\\n").Replace("\n", "\\n") ?? "";
                        await context.Response.WriteAsync($"{{\"error\":\"Internal Server Error\",\"message\":\"{ex.Message.Replace("\"", "\\\"")}\",\"stackTrace\":\"{stackTrace}\"}}");
                    }
                    else
                    {
                        await context.Response.WriteAsync("{\"error\":\"Internal Server Error\",\"message\":\"An error occurred while processing your request\"}");
                    }
                }
            });
        }
        else
        {
            Log.Information("Catch-all mock disabled - only explicit routes will be served");
        }

        Console.WriteLine($"""

                           ╔═══════════════════════════════════════════════════════════╗
                           ║                    LLMock CLI Server                      ║
                           ╚═══════════════════════════════════════════════════════════╝

                           🚀 Server starting on: http://localhost:{port}

                           """);

        Log.Information("LLMock CLI Server starting on http://localhost:{Port}", port);

        // Load OpenAPI specs if provided via CLI
        if (specs.Length > 0)
        {
            var openApiManager = app.Services.GetRequiredService<DynamicOpenApiManager>();

            Console.WriteLine("📋 Loading OpenAPI specifications...\n");
            Log.Information("Loading {SpecCount} OpenAPI specifications", specs.Length);

            foreach (var (spec, index) in specs.Select((s, i) => (s, i)))
                try
                {
                    var specName = Path.GetFileNameWithoutExtension(spec)?.Replace(".", "-") ?? $"spec{index}";
                    var specBasePath = $"/api/spec{index}";

                    Console.Write($"   Loading: {spec}... ");

                    var result = await openApiManager.LoadSpecAsync(
                        specName,
                        spec,
                        specBasePath);

                    Console.WriteLine($"✓ ({result.EndpointCount} endpoints at {specBasePath})");
                    Log.Information(
                        "Loaded OpenAPI spec '{SpecName}' from {Source} - {EndpointCount} endpoints at {BasePath}",
                        specName, spec, result.EndpointCount, specBasePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed: {ex.Message}");
                    Log.Error(ex, "Failed to load OpenAPI spec from {Source}", spec);
                }

            Console.WriteLine();
        }

        var catchAllStatus = string.IsNullOrWhiteSpace(catchAllPath)
            ? "Disabled - only explicit routes"
            : catchAllPath == "/"
                ? "Enabled - ALL paths (except management)"
                : $"Enabled - paths starting with {catchAllPath}";

        Console.WriteLine($$"""
                            📚 Available endpoints:
                               • /api/mock/**           - Shape-based mock endpoints
                               • /api/mock/stream/**    - Streaming mock endpoints
                               • /api/openapi/specs     - Manage OpenAPI specs
                               • /api/contexts          - View API contexts

                            🎯 Catch-all mock: {{catchAllStatus}}

                            💡 Tips:
                               • Load more specs at runtime via POST /api/openapi/specs
                               • Configure catch-all in appsettings.json (LLMockCli:CatchAllMockPath)

                            📝 Logging:
                               • Console: Info and above
                               • File: logs/llmock-{date}.log (Warning and above)

                            Press Ctrl+C to stop
                            ════════════════════════════════════════════════════════════

                            """);

        Log.Information("Server ready - listening for connections");

        // Log when a connection is established
        app.Lifetime.ApplicationStarted.Register(() => { Log.Information("Application started successfully"); });

        app.Lifetime.ApplicationStopping.Register(() => { Log.Information("Application shutting down..."); });

        await app.RunAsync();
    }
}