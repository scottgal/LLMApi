using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;
using OllamaSharp;
using OpenAI;
using OpenAI.Chat;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Extension methods for adding LLMock API to ASP.NET Core applications
/// </summary>
public static class LlMockApiExtensions
{
    #region Unified Registration (Backward Compatible)

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

    #endregion

    #region Modular Protocol Registration

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

    #endregion

    #region Service Registration Helpers

    private static void RegisterCoreServices(IServiceCollection services)
    {
        // Only register if not already registered (allows modular usage)
        if (services.All(x => x.ServiceType != typeof(LlmClient)))
        {
            services.AddHttpClient("LLMockApi");

            // Register IChatClient based on provider configuration
            services.AddScoped<IChatClient>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<LLMockApiOptions>>().Value;
                return CreateChatClient(options);
            });

            services.AddScoped<ShapeExtractor>();
            services.AddScoped<PromptBuilder>();
            services.AddScoped<LlmClient>();
            services.AddSingleton<CacheManager>();
            services.AddScoped<DelayHelper>();
            services.AddScoped<LLMockApiService>();
        }
    }

    /// <summary>
    /// Creates an IChatClient based on the configured provider
    /// </summary>
    private static IChatClient CreateChatClient(LLMockApiOptions options)
    {
        return options.Provider switch
        {
            LLMProvider.Ollama => CreateOllamaChatClient(options),
            LLMProvider.OpenAI => CreateOpenAIChatClient(options),
            LLMProvider.LMStudio => CreateLMStudioChatClient(options),
            LLMProvider.AzureOpenAI => CreateAzureOpenAIChatClient(options),
            _ => throw new InvalidOperationException($"Unsupported provider: {options.Provider}")
        };
    }

    private static IChatClient CreateOllamaChatClient(LLMockApiOptions options)
    {
        // OllamaApiClient implements IChatClient directly
        var ollamaClient = new OllamaApiClient(new Uri(options.BaseUrl), options.ModelName);
        return ollamaClient;
    }

    private static IChatClient CreateOpenAIChatClient(LLMockApiOptions options)
    {
        // Get API key from options or environment variable
        var apiKeyString = options.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKeyString))
        {
            throw new InvalidOperationException(
                "OpenAI API key is required. Set it in configuration (ApiKey) or environment variable (OPENAI_API_KEY)");
        }

        // Use the newer OpenAI.Chat.ChatClient pattern
        var apiKey = new ApiKeyCredential(apiKeyString);
        var chatClient = new ChatClient(options.ModelName, apiKey);
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateLMStudioChatClient(LLMockApiOptions options)
    {
        // LMStudio uses OpenAI-compatible API, so we create a ChatClient with custom endpoint
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.BaseUrl)
        };
        var apiKey = new ApiKeyCredential("dummy-key"); // LMStudio doesn't require real API key
        var chatClient = new ChatClient(options.ModelName, apiKey, clientOptions);
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateAzureOpenAIChatClient(LLMockApiOptions options)
    {
        // Get API key from options or environment variable
        var apiKey = options.ApiKey ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Azure OpenAI API key is required. Set it in configuration (ApiKey) or environment variable (AZURE_OPENAI_API_KEY)");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "Valid BaseUrl is required for Azure OpenAI. Set it to your Azure OpenAI endpoint (e.g., https://your-resource.openai.azure.com)");
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(options.BaseUrl),
            new System.ClientModel.ApiKeyCredential(apiKey));

        // Get the chat client for the specific deployment and convert to IChatClient
        var chatClient = azureClient.GetChatClient(options.ModelName);
        return chatClient.AsIChatClient();
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

    #endregion

    #region Unified Mapping (Backward Compatible)

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

    #endregion

    #region Modular Protocol Mapping

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

    #endregion

    #region SignalR Mapping

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

        routeBuilder.MapPost($"{contextPattern}/{{contextName}}/start", HandleStartContext)
            .WithName("LLMockApi-StartContext");

        routeBuilder.MapPost($"{contextPattern}/{{contextName}}/stop", HandleStopContext)
            .WithName("LLMockApi-StopContext");

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

    private static async Task<IResult> HandleCreateDynamicContext(
        HttpContext ctx,
        DynamicHubContextManager contextManager,
        LlmClient llmClient,
        ILogger<DynamicHubContextManager> logger)
    {
        try
        {
            Models.HubContextConfig? config;

            // Check Content-Type to determine how to parse the body
            var contentType = ctx.Request.ContentType?.ToLowerInvariant() ?? "";

            if (contentType.Contains("application/x-www-form-urlencoded"))
            {
                // Parse form-encoded data
                var form = await ctx.Request.ReadFormAsync();
                config = new Models.HubContextConfig
                {
                    Name = form.ContainsKey("name") ? form["name"].ToString() : string.Empty,
                    Description = form.ContainsKey("description") ? form["description"].ToString() : string.Empty,
                    Method = form.ContainsKey("method") && !string.IsNullOrWhiteSpace(form["method"]) ? form["method"].ToString() : "GET",
                    Path = form.ContainsKey("path") && !string.IsNullOrWhiteSpace(form["path"]) ? form["path"].ToString() : "/data",
                    Body = form.ContainsKey("body") && !string.IsNullOrWhiteSpace(form["body"]) ? form["body"].ToString() : null,
                    Shape = form.ContainsKey("shape") && !string.IsNullOrWhiteSpace(form["shape"]) ? form["shape"].ToString() : null
                };
            }
            else
            {
                // Parse JSON data
                using var reader = new StreamReader(ctx.Request.Body);
                var json = await reader.ReadToEndAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                config = JsonSerializer.Deserialize<Models.HubContextConfig>(json, options);
            }

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
                logger.LogInformation("Context registered successfully: {Name}, IsActive={IsActive}, ConnectionCount={Count}",
                    config.Name, config.IsActive, config.ConnectionCount);

                return Results.Ok(new
                {
                    message = $"Context '{config.Name}' registered successfully",
                    context = config
                });
            }
            else
            {
                logger.LogWarning("Context registration failed - already exists: {Name}", config.Name);
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
        DynamicHubContextManager contextManager,
        IConfiguration configuration)
    {
        // Get dynamic contexts registered at runtime
        var dynamicContexts = contextManager.GetAllContexts();

        // Also read configured contexts directly from appsettings to ensure they are always present
        var configured = configuration
            .GetSection($"{LLMockApiOptions.SectionName}:HubContexts")
            .Get<List<mostlylucid.mockllmapi.Models.HubContextConfig>>() ?? new List<mostlylucid.mockllmapi.Models.HubContextConfig>();

        // Merge configured + dynamic, with dynamic taking precedence on name collisions
        var merged = new Dictionary<string, mostlylucid.mockllmapi.Models.HubContextConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in configured)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
            {
                merged[c.Name] = c;
            }
        }
        foreach (var c in dynamicContexts)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
            {
                merged[c.Name] = c; // override configured with dynamic instance
            }
        }

        var contexts = merged.Values.ToList();
        return Results.Ok(new { contexts, count = contexts.Count });
    }

    private static IResult HandleGetContext(
        string contextName,
        DynamicHubContextManager contextManager,
        IConfiguration configuration)
    {
        // First try dynamic runtime contexts
        var context = contextManager.GetContext(contextName);
        if (context != null)
        {
            return Results.Ok(context);
        }

        // Fallback to configured contexts from appsettings.json to ensure visibility even if not dynamically registered
        var configured = configuration
            .GetSection($"{LLMockApiOptions.SectionName}:HubContexts")
            .Get<List<mostlylucid.mockllmapi.Models.HubContextConfig>>()
            ?? new List<mostlylucid.mockllmapi.Models.HubContextConfig>();

        var match = configured.Find(c => !string.IsNullOrWhiteSpace(c.Name) && string.Equals(c.Name, contextName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return Results.Ok(match);
        }

        return Results.NotFound(new { error = $"Context '{contextName}' not found" });
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

    private static IResult HandleStartContext(
        string contextName,
        DynamicHubContextManager contextManager)
    {
        var success = contextManager.StartContext(contextName);

        if (success)
        {
            return Results.Ok(new { message = $"Context '{contextName}' started successfully" });
        }
        else
        {
            return Results.NotFound(new { error = $"Context '{contextName}' not found" });
        }
    }

    private static IResult HandleStopContext(
        string contextName,
        DynamicHubContextManager contextManager)
    {
        var success = contextManager.StopContext(contextName);

        if (success)
        {
            return Results.Ok(new { message = $"Context '{contextName}' stopped successfully" });
        }
        else
        {
            return Results.NotFound(new { error = $"Context '{contextName}' not found" });
        }
    }

    #endregion

    #region Backward Compatibility (Deprecated Method Names)

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

    #endregion
}
