using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Extension methods for adding LLMock API to ASP.NET Core applications
/// </summary>
public static class LlMockApiExtensions
{
    /// <summary>hich sends out objects as they arrive. 
    /// Adds LLMock API services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing MockLlmApi section</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
        RegisterServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock API services to the service collection with inline configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockApi(
        this IServiceCollection services,
        Action<LLMockApiOptions> configure)
    {
        services.Configure(configure);
        RegisterServices(services);
        return services;
    }

    /// <summary>
    /// Adds LLMock SignalR services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing MockLlmApi section</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockSignalR(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
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
        RegisterSignalRServices(services);
        return services;
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient("LLMockApi");

        // Register all services
        services.AddScoped<ShapeExtractor>();
        services.AddScoped<PromptBuilder>();
        services.AddScoped<LlmClient>();
        services.AddSingleton<CacheManager>();
        services.AddScoped<DelayHelper>();

        // Register request handlers
        services.AddScoped<RegularRequestHandler>();
        services.AddScoped<StreamingRequestHandler>();

        // Register main facade
        services.AddScoped<LLMockApiService>();
    }

    private static void RegisterSignalRServices(IServiceCollection services)
    {
        // Register SignalR components
        services.AddSignalR();
        services.AddSingleton<DynamicHubContextManager>();
        services.AddHostedService<MockDataBackgroundService>();
    }

    /// <summary>
    /// Maps LLMock API endpoints to the application
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="pattern">The route pattern (e.g., "/api/mock" or "/demo")</param>
    /// <param name="includeStreaming">Whether to include streaming endpoint (default: true)</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder MapLLMockApi(
        this IApplicationBuilder app,
        string pattern = "/api/mock",
        bool includeStreaming = true)
    {
        if (app is not IEndpointRouteBuilder routeBuilder)
        {
            throw new InvalidOperationException(
                "MapLLMockApi requires endpoint routing. Call UseRouting() before MapLLMockApi().");
        }

        var autoPattern = $"{pattern.TrimEnd('/')}/{{**path}}";

        // Non-streaming endpoint
        routeBuilder.MapMethods(autoPattern, new[] { "GET", "POST", "PUT", "DELETE", "PATCH" },
            HandleRegularRequest)
            .WithName("LLMockApi-Regular");

        // Streaming endpoint
        if (includeStreaming)
        {
            var streamPattern = $"{pattern.TrimEnd('/')}/stream/{{**path}}";

            routeBuilder.MapMethods(streamPattern, new[] { "GET", "POST", "PUT", "DELETE", "PATCH" },
                HandleStreamingRequest)
                .WithName("LLMockApi-Streaming");
        }

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

        routeBuilder.MapPost(contextPattern, HandleCreateDynamicContext)
            .WithName("LLMockApi-CreateContext");

        routeBuilder.MapGet(contextPattern, HandleListContexts)
            .WithName("LLMockApi-ListContexts");

        routeBuilder.MapGet($"{contextPattern}/{{contextName}}", HandleGetContext)
            .WithName("LLMockApi-GetContext");

        routeBuilder.MapDelete($"{contextPattern}/{{contextName}}", HandleDeleteContext)
            .WithName("LLMockApi-DeleteContext");

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

    private static async Task<IResult> HandleCreateDynamicContext(
        HttpContext ctx,
        DynamicHubContextManager contextManager,
        LlmClient llmClient,
        ILogger<DynamicHubContextManager> logger)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();
            var config = JsonSerializer.Deserialize<Models.HubContextConfig>(json);

            if (config == null || string.IsNullOrWhiteSpace(config.Name))
            {
                return Results.BadRequest(new { error = "Invalid context configuration. Name is required." });
            }

            // If description is provided but no shape, use LLM to generate shape
            if (!string.IsNullOrWhiteSpace(config.Description) && string.IsNullOrWhiteSpace(config.Shape))
            {
                var shapePrompt = $@"Based on this description, generate a JSON schema that defines the data structure.
Description: {config.Description}

Return ONLY valid JSON schema with no additional text. Include:
- type, properties, required fields
- Appropriate data types (string, number, boolean, object, array)
- Clear descriptions for each property

Example format:
{{
  ""type"": ""object"",
  ""properties"": {{
    ""fieldName"": {{
      ""type"": ""string"",
      ""description"": ""Description of field""
    }}
  }},
  ""required"": [""fieldName""]
}}";

                try
                {
                    var shape = await llmClient.GetCompletionAsync(shapePrompt, ctx.RequestAborted);
                    config.Shape = shape;
                    config.IsJsonSchema = true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to generate shape from description, using description as-is");
                    config.Shape = config.Description;
                }
            }

            var success = contextManager.RegisterContext(config);

            if (success)
            {
                return Results.Ok(new
                {
                    message = $"Context '{config.Name}' registered successfully",
                    context = config
                });
            }
            else
            {
                return Results.Conflict(new { error = $"Context '{config.Name}' already exists" });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating dynamic context");
            return Results.Problem(ex.Message);
        }
    }

    private static IResult HandleListContexts(
        DynamicHubContextManager contextManager)
    {
        var contexts = contextManager.GetAllContexts();
        return Results.Ok(new { contexts, count = contexts.Count });
    }

    private static IResult HandleGetContext(
        string contextName,
        DynamicHubContextManager contextManager)
    {
        var context = contextManager.GetContext(contextName);

        if (context != null)
        {
            return Results.Ok(context);
        }
        else
        {
            return Results.NotFound(new { error = $"Context '{contextName}' not found" });
        }
    }

    private static IResult HandleDeleteContext(
        string contextName,
        DynamicHubContextManager contextManager)
    {
        var success = contextManager.UnregisterContext(contextName);

        if (success)
        {
            return Results.Ok(new { message = $"Context '{contextName}' deleted successfully" });
        }
        else
        {
            return Results.NotFound(new { error = $"Context '{contextName}' not found" });
        }
    }
}
