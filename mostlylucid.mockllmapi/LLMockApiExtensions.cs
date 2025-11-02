using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Extension methods for adding LLMock API to ASP.NET Core applications
/// </summary>
public static class LLMockApiExtensions
{
    /// <summary>
    /// Adds LLMock API services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing LLMockApi section</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddLLMockApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LLMockApiOptions>(configuration.GetSection(LLMockApiOptions.SectionName));
        services.AddHttpClient("LLMockApi");
        services.AddScoped<LLMockApiService>();
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
        services.AddHttpClient("LLMockApi");
        services.AddScoped<LLMockApiService>();
        return services;
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
            async (HttpContext ctx, string path, LLMockApiService service, ILogger<LLMockApiService> logger) =>
            {
                try
                {
                    var method = ctx.Request.Method;
                    var query = ctx.Request.QueryString.Value;
                    var body = await service.ReadBodyAsync(ctx.Request);
                    var shape = service.ExtractShapeAndCacheCount(ctx.Request, body, out var cacheCount);

                    // Use service-level caching if requested via $cache inside shape
                    var content = await service.GetResponseWithCachingAsync(method, $"{pattern}/{path}{query}", body, shape, cacheCount);

                    // Optionally include schema in header
                    service.TryAddSchemaHeader(ctx, shape);

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
            });

        // Streaming endpoint
        if (includeStreaming)
        {
            var streamPattern = $"{pattern.TrimEnd('/')}/stream/{{**path}}";

            routeBuilder.MapMethods(streamPattern, new[] { "GET", "POST", "PUT", "DELETE", "PATCH" },
                async (HttpContext ctx, string path, LLMockApiService service, ILogger<LLMockApiService> logger) =>
                {
                    try
                    {
                        var method = ctx.Request.Method;
                        var query = ctx.Request.QueryString.Value;
                        var body = await service.ReadBodyAsync(ctx.Request);
                        var shape = service.ExtractShape(ctx.Request, body);
                        var prompt = service.BuildPrompt(method, $"{pattern}/stream/{path}{query}", body, shape, streaming: true);

                        ctx.Response.StatusCode = 200;
                        ctx.Response.Headers.CacheControl = "no-cache";
                        ctx.Response.Headers.Connection = "keep-alive";
                        ctx.Response.ContentType = "text/event-stream";

                        // Optionally include schema in header before any writes
                        service.TryAddSchemaHeader(ctx, shape);

                        using var client = service.CreateHttpClient();
                        var req = service.BuildChatRequest(prompt, stream: true);
                        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                        {
                            Content = JsonContent.Create(req)
                        };
                        using var httpRes = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
                        httpRes.EnsureSuccessStatusCode();

                        await using var stream = await httpRes.Content.ReadAsStreamAsync(ctx.RequestAborted);
                        using var reader = new StreamReader(stream);

                        var accumulated = new System.Text.StringBuilder();

                        while (!ctx.RequestAborted.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync();
                            if (line == null) break;
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                            {
                                var payload = line.Substring(5).Trim();
                                if (payload == "[DONE]")
                                {
                                    var finalJson = accumulated.ToString();
                                    // Include schema in final event payload if enabled
                                    object finalPayload;
                                    if (service.ShouldIncludeSchema(ctx.Request) && !string.IsNullOrWhiteSpace(shape))
                                    {
                                        finalPayload = new { content = finalJson, done = true, schema = shape };
                                    }
                                    else
                                    {
                                        finalPayload = new { content = finalJson, done = true };
                                    }
                                    await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(finalPayload)}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                    break;
                                }

                                try
                                {
                                    using var doc = JsonDocument.Parse(payload);
                                    var root = doc.RootElement;

                                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                                    {
                                        var choice = choices[0];
                                        string? chunk = null;

                                        if (choice.TryGetProperty("delta", out var delta) &&
                                            delta.TryGetProperty("content", out var deltaContent))
                                        {
                                            chunk = deltaContent.GetString();
                                        }
                                        else if (choice.TryGetProperty("message", out var msg) &&
                                                 msg.TryGetProperty("content", out var msgContent))
                                        {
                                            chunk = msgContent.GetString();
                                        }

                                        if (!string.IsNullOrEmpty(chunk))
                                        {
                                            accumulated.Append(chunk);
                                            await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { chunk, done = false })}\n\n");
                                            await ctx.Response.Body.FlushAsync();
                                        }
                                    }
                                }
                                catch
                                {
                                    // Skip malformed chunks
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing LLMock API streaming request");
                        await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n");
                        await ctx.Response.Body.FlushAsync();
                    }
                });
        }

        return app;
    }
}

internal struct ChatCompletionLite
{
    public ChoiceLite[] Choices { get; set; }

    public string? FirstContent => Choices != null && Choices.Length > 0
        ? Choices[0].Message.Content
        : null;
}

internal struct ChoiceLite
{
    public MessageLite Message { get; set; }
}

internal struct MessageLite
{
    public string Content { get; set; }
}
