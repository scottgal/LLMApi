using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Extension methods for adding LLMock API to ASP.NET Core applications
/// </summary>
public static class LlMockApiExtensions
{
    /// <summary>
    /// Adds ALL LLMock API services to the service collection (REST, Streaming, GraphQL)
    /// For modular setup, use AddLLMockRest/AddLLMockStreaming/AddLLMockGraphQL instead
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing MockLlmApi section</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
        RegisterCoreServices(services);
        RegisterRestServices(services);
        RegisterStreamingServices(services);
        RegisterGraphQLServices(services);
        return services;
    }

    /// <summary>
    /// Adds ALL LLMock API services to the service collection with inline configuration (REST, Streaming, GraphQL)
    /// For modular setup, use AddLLMockRest/AddLLMockStreaming/AddLLMockGraphQL instead
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockApi(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        services.Configure(configure);
        RegisterCoreServices(services);
        RegisterRestServices(services);
        RegisterStreamingServices(services);
        RegisterGraphQLServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock REST API services (non-streaming)
    /// </summary>
    public static IServiceCollection AddLLMockRest(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
        RegisterCoreServices(services);
        RegisterRestServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock REST API services with inline configuration
    /// </summary>
    public static IServiceCollection AddLLMockRest(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        services.Configure(configure);
        RegisterCoreServices(services);
        RegisterRestServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock SSE Streaming services
    /// </summary>
    public static IServiceCollection AddLLMockStreaming(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
        RegisterCoreServices(services);
        RegisterStreamingServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock SSE Streaming services with inline configuration
    /// </summary>
    public static IServiceCollection AddLLMockStreaming(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        services.Configure(configure);
        RegisterCoreServices(services);
        RegisterStreamingServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock GraphQL services
    /// </summary>
    public static IServiceCollection AddLLMockGraphQL(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
        RegisterCoreServices(services);
        RegisterGraphQLServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock GraphQL services with inline configuration
    /// </summary>
    public static IServiceCollection AddLLMockGraphQL(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        services.Configure(configure);
        RegisterCoreServices(services);
        RegisterGraphQLServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock SignalR services to the service collection
    /// NOTE: This assumes core options have been configured via another Add method
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing MockLlmApi section (not used, kept for compatibility)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockSignalR(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        RegisterCoreServices(services);
        RegisterSignalRServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock SignalR services to the service collection with inline configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockSignalR(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        services.Configure(configure);
        RegisterCoreServices(services);
        RegisterSignalRServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock OpenAPI services for generating mock endpoints from OpenAPI/Swagger specs
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing MockLlmApi section with OpenApiSpecs</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockOpenApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
        RegisterCoreServices(services);
        RegisterOpenApiServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock OpenAPI services with inline configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure options including OpenApiSpecs</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockOpenApi(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        services.Configure(configure);
        RegisterCoreServices(services);
        RegisterOpenApiServices(services);
        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        // Only register if not already registered (allows modular usage)
        if (services.All(x => x.ServiceType != typeof(LlmClient)))
        {
            services.AddHttpClient("LLMockApi");
            services.AddScoped<ShapeExtractor>();
            services.AddScoped<ContextExtractor>();
            services.AddScoped<PromptBuilder>();
            services.AddScoped<LlmClient>();
            services.AddSingleton<CacheManager>();
            services.AddScoped<DelayHelper>();
            services.AddScoped<LLMockApiService>();
        }
    }

    private static void RegisterRestServices(IServiceCollection services)
    {
        if (services.All(x => x.ServiceType != typeof(RegularRequestHandler)))
        {
            services.AddScoped<RegularRequestHandler>();
        }
    }

    private static void RegisterStreamingServices(IServiceCollection services)
    {
        if (services.All(x => x.ServiceType != typeof(StreamingRequestHandler)))
        {
            services.AddScoped<StreamingRequestHandler>();
        }
    }

    private static void RegisterGraphQLServices(IServiceCollection services)
    {
        if (services.All(x => x.ServiceType != typeof(GraphQLRequestHandler)))
        {
            services.AddScoped<GraphQLRequestHandler>();
        }
    }

    private static void RegisterSignalRServices(IServiceCollection services)
    {
        if (services.All(x => x.ServiceType != typeof(DynamicHubContextManager)))
        {
            services.AddSignalR();
            services.AddSingleton<DynamicHubContextManager>();
            services.AddHostedService<MockDataBackgroundService>();
        }
    }

    private static void RegisterOpenApiServices(IServiceCollection services)
    {
        if (services.All(x => x.ServiceType != typeof(OpenApiSpecLoader)))
        {
            services.AddScoped<OpenApiSpecLoader>();
            services.AddScoped<OpenApiSchemaConverter>();
            services.AddScoped<OpenApiRequestHandler>();
            services.AddSingleton<DynamicOpenApiManager>();
            services.AddSingleton<OpenApiContextManager>();
            services.AddSignalR(); // Ensure SignalR is registered for OpenApiHub
        }
    }

    /// <summary>
    /// Maps ALL LLMock API endpoints to the application (REST + optionally Streaming + optionally GraphQL)
    /// For modular setup, use MapLLMockRest/MapLLMockStreaming/MapLLMockGraphQL instead
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The route pattern (e.g., "/api/mock" or "/demo")</param>
    /// <param name="includeStreaming">Whether to include streaming endpoint (default: true)</param>
    /// <param name="includeGraphQL">Whether to include GraphQL endpoint (default: true)</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockApi(
        this IApplicationBuilder app,
        string pattern = "/api/mock",
        bool includeStreaming = true,
        bool includeGraphQL = true)
    {
        MapLLMockRest(app, pattern);

        if (includeStreaming)
            MapLLMockStreaming(app, pattern);

        if (includeGraphQL)
            MapLLMockGraphQL(app, pattern);

        return app;
    }

    /// <summary>
    /// Maps LLMock REST API endpoints (non-streaming)
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The route pattern (e.g., "/api/mock")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockRest(
        this IApplicationBuilder app,
        string pattern = "/api/mock")
    {
        if (app is not IEndpointRouteBuilder routeBuilder)
        {
            throw new InvalidOperationException(
                "MapLLMockRest requires endpoint routing. Call UseRouting() before MapLLMockRest().");
        }

        var autoPattern = $"{pattern.TrimEnd('/')}/{{**path}}";

        routeBuilder.MapMethods(autoPattern, new[] { "GET", "POST", "PUT", "DELETE", "PATCH" },
            HandleRegularRequest)
            .WithName($"LLMockApi-Rest-{pattern}");

        return app;
    }

    /// <summary>
    /// Maps LLMock SSE Streaming endpoints
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The route pattern (e.g., "/api/mock")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockStreaming(
        this IApplicationBuilder app,
        string pattern = "/api/mock")
    {
        if (app is not IEndpointRouteBuilder routeBuilder)
        {
            throw new InvalidOperationException(
                "MapLLMockStreaming requires endpoint routing. Call UseRouting() before MapLLMockStreaming().");
        }

        var streamPattern = $"{pattern.TrimEnd('/')}/stream/{{**path}}";

        routeBuilder.MapMethods(streamPattern, new[] { "GET", "POST", "PUT", "DELETE", "PATCH" },
            HandleStreamingRequest)
            .WithName($"LLMockApi-Streaming-{pattern}");

        return app;
    }

    /// <summary>
    /// Maps LLMock GraphQL endpoint
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The route pattern (e.g., "/api/mock")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockGraphQL(
        this IApplicationBuilder app,
        string pattern = "/api/mock")
    {
        if (app is not IEndpointRouteBuilder routeBuilder)
        {
            throw new InvalidOperationException(
                "MapLLMockGraphQL requires endpoint routing. Call UseRouting() before MapLLMockGraphQL().");
        }

        var graphqlPattern = $"{pattern.TrimEnd('/')}/graphql";

        routeBuilder.MapPost(graphqlPattern, HandleGraphQLRequest)
            .WithName($"LLMockApi-GraphQL-{pattern}");

        return app;
    }

    /// <summary>
    /// Maps LLMock SignalR hub and management endpoints to the application
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="hubPattern">SignalR hub pattern (default: "/hub/mock")</param>
    /// <param name="managementPattern">Management API pattern (default: "/api/mock")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockSignalR(
        this IApplicationBuilder app,
        string hubPattern = "/hub/mock",
        string managementPattern = "/api/mock")
    {
        if (app is not IEndpointRouteBuilder routeBuilder)
        {
            throw new InvalidOperationException(
                "MapLLMockSignalR requires endpoint routing. Call UseRouting() before MapLLMockSignalR().");
        }

        // Map SignalR hub
        routeBuilder.MapHub<Hubs.MockLlmHub>(hubPattern);

        // Add dynamic context management endpoints
        var contextPattern = $"{managementPattern.TrimEnd('/')}/contexts";

        routeBuilder.MapPost(contextPattern, SignalRManagementEndpoints.HandleCreateDynamicContext)
            .WithName("LLMockApi-CreateContext");

        routeBuilder.MapGet(contextPattern, (DynamicHubContextManager manager, IConfiguration config) => SignalRManagementEndpoints.HandleListContexts(manager, config))
            .WithName("LLMockApi-ListContexts");

        routeBuilder.MapGet($"{contextPattern}/{{contextName}}", (string contextName, DynamicHubContextManager manager, IConfiguration config) => SignalRManagementEndpoints.HandleGetContext(contextName, manager, config))
            .WithName("LLMockApi-GetContext");

        routeBuilder.MapDelete($"{contextPattern}/{{contextName}}", SignalRManagementEndpoints.HandleDeleteContext)
            .WithName("LLMockApi-DeleteContext");

        routeBuilder.MapPost($"{contextPattern}/{{contextName}}/start", SignalRManagementEndpoints.HandleStartContext)
            .WithName("LLMockApi-StartContext");

        routeBuilder.MapPost($"{contextPattern}/{{contextName}}/stop", SignalRManagementEndpoints.HandleStopContext)
            .WithName("LLMockApi-StopContext");

        return app;
    }

    /// <summary>
    /// Maps LLMock OpenAPI endpoints based on configured OpenAPI specs
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockOpenApi(this IApplicationBuilder app)
    {
        if (app is not IEndpointRouteBuilder routeBuilder)
        {
            throw new InvalidOperationException(
                "MapLLMockOpenApi requires endpoint routing. Call UseRouting() before MapLLMockOpenApi().");
        }

        // Get the options to read configured OpenAPI specs
        var services = app.ApplicationServices;
        var options = services.GetRequiredService<IOptions<LLMockApiOptions>>().Value;

        // Load and map each configured OpenAPI spec
        foreach (var specConfig in options.OpenApiSpecs)
        {
            // Create a scope to resolve scoped services during startup
            using var scope = services.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<OpenApiSpecLoader>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<OpenApiSpecLoader>>();

            try
            {
                // Load the spec synchronously during startup (this is acceptable for initialization)
                var document = loader.LoadSpecAsync(specConfig.Source).GetAwaiter().GetResult();

                // Determine base path
                var basePath = specConfig.BasePath ??
                              document.Servers?.FirstOrDefault()?.Url ??
                              "/api";

                logger.LogInformation("Mapping OpenAPI spec '{Name}' at base path: {BasePath}",
                    specConfig.Name, basePath);

                // Get all operations from the spec
                var operations = loader.GetOperations(document);

                // Capture contextName for closures
                var contextName = specConfig.ContextName;

                foreach (var (path, method, operation) in operations)
                {
                    // Apply filtering if configured
                    if (ShouldSkipOperation(operation, path, specConfig))
                        continue;

                    // Build full route path
                    var routePath = $"{basePath.TrimEnd('/')}{path}";

                    // Map the endpoint
                    var methodName = method.ToString().ToUpperInvariant();
                    routeBuilder.MapMethods(routePath, new[] { methodName },
                        async (HttpContext ctx) =>
                        {
                            var handler = ctx.RequestServices.GetRequiredService<OpenApiRequestHandler>();
                            var response = await handler.HandleRequestAsync(
                                ctx, document, path, method, operation, contextName, ctx.RequestAborted);

                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync(response);
                        })
                        .WithName($"LLMockApi-OpenApi-{specConfig.Name}-{methodName}-{path.Replace("/", "-")}");

                    logger.LogDebug("Mapped {Method} {Path} from OpenAPI spec '{Name}'",
                        methodName, routePath, specConfig.Name);

                    // Optionally map streaming endpoint
                    if (specConfig.EnableStreaming)
                    {
                        var streamPath = $"{routePath}/stream";
                        routeBuilder.MapMethods(streamPath, new[] { methodName },
                            async (HttpContext ctx) =>
                            {
                                ctx.Response.ContentType = "text/event-stream";
                                ctx.Response.Headers["Cache-Control"] = "no-cache";
                                ctx.Response.Headers["Connection"] = "keep-alive";

                                var handler = ctx.RequestServices.GetRequiredService<OpenApiRequestHandler>();
                                await foreach (var chunk in handler.HandleStreamingRequestAsync(
                                    ctx, document, path, method, operation, ctx.RequestAborted))
                                {
                                    await ctx.Response.WriteAsync($"data: {chunk}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                }
                            })
                            .WithName($"LLMockApi-OpenApi-{specConfig.Name}-{methodName}-{path.Replace("/", "-")}-Stream");

                        logger.LogDebug("Mapped streaming {Method} {Path} from OpenAPI spec '{Name}'",
                            methodName, streamPath, specConfig.Name);
                    }
                }

                logger.LogInformation("Successfully mapped OpenAPI spec '{Name}' with {Count} operations",
                    specConfig.Name, operations.Count());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to map OpenAPI spec '{Name}' from source: {Source}",
                    specConfig.Name, specConfig.Source);
            }
        }

        return app;
    }

    private static bool ShouldSkipOperation(OpenApiOperation operation, string path, OpenApiSpecConfig config)
    {
        // Check tag filtering
        if (config.IncludeTags?.Count > 0)
        {
            if (operation.Tags == null || !operation.Tags.Any(t => config.IncludeTags.Contains(t.Name)))
                return true;
        }

        if (config.ExcludeTags?.Count > 0)
        {
            if (operation.Tags?.Any(t => config.ExcludeTags.Contains(t.Name)) == true)
                return true;
        }

        // Check path filtering (simple wildcard support)
        if (config.IncludePaths?.Count > 0)
        {
            if (!config.IncludePaths.Any(pattern => PathMatchesPattern(path, pattern)))
                return true;
        }

        if (config.ExcludePaths?.Count > 0)
        {
            if (config.ExcludePaths.Any(pattern => PathMatchesPattern(path, pattern)))
                return true;
        }

        return false;
    }

    private static bool PathMatchesPattern(string path, string pattern)
    {
        // Simple wildcard matching: "/users/*" matches "/users/anything"
        if (pattern.EndsWith("/*"))
        {
            var prefix = pattern[..^2]; // Remove "/*"
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Exact match
        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps OpenAPI management endpoints for dynamic spec loading
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The route pattern for management endpoints (default: "/api/openapi")</param>
    /// <param name="hubPattern">The route pattern for SignalR hub (default: "/hub/openapi")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockOpenApiManagement(
        this IApplicationBuilder app,
        string pattern = "/api/openapi",
        string hubPattern = "/hub/openapi")
    {
        if (app is not IEndpointRouteBuilder routeBuilder)
        {
            throw new InvalidOperationException(
                "MapLLMockOpenApiManagement requires endpoint routing. Call UseRouting() before MapLLMockOpenApiManagement().");
        }

        // Map SignalR hub for real-time updates
        routeBuilder.MapHub<Hubs.OpenApiHub>(hubPattern);

        var specsPattern = $"{pattern.TrimEnd('/')}/specs";

        // List all loaded specs
        routeBuilder.MapGet(specsPattern, OpenApiManagementEndpoints.HandleListSpecs)
            .WithName("LLMockApi-OpenApi-ListSpecs");

        // Load a new spec
        routeBuilder.MapPost(specsPattern, OpenApiManagementEndpoints.HandleLoadSpec)
            .WithName("LLMockApi-OpenApi-LoadSpec");

        // Get specific spec details
        routeBuilder.MapGet($"{specsPattern}/{{specName}}", OpenApiManagementEndpoints.HandleGetSpec)
            .WithName("LLMockApi-OpenApi-GetSpec");

        // Delete a spec
        routeBuilder.MapDelete($"{specsPattern}/{{specName}}", OpenApiManagementEndpoints.HandleDeleteSpec)
            .WithName("LLMockApi-OpenApi-DeleteSpec");

        // Reload a spec
        routeBuilder.MapPost($"{specsPattern}/{{specName}}/reload", OpenApiManagementEndpoints.HandleReloadSpec)
            .WithName("LLMockApi-OpenApi-ReloadSpec");

        // Test an endpoint
        routeBuilder.MapPost($"{pattern.TrimEnd('/')}/test", OpenApiManagementEndpoints.HandleTestEndpoint)
            .WithName("LLMockApi-OpenApi-TestEndpoint");

        // Context management endpoints
        var contextsPattern = $"{pattern.TrimEnd('/')}/contexts";

        // List all contexts
        routeBuilder.MapGet(contextsPattern, (OpenApiContextManager manager) => OpenApiManagementEndpoints.HandleListApiContexts(manager))
            .WithName("LLMockApi-OpenApi-ListContexts");

        // Get specific context details
        routeBuilder.MapGet($"{contextsPattern}/{{contextName}}", (string contextName, OpenApiContextManager manager) => OpenApiManagementEndpoints.HandleGetApiContext(contextName, manager))
            .WithName("LLMockApi-OpenApi-GetContext");

        // Clear a specific context
        routeBuilder.MapDelete($"{contextsPattern}/{{contextName}}", (string contextName, OpenApiContextManager manager) => OpenApiManagementEndpoints.HandleClearApiContext(contextName, manager))
            .WithName("LLMockApi-OpenApi-ClearContext");

        // Clear all contexts
        routeBuilder.MapDelete(contextsPattern, (OpenApiContextManager manager) => OpenApiManagementEndpoints.HandleClearAllApiContexts(manager))
            .WithName("LLMockApi-OpenApi-ClearAllContexts");

        return app;
    }

    private static async Task HandleRegularRequest(
        HttpContext ctx,
        string path,
        LLMockApiService service,
        ILogger<LLMockApiService> logger)
    {
        try
        {
            var method = ctx.Request.Method;
            var query = ctx.Request.QueryString.Value;
            var body = await service.ReadBodyAsync(ctx.Request);

            // Extract pattern from route
            var pattern = GetPatternFromPath(ctx.Request.Path, path);

            var content = await service.HandleRequestAsync(
                method,
                $"{pattern}/{path}{query}",
                body,
                ctx.Request,
                ctx,
                ctx.RequestAborted);

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing LLMock API request");
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    private static async Task HandleStreamingRequest(
        HttpContext ctx,
        string path,
        LLMockApiService service,
        ILogger<LLMockApiService> logger)
    {
        try
        {
            var method = ctx.Request.Method;
            var query = ctx.Request.QueryString.Value;
            var body = await service.ReadBodyAsync(ctx.Request);

            // Extract pattern from route
            var pattern = GetPatternFromPath(ctx.Request.Path, path, isStreaming: true);

            await service.HandleStreamingRequestAsync(
                method,
                $"{pattern}/stream/{path}{query}",
                body,
                ctx.Request,
                ctx,
                ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing LLMock API streaming request");
            await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }

    private static string GetPatternFromPath(string fullPath, string capturedPath, bool isStreaming = false)
    {
        var pathStr = fullPath.ToString();
        var suffix = isStreaming ? $"/stream/{capturedPath}" : $"/{capturedPath}";
        if (pathStr.EndsWith(suffix))
        {
            return pathStr.Substring(0, pathStr.Length - suffix.Length);
        }
        return pathStr;
    }

    private static async Task HandleGraphQLRequest(
        HttpContext ctx,
        GraphQLRequestHandler handler,
        LLMockApiService service,
        ILogger<GraphQLRequestHandler> logger)
    {
        try
        {
            var body = await service.ReadBodyAsync(ctx.Request);

            var content = await handler.HandleGraphQLRequestAsync(
                body,
                ctx.Request,
                ctx,
                ctx.RequestAborted);

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing GraphQL request");
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                data = (object?)null,
                errors = new[]
                {
                    new
                    {
                        message = ex.Message,
                        extensions = new { code = "INTERNAL_SERVER_ERROR" }
                    }
                }
            }));
        }
    }

    /// <summary>
    /// DEPRECATED: Use AddLLMockApi instead. This method will be removed in a future version.
    /// Adds LLMock API services to the service collection using appsettings configuration
    /// </summary>
    [Obsolete("Use AddLLMockApi instead. This method will be removed in v2.0.0.", false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection Addmostlylucid_mockllmapi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddLLMockApi(configuration);
    }

    /// <summary>
    /// DEPRECATED: Use AddLLMockApi instead. This method will be removed in a future version.
    /// Adds LLMock API services to the service collection with inline configuration
    /// </summary>
    [Obsolete("Use AddLLMockApi instead. This method will be removed in v2.0.0.", false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection Addmostlylucid_mockllmapi(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        return services.AddLLMockApi(configure);
    }

    /// <summary>
    /// DEPRECATED: Use MapLLMockApi instead. This method will be removed in a future version.
    /// Maps LLMock API endpoints
    /// </summary>
    [Obsolete("Use MapLLMockApi instead. This method will be removed in v2.0.0.", false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IApplicationBuilder Mapmostlylucid_mockllmapi(
        this IApplicationBuilder app,
        string pattern = "/api/mock",
        bool includeStreaming = true)
    {
        return app.MapLLMockApi(pattern, includeStreaming);
    }
}
